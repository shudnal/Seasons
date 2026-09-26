using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Seasons
{
    // Cooperative ZDO election, not a server arbiter or a replacement for native ownership.
    public partial class IceFloeClimb
    {
        public enum FloeAuthorityMode
        {
            Idle, NativeOwner, NativeReplica, Candidate, WaitingForCandidate,
            LeaseOwner, LeaseReplica, VisualOnly, OutOfRange, Paused, Disabled, Fault
        }

        [Header("Fallback simulation (shared runtime setting, this peer only)")]
        public static bool EnableFallbackSimulation = true;

        private const double CandidateWaitSeconds = 1.0;
        private const double LeaseTimeoutSeconds = 3.0;
        private const double PublishIntervalSeconds = 0.1;
        private const double ClockToleranceSeconds = 1.0;
        private static readonly int candidateKey = "SeasonsFloeCandidate".GetStableHashCode();
        private static readonly int candidateTokenKey = "SeasonsFloeCandidateToken".GetStableHashCode();
        private static readonly int simulatorKey = "SeasonsFloeSimulator".GetStableHashCode();
        private static readonly int simulatorTokenKey = "SeasonsFloeSimulatorToken".GetStableHashCode();
        private static readonly int simulatorHeartbeatKey = "SeasonsFloeHeartbeat".GetStableHashCode();
        private static readonly int simulatorNativeRevisionKey = "SeasonsFloeNativeRevision".GetStableHashCode();

        [Header("Live authority diagnostics (local observations)")]
        public FloeAuthorityMode AuthorityMode;
        public string AuthorityReason = "Not registered";
        public long LocalSimulationPeer, CandidatePeer, CandidateToken, SimulatorPeer, SimulatorToken, SimulatorHeartbeat;
        public float CandidateAge, CandidateStableTime, HeartbeatAge;
        public double AuthorityWorldSeconds;
        public long CandidateAnnouncements, LeaseAcquisitions, LeaseReleases, PosePublications;
        public bool FallbackSimulator { get; private set; }

        private long localCandidateToken, localLeaseToken;
        private ushort candidateNativeRevision;
        private long observedCandidatePeer, observedCandidateToken;
        private double candidateStableSince = -1.0, candidatePulseAt, nextProposalAt = -1.0;
        private double lastAuthorityTime = -1.0, nextPoseAt, lastPhysicsTime = -1.0;
        private double electionPhase;
        private bool candidatePulsed, leasePoseReady;
        private uint replicaPoseRevision;
        private long replicaPoseToken;
        private double replicaPoseObservedAt;

        private struct LeaseRecord
        {
            internal long Candidate, CandidateToken, Simulator, Token, Heartbeat;
            internal int NativeRevision;
        }

        public long LeaseRecordReads, LeaseRecordReuses, PosePublishNotDue;
        private ZDO cachedLeaseZdo;
        private ZDOID cachedLeaseId;
        private uint cachedLeaseRevision;
        private bool cachedLeaseValid;
        private LeaseRecord cachedLease;

        private LeaseRecord ReadLease(ZDO zdo)
        {
            // Cache decoded fields, NEVER the right to simulate. Native ownership and the
            // current server clock are still checked by LiveLease immediately before use.
            // Object identity plus UID protects against pooled ZDO reuse. Every normal
            // local Set/remote data update changes DataRevision before the next callback.
            if (cachedLeaseValid && ReferenceEquals(cachedLeaseZdo, zdo) &&
                cachedLeaseId == zdo.m_uid && cachedLeaseRevision == zdo.DataRevision)
            {
                LeaseRecordReuses++;
                return cachedLease;
            }
            cachedLease = new LeaseRecord
            {
                Candidate = zdo.GetLong(candidateKey), CandidateToken = zdo.GetLong(candidateTokenKey),
                Simulator = zdo.GetLong(simulatorKey), Token = zdo.GetLong(simulatorTokenKey),
                Heartbeat = zdo.GetLong(simulatorHeartbeatKey), NativeRevision = zdo.GetInt(simulatorNativeRevisionKey)
            };
            cachedLeaseZdo = zdo;
            cachedLeaseId = zdo.m_uid;
            cachedLeaseRevision = zdo.DataRevision;
            cachedLeaseValid = true;
            LeaseRecordReads++;
            return cachedLease;
        }

        private static bool TryAuthorityTime(out double now)
        {
            now = ZNet.instance ? ZNet.instance.GetTimeSeconds() : double.NaN;
            // Full, synchronized server world time. Never wrapped wave time, local Time.time,
            // wall-clock time, or a per-machine stopwatch. Tokens use milliseconds of this clock.
            return !double.IsNaN(now) && !double.IsInfinity(now) && now >= 0.0 && now < long.MaxValue / 1000.0;
        }

        private static bool Recent(double now, double then, double lifetime) =>
            then >= 0.0 && now - then >= -ClockToleranceSeconds && now - then <= lifetime;

        private static bool LiveLease(ZDO zdo, LeaseRecord lease, double now) =>
            !zdo.HasOwner() && lease.Simulator != 0 && lease.Token > 0 &&
            lease.NativeRevision == zdo.OwnerRevision && Recent(now, lease.Heartbeat, LeaseTimeoutSeconds);

        private bool TryLiveAuthorityZdo(out ZDO zdo)
        {
            zdo = m_view && m_view.IsValid() ? m_view.GetZDO() : null;
            return zdo != null && zdo.IsValid() && ZDOMan.instance != null &&
                ReferenceEquals(ZDOMan.instance.GetZDO(zdo.m_uid), zdo);
        }

        private void InitializeSimulationAuthority()
        {
            cachedLeaseValid = false;
            cachedLeaseZdo = null;
            LocalSimulationPeer = ZDOMan.GetSessionID();
            ulong peer = unchecked((ulong)LocalSimulationPeer);
            uint hash = unchecked((uint)m_view.GetZDO().m_uid.GetHashCode());
            hash ^= (uint)peer ^ (uint)(peer >> 32);
            hash ^= hash >> 16;
            hash = unchecked(hash * 0x7feb352du);
            hash ^= hash >> 15;
            electionPhase = (hash & 1023u) / 1024.0;
            localCandidateToken = localLeaseToken = observedCandidatePeer = observedCandidateToken = 0;
            candidateStableSince = nextProposalAt = lastAuthorityTime = lastPhysicsTime = -1.0;
            FallbackSimulator = leasePoseReady = candidatePulsed = false;
            replicaPoseToken = 0;
            AuthorityMode = FloeAuthorityMode.Idle;
            AuthorityReason = "Waiting for authority observation";
        }

        private bool WithinFallbackRange()
        {
            Camera camera = GameCamera.instance ? GameCamera.instance.m_camera : null;
            if (!camera || !ZNet.instance || ZNet.instance.IsDedicated() || SeasonalIceFloeWaves.WaterDistance <= 0f ||
                !Finite(Body.position) || !Finite(camera.farClipPlane) || camera.farClipPlane <= 0f || BeyondWater())
                return false;
            return (camera.transform.position - Body.position).sqrMagnitude <= camera.farClipPlane * camera.farClipPlane;
        }

        private bool HasPhysicsAuthority => m_view && m_view.IsValid() &&
            (m_view.IsOwner() || HasCurrentFallbackLease());
        private long PhysicsOwner => m_view.IsOwner() ? m_view.GetZDO().GetOwner() : LocalSimulationPeer;
        private long PhysicsAuthorityToken => m_view.IsOwner() ? 0 : localLeaseToken;

        private bool HasCurrentFallbackLease()
        {
            if (!FallbackSimulator || !EnableFallbackSimulation || Distant || !WaveValid || !WithinFallbackRange() ||
                Game.IsPaused() || Time.timeScale <= 0f || !TryAuthorityTime(out double now))
                return false;
            ZDO zdo = m_view.GetZDO();
            LeaseRecord lease = ReadLease(zdo);
            return LiveLease(zdo, lease, now) && lease.Simulator == LocalSimulationPeer && lease.Token == localLeaseToken;
        }

        private void ObserveLease(LeaseRecord lease, double now)
        {
            CandidatePeer = lease.Candidate;
            CandidateToken = lease.CandidateToken;
            SimulatorPeer = lease.Simulator;
            SimulatorToken = lease.Token;
            SimulatorHeartbeat = lease.Heartbeat;
            CandidateAge = lease.CandidateToken > 0 ? (float)(now - lease.CandidateToken / 1000.0) : -1f;
            HeartbeatAge = lease.Simulator != 0 ? (float)(now - lease.Heartbeat) : -1f;
            CandidateStableTime = candidateStableSince >= 0.0 ? (float)Math.Max(0.0, now - candidateStableSince) : 0f;
            AuthorityWorldSeconds = now;
        }

        private void StopLocalFallback()
        {
            bool wasActive = FallbackSimulator;
            FallbackSimulator = leasePoseReady = false;
            localLeaseToken = 0;
            lastPhysicsTime = -1.0;
            if (wasActive)
                diagnosticStepPending = false;
            if (wasActive && Body && Sync)
            {
                // Native sync must be allowed to process this very frame after a handoff.
                Sync.m_lastUpdateFrame = -1;
                if (!m_view || !m_view.IsValid() || !m_view.IsOwner())
                {
                    Body.useGravity = false;
                    StopMotion();
                }
            }
        }

        private void ClearOwnedLeaseFields(ZDO zdo)
        {
            // OnDestroy may be too late to write; never keep or resurrect a pooled ZDO.
            // Nor may an old simulator modify a newly assigned native owner's data.
            if (zdo.HasOwner() && !zdo.IsOwner())
                return;
            LeaseRecord lease = ReadLease(zdo);
            if (lease.Candidate == LocalSimulationPeer && localCandidateToken != 0 && lease.CandidateToken == localCandidateToken)
            {
                zdo.Set(candidateKey, 0L);
                zdo.Set(candidateTokenKey, 0L);
            }
            if (lease.Simulator == LocalSimulationPeer && localLeaseToken != 0 && lease.Token == localLeaseToken)
            {
                zdo.Set(simulatorKey, 0L);
                zdo.Set(simulatorTokenKey, 0L);
                zdo.Set(simulatorHeartbeatKey, 0L);
                zdo.Set(simulatorNativeRevisionKey, 0);
            }
        }

        private void WithdrawSimulationAuthority(string reason, double cooldown = 0.0)
        {
            bool hadClaim = FallbackSimulator || localCandidateToken != 0;
            if (hadClaim && TryLiveAuthorityZdo(out ZDO zdo))
                ClearOwnedLeaseFields(zdo);
            StopLocalFallback();
            localCandidateToken = observedCandidatePeer = observedCandidateToken = 0;
            candidateStableSince = -1.0;
            CandidateStableTime = 0f;
            nextProposalAt = TryAuthorityTime(out double now) ? now + cooldown + electionPhase * 0.2 : -1.0;
            AuthorityReason = reason;
            if (hadClaim)
                LeaseReleases++;
        }

        // Instance action for controlled handoff experiments, not a native owner release.
        public void ReleaseFallbackSimulation()
        {
            WithdrawSimulationAuthority("Explicit release", LeaseTimeoutSeconds);
            AuthorityMode = FloeAuthorityMode.Idle;
        }

        private static void ClearLeaseForNativeOwner(ZDO zdo, LeaseRecord lease)
        {
            if (lease.Candidate != 0) zdo.Set(candidateKey, 0L);
            if (lease.CandidateToken != 0) zdo.Set(candidateTokenKey, 0L);
            if (lease.Simulator != 0) zdo.Set(simulatorKey, 0L);
            if (lease.Token != 0) zdo.Set(simulatorTokenKey, 0L);
            if (lease.Heartbeat != 0) zdo.Set(simulatorHeartbeatKey, 0L);
            if (lease.NativeRevision != 0) zdo.Set(simulatorNativeRevisionKey, 0);
        }

        internal void UpdateSimulationAuthority()
        {
            if (!WaveValid || !TryLiveAuthorityZdo(out ZDO zdo) || !TryAuthorityTime(out double now))
            {
                StopLocalFallback();
                AuthorityMode = FloeAuthorityMode.Idle;
                AuthorityReason = "No valid object or server clock";
                return;
            }
            UpdateOwnerlessBodyMode(zdo);
            LeaseRecord lease = ReadLease(zdo);
            ObserveLease(lease, now);
            if (zdo.HasOwner())
            {
                if (zdo.IsOwner())
                {
                    if (FallbackSimulator)
                    {
                        // The same peer remains physical authority. Keep its current pose and
                        // momentum instead of adopting its own older 10 Hz lease publication.
                        Sync.m_wasOwner = true;
                        Sync.m_positionCached = Vector3.negativeInfinity;
                        Sync.m_velocityCached = Vector3.negativeInfinity;
                    }
                    ClearLeaseForNativeOwner(zdo, lease);
                }
                StopLocalFallback();
                localCandidateToken = observedCandidatePeer = observedCandidateToken = 0;
                candidateStableSince = -1.0;
                nextProposalAt = -1.0;
                lastAuthorityTime = now;
                AuthorityMode = zdo.IsOwner() ? FloeAuthorityMode.NativeOwner : FloeAuthorityMode.NativeReplica;
                AuthorityReason = "Native ownership takes precedence";
                return;
            }
            if (lastAuthorityTime >= 0.0 && now < lastAuthorityTime - 0.05)
            {
                WithdrawSimulationAuthority("Server clock moved backwards", CandidateWaitSeconds);
                lastAuthorityTime = now;
                AuthorityMode = FloeAuthorityMode.Idle;
                return;
            }
            lastAuthorityTime = now;
            if (!EnableFallbackSimulation || !isActiveAndEnabled || !m_floating.isActiveAndEnabled || !Sync.isActiveAndEnabled ||
                (Body.isKinematic && !OwnerlessKinematic) || Game.IsPaused() || Time.timeScale <= 0f || !WithinFallbackRange())
            {
                WithdrawSimulationAuthority("Outside active fallback conditions");
                AuthorityMode = !EnableFallbackSimulation ? FloeAuthorityMode.Disabled :
                    Game.IsPaused() || Time.timeScale <= 0f ? FloeAuthorityMode.Paused :
                    BeyondWater() ? FloeAuthorityMode.VisualOnly : FloeAuthorityMode.OutOfRange;
                return;
            }
            if (LiveLease(zdo, lease, now))
            {
                if (FallbackSimulator && lease.Simulator == LocalSimulationPeer && lease.Token == localLeaseToken)
                {
                    AuthorityMode = FloeAuthorityMode.LeaseOwner;
                    AuthorityReason = "Locally held simulation lease";
                    return;
                }
                // Even an unexpired record naming this peer cannot revive an unloaded local
                // simulation. Only a locally completed candidate round can start the driver.
                if (FallbackSimulator || localCandidateToken != 0)
                    WithdrawSimulationAuthority("Observed another live lease", CandidateWaitSeconds);
                AuthorityMode = FloeAuthorityMode.LeaseReplica;
                AuthorityReason = "Following the published simulator";
                return;
            }
            if (FallbackSimulator)
            {
                WithdrawSimulationAuthority("Lease expired or changed", CandidateWaitSeconds);
                AuthorityMode = FloeAuthorityMode.Idle;
                return;
            }

            bool liveCandidate = lease.Candidate != 0 && lease.CandidateToken > 0 &&
                Recent(now, lease.CandidateToken / 1000.0, LeaseTimeoutSeconds);
            if (liveCandidate)
            {
                if (lease.Candidate != observedCandidatePeer || lease.CandidateToken != observedCandidateToken)
                {
                    observedCandidatePeer = lease.Candidate;
                    observedCandidateToken = lease.CandidateToken;
                    candidateStableSince = now;
                }
                CandidateStableTime = (float)Math.Max(0.0, now - candidateStableSince);
                if (lease.Candidate != LocalSimulationPeer || lease.CandidateToken != localCandidateToken)
                {
                    localCandidateToken = 0;
                    AuthorityMode = FloeAuthorityMode.WaitingForCandidate;
                    AuthorityReason = "Another candidate is settling";
                    return;
                }
                if (candidateNativeRevision != zdo.OwnerRevision)
                {
                    WithdrawSimulationAuthority("Native ownership revision changed", CandidateWaitSeconds);
                    return;
                }
                AuthorityMode = FloeAuthorityMode.Candidate;
                AuthorityReason = "Waiting for one unchanged server second";
                // RPC_ZDOData ignores equal DataRevision, it does not elect a winner. One
                // staggered re-announcement reduces equal-revision lockstep before promotion.
                // This is a best-effort election, not consensus under arbitrary network delay.
                if (!candidatePulsed && now >= candidatePulseAt)
                {
                    zdo.IncreaseDataRevision();
                    candidatePulsed = true;
                    CandidateAnnouncements++;
                }
                if (now - candidateStableSince < CandidateWaitSeconds)
                    return;
                if (!TryAdoptFallbackBody(zdo))
                {
                    WithdrawSimulationAuthority("Invalid published body state", LeaseTimeoutSeconds);
                    AuthorityMode = FloeAuthorityMode.Fault;
                    return;
                }
                // No callback between the last observation and these writes yields control.
                // The ZDO transport still provides no atomic compare-and-set across peers.
                localLeaseToken = localCandidateToken;
                zdo.Set(simulatorKey, LocalSimulationPeer);
                zdo.Set(simulatorTokenKey, localLeaseToken);
                zdo.Set(simulatorNativeRevisionKey, (int)zdo.OwnerRevision);
                zdo.Set(simulatorHeartbeatKey, (long)Math.Floor(now));
                FallbackSimulator = true;
                leasePoseReady = false;
                lastPhysicsTime = -1.0;
                nextPoseAt = now;
                LeaseAcquisitions++;
                AuthorityMode = FloeAuthorityMode.LeaseOwner;
                AuthorityReason = "Candidate promoted to simulator";
                ObserveLease(ReadLease(zdo), now);
                return;
            }
            localCandidateToken = observedCandidatePeer = observedCandidateToken = 0;
            candidateStableSince = -1.0;
            if (nextProposalAt < 0.0)
                nextProposalAt = now + electionPhase * 0.2;
            AuthorityMode = FloeAuthorityMode.Idle;
            AuthorityReason = "Waiting to announce candidate";
            if (now < nextProposalAt)
                return;
            long token = Math.Max(1L, (long)Math.Floor(now * 1000.0));
            zdo.Set(candidateKey, LocalSimulationPeer);
            zdo.Set(candidateTokenKey, token);
            localCandidateToken = observedCandidateToken = token;
            observedCandidatePeer = LocalSimulationPeer;
            candidateNativeRevision = zdo.OwnerRevision;
            candidateStableSince = now;
            candidatePulseAt = now + 0.2 + electionPhase * 0.5;
            candidatePulsed = false;
            nextProposalAt = -1.0;
            CandidateAnnouncements++;
            AuthorityMode = FloeAuthorityMode.Candidate;
            AuthorityReason = "Candidate announced";
            ObserveLease(ReadLease(zdo), now);
        }

        private bool TryAdoptFallbackBody(ZDO zdo)
        {
            Vector3 position = zdo.GetPosition();
            Quaternion rotation = zdo.GetRotation();
            Vector3 velocity = zdo.GetVec3(ZDOVars.s_bodyVelHash, Vector3.zero);
            Vector3 angularVelocity = zdo.GetVec3(ZDOVars.s_bodyAVelHash, Vector3.zero);
            if (!Finite(position) || !FiniteRotation(rotation) || !Finite(velocity) || !Finite(angularVelocity))
                return false;
            RestoreWaves();
            Body.position = position;
            Body.rotation = rotation;
            if (OwnerlessKinematic)
            {
                KinematicVelocity = velocity;
                KinematicAngularVelocity = angularVelocity;
                Body.useGravity = false;
            }
            else
            {
                Body.linearVelocity = velocity;
                Body.angularVelocity = angularVelocity;
                Body.useGravity = Sync.m_useGravity;
                Body.WakeUp();
            }
            Sync.m_wasOwner = false;
            Sync.m_lastUpdateFrame = -1;
            return true;
        }

        private static bool FiniteRotation(Quaternion value) =>
            Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w) &&
            value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > 0.000001f;

        private void RecordFallbackMotionStep()
        {
            if (!FallbackSimulator)
                return;
            if (Status == WaveStatus.InvalidBody || !TryAuthorityTime(out double now))
            {
                WithdrawSimulationAuthority("Motion update failed", LeaseTimeoutSeconds);
                AuthorityMode = FloeAuthorityMode.Fault;
                return;
            }
            lastPhysicsTime = now;
            leasePoseReady = true;
        }

        internal void PublishFallbackPose()
        {
            if (!leasePoseReady || HoldingGravity || !TryAuthorityTime(out double now) || now - lastPhysicsTime > 0.5)
                return;
            // Most LateUpdate calls are between publications. Do the cheap deadline check
            // before camera/range and lease lookups, but revalidate fully before any write.
            if (now < nextPoseAt)
            {
                PosePublishNotDue++;
                return;
            }
            if (!HasCurrentFallbackLease())
                return;
            ZDO zdo = m_view.GetZDO();
            Vector3 position = Body.position;
            Vector3 velocity = OwnerlessKinematic ? KinematicVelocity : Body.linearVelocity;
            Vector3 angularVelocity = OwnerlessKinematic ? KinematicAngularVelocity : Body.angularVelocity;
            Quaternion rotation = Body.rotation;
            if (!Finite(position) || !Finite(velocity) || !Finite(angularVelocity) || !FiniteRotation(rotation))
            {
                WithdrawSimulationAuthority("Invalid pose publication", LeaseTimeoutSeconds);
                return;
            }
            uint revision = zdo.DataRevision;
            bool moved = !zdo.GetPosition().Equals(position);
            zdo.SetPosition(position);
            zdo.SetRotation(rotation);
            zdo.Set(ZDOVars.s_velHash, velocity);
            zdo.Set(ZDOVars.s_bodyVelHash, velocity);
            zdo.Set(ZDOVars.s_bodyAVelHash, angularVelocity);
            zdo.Set(simulatorHeartbeatKey, (long)Math.Floor(now));
            // Position alone does not dirty an ownerless ZDO. Do not rely on a coincident
            // rotation/velocity/heartbeat change to make vertical movement publishable.
            if (moved && zdo.DataRevision == revision)
                zdo.IncreaseDataRevision();
            nextPoseAt = now + PublishIntervalSeconds + electionPhase * 0.02;
            PosePublications++;
        }

        internal bool IsFallbackReplica()
        {
            if (!EnableFallbackSimulation || !WaveValid || FallbackSimulator || Distant || !WithinFallbackRange() ||
                !TryAuthorityTime(out double now))
                return false;
            ZDO zdo = m_view.GetZDO();
            LeaseRecord lease = ReadLease(zdo);
            return LiveLease(zdo, lease, now) && lease.Simulator != LocalSimulationPeer;
        }

        private void AppendAuthorityDiagnostics(StringBuilder builder)
        {
            if (TryLiveAuthorityZdo(out ZDO zdo) && TryAuthorityTime(out double now))
                ObserveLease(ReadLease(zdo), now); // Reads only; hover never claims or renews.
            builder.Append("\nAuthority=").Append(AuthorityMode).Append(" simulatorLocal=").Append(FallbackSimulator);
            builder.Append(" peer=").Append(LocalSimulationPeer).Append(" fallbackEnabled=").Append(EnableFallbackSimulation);
            builder.Append("\nCandidate=").Append(CandidatePeer).Append(" token=").Append(CandidateToken);
            builder.Append(" age/stable=").Append(Number(CandidateAge)).Append('/').Append(Number(CandidateStableTime));
            builder.Append("\nSimulator=").Append(SimulatorPeer).Append(" token=").Append(SimulatorToken);
            builder.Append(" heartbeat=").Append(SimulatorHeartbeat).Append(" age=").Append(Number(HeartbeatAge));
            builder.Append("\nServer seconds=").Append(AuthorityWorldSeconds.ToString("F3", CultureInfo.InvariantCulture));
            builder.Append(" announcements=").Append(CandidateAnnouncements);
            builder.Append(" publications=").Append(PosePublications).Append(" acquired/released=");
            builder.Append(LeaseAcquisitions).Append('/').Append(LeaseReleases).Append(" reason=").Append(AuthorityReason);
            builder.Append("\nLease cache reads/reuses=").Append(LeaseRecordReads).Append('/').Append(LeaseRecordReuses);
            builder.Append(" publishNotDue=").Append(PosePublishNotDue);
        }
    }
}
