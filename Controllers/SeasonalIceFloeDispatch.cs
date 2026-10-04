using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Seasons
{
    // Membership selects work, never permission to simulate or publish. All mutations
    // and callbacks below run on the main thread; wave jobs do not access these lists.
    internal static partial class SeasonalIceFloeWaves
    {
        internal enum DispatchMode { Pending, Fixed, Kinematic, Distant }

        internal sealed class DispatchEntry
        {
            internal readonly IceFloe Controller;
            internal readonly ZDO Zdo;
            internal readonly ZDOID Id;
            internal DispatchMode Mode;
            internal int FixedIndex = -1;
            internal bool Retired, Queued;
            internal ulong Change = 1, PreparedChange;
            internal long PreparedInput = -1, PreparedStep, EnqueuedStep, CompletedStep;

            internal DispatchEntry(IceFloe controller, ZDO zdo)
            {
                Controller = controller;
                Zdo = zdo;
                Id = zdo.m_uid;
            }
        }

        private static readonly Dictionary<ZDOID, DispatchEntry> dispatchById = new Dictionary<ZDOID, DispatchEntry>();
        private static readonly List<DispatchEntry> fixedParticipants = new List<DispatchEntry>();
        private static readonly List<DispatchEntry> framePhase = new List<DispatchEntry>();
        private static readonly List<DispatchEntry> fixedPhase = new List<DispatchEntry>();
        private static readonly List<DispatchEntry> latePhase = new List<DispatchEntry>();
        private static readonly Queue<DispatchEntry> dispatchChanges = new Queue<DispatchEntry>();
        private static int dispatchFrame = -1, dispatchEpoch, kinematicCount, distantCount;
        private static bool dispatchPaused, dispatchFallback, dispatchRunning, fixedPhaseOpen;
        private static long dispatchInput, preparedDispatchInput = -1, fixedStep;
        private static long frameSweeps, frameVisits, transitionVisits, modeChanges, fixedSteps, fixedVisits;

        private static void RegisterDispatch(IceFloe controller)
        {
            ZDO zdo = controller.m_view.GetZDO();
            if (dispatchById.TryGetValue(zdo.m_uid, out DispatchEntry previous))
            {
                if (ReferenceEquals(previous.Controller, controller))
                    UnregisterDispatch(controller);
                else
                    Untrack(previous.Controller);
            }
            DispatchEntry entry = new DispatchEntry(controller, zdo);
            controller.Dispatch = entry;
            dispatchById.Add(entry.Id, entry);
            EnqueueDispatch(entry);
        }

        private static void RemoveFixedParticipant(DispatchEntry entry)
        {
            int index = entry.FixedIndex;
            if (index < 0)
                return;
            int last = fixedParticipants.Count - 1;
            DispatchEntry moved = fixedParticipants[last];
            fixedParticipants[index] = moved;
            moved.FixedIndex = index;
            fixedParticipants.RemoveAt(last);
            entry.FixedIndex = -1;
        }

        private static void UnregisterDispatch(IceFloe controller)
        {
            DispatchEntry entry = controller.Dispatch;
            if (entry == null || entry.Retired)
                return;
            entry.Retired = true;
            RemoveFixedParticipant(entry);
            if (entry.Mode == DispatchMode.Kinematic) kinematicCount--;
            if (entry.Mode == DispatchMode.Distant) distantCount--;
            if (dispatchById.TryGetValue(entry.Id, out DispatchEntry current) && ReferenceEquals(current, entry))
                dispatchById.Remove(entry.Id);
            controller.Dispatch = null;
            // Queued/snapshotted entries are immutable registrations. A later admission
            // of the same component or a pooled ZDO cannot revive this retired record.
        }

        private static bool TryLiveDispatch(DispatchEntry entry, out IceFloe controller)
        {
            controller = entry?.Controller;
            if (entry == null || entry.Retired || !ReferenceEquals(controller.Dispatch, entry))
                return false;
            if (!controller || !controller.WaveValid || entry.Zdo.m_uid != entry.Id ||
                !ReferenceEquals(controller.m_view.GetZDO(), entry.Zdo))
            {
                Untrack(controller);
                return false;
            }
            return true;
        }

        private static void EnqueueDispatch(DispatchEntry entry)
        {
            if (entry == null || entry.Retired || entry.Queued)
                return;
            entry.Queued = true;
            dispatchChanges.Enqueue(entry);
        }

        private static void RequestDispatch(IceFloe controller)
        {
            DispatchEntry entry = controller.Dispatch;
            if (entry == null || entry.Retired)
                return;
            entry.Change++;
            EnqueueDispatch(entry);
        }

        private static void NetworkDispatchChanged(ZDO zdo)
        {
            // No component lookup, state restoration, authority decision, or ZDO write
            // here. SetOwnerInternal can run BEFORE OwnerRevision/payload completion.
            if (dispatchById.Count == 0 || zdo == null ||
                !dispatchById.TryGetValue(zdo.m_uid, out DispatchEntry entry) ||
                entry.Retired || !ReferenceEquals(entry.Zdo, zdo))
                return;
            entry.Change++;
            EnqueueDispatch(entry);
        }

        private static void InvalidateDispatchInputs() => dispatchInput++;

        private static void QueueFixedVisit(DispatchEntry entry)
        {
            if (!fixedPhaseOpen || entry.FixedIndex < 0 || entry.CompletedStep == fixedStep || entry.EnqueuedStep == fixedStep)
                return;
            entry.EnqueuedStep = fixedStep;
            fixedPhase.Add(entry);
        }

        private static void ReconcileDispatch(DispatchEntry entry)
        {
            if (!TryLiveDispatch(entry, out IceFloe controller))
                return;
            DispatchMode mode = controller.Distant ? DispatchMode.Distant :
                controller.OwnerlessKinematic ? DispatchMode.Kinematic : DispatchMode.Fixed;
            if (entry.Mode != mode)
            {
                if (entry.Mode == DispatchMode.Kinematic) kinematicCount--;
                if (entry.Mode == DispatchMode.Distant) distantCount--;
                if (mode == DispatchMode.Kinematic) kinematicCount++;
                if (mode == DispatchMode.Distant) distantCount++;
                if (mode == DispatchMode.Fixed)
                {
                    entry.FixedIndex = fixedParticipants.Count;
                    fixedParticipants.Add(entry);
                }
                else
                    RemoveFixedParticipant(entry);
                if (entry.Mode != DispatchMode.Pending)
                    modeChanges++;
                entry.Mode = mode;
            }
            if (mode != DispatchMode.Fixed)
                controller.ObservePassiveDispatch();
            // Newly promoted entries can join this very fixed pass, but never receive
            // a second ClientSync/force pass after an already completed visit.
            QueueFixedVisit(entry);
        }

        private static void PrepareDispatch(DispatchEntry entry, bool fixedPreparation)
        {
            if (!TryLiveDispatch(entry, out IceFloe controller))
                return;
            ulong change = entry.Change;
            long input = dispatchInput;
            if (entry.PreparedChange != change || entry.PreparedInput != input)
                controller.InvalidateDispatchPreparation();
            try
            {
                controller.BeforeSync();
                entry.PreparedChange = change;
                entry.PreparedInput = input;
                if (fixedPreparation)
                    entry.PreparedStep = fixedStep;
            }
            catch
            {
                EnqueueDispatch(entry);
                throw;
            }
            finally
            {
                ReconcileDispatch(entry);
            }
        }

        private static void SnapshotDispatch(List<DispatchEntry> destination)
        {
            destination.Clear();
            for (int i = 0; i < participants.Count; ++i)
            {
                DispatchEntry entry = participants[i].Dispatch;
                if (entry != null && !entry.Retired)
                    destination.Add(entry);
            }
        }

        private static void PrepareDispatchFrame()
        {
            int frame = Time.frameCount;
            bool paused = Game.IsPaused() || Time.timeScale <= 0f;
            bool fallback = IceFloe.EnableFallbackSimulation;
            if (dispatchFrame == frame && dispatchPaused == paused && dispatchFallback == fallback &&
                preparedDispatchInput == dispatchInput)
                return;
            int epoch = dispatchEpoch;
            dispatchFrame = frame;
            dispatchPaused = paused;
            dispatchFallback = fallback;
            preparedDispatchInput = dispatchInput;
            frameSweeps++;
            try
            {
                SnapshotDispatch(framePhase);
                for (int i = 0; i < framePhase.Count && epoch == dispatchEpoch; ++i)
                {
                    frameVisits++;
                    PrepareDispatch(framePhase[i], fixedPreparation: true);
                }
            }
            catch
            {
                dispatchFrame = -1;
                throw;
            }
            finally { framePhase.Clear(); }
        }

        private static void DrainDispatchChanges(bool fixedPreparation)
        {
            int epoch = dispatchEpoch;
            // Bound each drain to its entry set. Notifications caused by preparation
            // are retained for the next drain rather than recursing into that callback.
            int count = dispatchChanges.Count;
            while (count-- > 0 && dispatchChanges.Count != 0 && epoch == dispatchEpoch)
            {
                DispatchEntry entry = dispatchChanges.Dequeue();
                entry.Queued = false;
                if (entry.Retired || (entry.PreparedChange == entry.Change && entry.PreparedInput == dispatchInput))
                    continue;
                transitionVisits++;
                PrepareDispatch(entry, fixedPreparation);
            }
        }

        internal static void FixedStep(float dt)
        {
            if (dispatchRunning)
                return;
            dispatchRunning = true;
            int epoch = dispatchEpoch;
            fixedStep++;
            fixedSteps++;
            try
            {
                PrepareDispatchFrame();
                DrainDispatchChanges(fixedPreparation: true);
                if (epoch != dispatchEpoch)
                    return;
                fixedPhase.Clear();
                fixedPhaseOpen = true;
                for (int i = 0; i < fixedParticipants.Count; ++i)
                    QueueFixedVisit(fixedParticipants[i]);
                int cursor = 0;
                while (epoch == dispatchEpoch)
                {
                    DrainDispatchChanges(fixedPreparation: true);
                    if (epoch != dispatchEpoch || cursor >= fixedPhase.Count)
                        break;
                    DispatchEntry entry = fixedPhase[cursor++];
                    if (entry.FixedIndex < 0 || entry.CompletedStep == fixedStep ||
                        !TryLiveDispatch(entry, out IceFloe controller))
                    {
                        entry.EnqueuedStep = 0;
                        continue;
                    }
                    try
                    {
                        // Keep this entry marked enqueued during preparation so its
                        // membership reconciliation cannot append a duplicate visit.
                        if (entry.PreparedStep != fixedStep)
                            PrepareDispatch(entry, fixedPreparation: true);
                        if (entry.FixedIndex < 0 || !TryLiveDispatch(entry, out controller))
                            continue;
                        entry.CompletedStep = fixedStep;
                        fixedVisits++;
                        controller.Sync.ClientSync(dt);
                        if (!TryLiveDispatch(entry, out controller))
                            continue;
                        // Keep the existing second refresh and live authority checks
                        // after native/foreign sync. Membership is not cached permission.
                        controller.SimulatePhysics(dt);
                    }
                    finally
                    {
                        entry.EnqueuedStep = 0;
                        ReconcileDispatch(entry);
                    }
                }
            }
            finally
            {
                fixedPhaseOpen = false;
                fixedPhase.Clear();
                dispatchRunning = false;
            }
        }

        internal static void LateStep()
        {
            if (dispatchRunning)
                return;
            dispatchRunning = true;
            int epoch = dispatchEpoch;
            try
            {
                SnapshotDispatch(latePhase);
                for (int i = 0; i < latePhase.Count && epoch == dispatchEpoch; ++i)
                {
                    DispatchEntry entry = latePhase[i];
                    if (!TryLiveDispatch(entry, out IceFloe controller))
                        continue;
                    bool acquiring = controller.m_view.IsOwner() && !controller.Sync.m_wasOwner;
                    PrepareDispatch(entry, fixedPreparation: false);
                    if (!TryLiveDispatch(entry, out controller))
                        continue;
                    try
                    {
                        if (Time.time >= controller.NextTerrainCheckTime)
                        {
                            controller.NextTerrainCheckTime = Time.time + 30f;
                            if (!controller.Distant && !controller.Body.isKinematic)
                                controller.m_floating.TerrainCheck();
                        }
                        if (!TryLiveDispatch(entry, out controller))
                            continue;
                        if (controller.Distant)
                        {
                            if (SeasonalIceFloeBatching.Configured || SeasonalIceFloeBatching.BatchedCount != 0)
                                SeasonalIceFloeBatching.Reconcile(controller);
                            continue;
                        }
                        controller.UpdateOwnerlessMotion();
                        if (!TryLiveDispatch(entry, out controller))
                            continue;
                        bool heldOnAcquire = acquiring && (controller.HoldingGravity || controller.RecoveryPending);
                        controller.Sync.OwnerSync();
                        if (!TryLiveDispatch(entry, out controller))
                            continue;
                        if (heldOnAcquire)
                        {
                            controller.StopMotion();
                            controller.RecoveryPending = false;
                        }
                        controller.PublishFallbackPose();
                        if (TryLiveDispatch(entry, out controller) &&
                            (SeasonalIceFloeBatching.Configured || SeasonalIceFloeBatching.BatchedCount != 0))
                            SeasonalIceFloeBatching.Reconcile(controller);
                    }
                    finally { ReconcileDispatch(entry); }
                }
                DrainDispatchChanges(fixedPreparation: false);
            }
            finally
            {
                latePhase.Clear();
                dispatchRunning = false;
            }
        }

        internal static string GetDispatchStatus() =>
            $"dispatch total/fixed/kinematic/distant={dispatchById.Count}/{fixedParticipants.Count}/{kinematicCount}/{distantCount} " +
            $"pending={dispatchChanges.Count} transitions={modeChanges} " +
            $"sweeps/visits/events={frameSweeps}/{frameVisits}/{transitionVisits} fixed steps/visits={fixedSteps}/{fixedVisits}";

        private static void ResetDispatch()
        {
            dispatchEpoch++;
            foreach (DispatchEntry entry in dispatchById.Values)
                entry.Retired = true;
            dispatchById.Clear();
            fixedParticipants.Clear();
            dispatchChanges.Clear();
            framePhase.Clear();
            fixedPhase.Clear();
            latePhase.Clear();
            kinematicCount = distantCount = 0;
            dispatchFrame = -1;
            preparedDispatchInput = -1;
            dispatchInput = 0;
            frameSweeps = frameVisits = transitionVisits = modeChanges = fixedSteps = fixedVisits = 0;
            // Do not clear dispatchRunning in a reentrant world reset: the active
            // invocation owns that guard until its finally block has unwound.
        }

        [HarmonyPatch]
        private static class ZDO_FloeDispatchChanges
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(ZDO), nameof(ZDO.SetOwnerInternal));
                yield return AccessTools.Method(typeof(ZDO), nameof(ZDO.Deserialize));
            }

            [HarmonyPostfix]
            private static void Postfix(ZDO __instance) => NetworkDispatchChanged(__instance);
        }
    }

    public partial class IceFloe
    {
        internal SeasonalIceFloeWaves.DispatchEntry Dispatch;

        internal void ObservePassiveDispatch()
        {
            LastForceCalls = 0;
            if (statePaused)
            {
                diagnosticStepPending = false;
                Status = WaveStatus.Paused;
            }
            else if (Distant)
                Status = WaveStatus.Distant;
            // LastRunFixedTime now describes only an actual SimulatePhysics call.
        }

        internal void InvalidateDispatchPreparation()
        {
            // Invalidate observations, not ownership/leases or the surface forecast.
            stateFrame = authorityFrame = -1;
        }
    }
}
