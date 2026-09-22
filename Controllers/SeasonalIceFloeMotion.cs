using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    /// <summary>Physics safety is immediate; optional wave and visual work is centrally budgeted.</summary>
    internal static class SeasonalIceFloeMotion
    {
        internal const int StateVisitsPerFrame = 64;
        internal const int WaveTargetsPerFrame = 16;
        internal const int ForceFloesPerFrame = 8;
        internal const int VisualTransformsPerFrame = 64;
        internal const double WorkMilliseconds = 0.75;
        internal const float ModeInterval = 0.2f;
        internal const float NearWaveInterval = 0.1f;
        internal const float FarWaveInterval = 0.25f;
        private const float MissingLiquid = -10000f;

        private sealed class State
        {
            internal Floating Floating;
            internal ZNetView View;
            internal ZSyncTransform Sync;
            internal Rigidbody Body;
            internal Transform Root;
            internal Collider Collider;
            internal WaterVolume Water;
            internal SeasonalIceFloeVisual Visual;
            internal bool Gravity, Kinematic, SyncGravity, SyncKinematic;
            internal bool Physical, Returning, Applied, WaterObserved, VisualEligible, RestoreVisuals;
            internal long Owner;
            internal float NextMode, NextWave, LastForce;
            internal float Depth = 1f;
            internal int Index;

            internal void RestoreProperties()
            {
                if (!Applied)
                    return;
                if (Body)
                {
                    Body.isKinematic = Kinematic;
                    Body.useGravity = Gravity;
                }
                if (Sync)
                {
                    Sync.m_isKinematicBody = SyncKinematic;
                    Sync.m_useGravity = SyncGravity;
                }
                Applied = false;
            }

            internal void Freeze()
            {
                if (!Body || !Sync)
                    return;
                // Set both cached sync policy and body state: OwnerSync restores m_useGravity.
                Sync.m_useGravity = false;
                Sync.m_isKinematicBody = true;
                Body.useGravity = false;
                if (!Body.isKinematic)
                {
                    Body.linearVelocity = Vector3.zero;
                    Body.angularVelocity = Vector3.zero;
                    Body.isKinematic = true;
                }
                Applied = true;
                Physical = false;
            }
        }

        private static readonly List<State> states = new List<State>();
        private static readonly Dictionary<Floating, State> floatingStates = new Dictionary<Floating, State>();
        private static readonly Dictionary<ZSyncTransform, State> syncStates = new Dictionary<ZSyncTransform, State>();
        private static readonly List<Vector3> players = new List<Vector3>();
        private static WaterVolume waveSampler;
        private static int stateCursor, forceCursor, visualCursor, visualPart;
        private static int lastForceFrame = -1;
        private static float nextPlayers;
        internal static float WaterDistance { get; private set; }
        internal static float WaterDistanceSquared { get; private set; }
        private static float enterDistanceSquared, exitDistanceSquared;

        internal static void Initialize(ZoneSystem zone)
        {
            waveSampler = zone.m_zonePrefab.GetComponentInChildren<WaterVolume>(true);
            RefreshDistance();
        }

        internal static void RefreshDistance()
        {
            if (!ZNet.instance || !ZoneSystem.instance)
                return;
            // Match Water.ApplySettings exactly; the gameplay radius is a separate policy.
            WaterDistance = (float)ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance
                * ZoneSystem.instance.m_zoneSize;
            WaterDistanceSquared = WaterDistance * WaterDistance;
            float near = Mathf.Min(WaterDistance, ZoneSystem.instance.m_zoneSize);
            float far = near + ZoneSystem.instance.m_zoneSize * 0.125f;
            enterDistanceSquared = near * near;
            exitDistanceSquared = far * far;
        }

        internal static void Track(Floating floating)
        {
            if (!floating || floatingStates.ContainsKey(floating))
                return;
            ZNetView view = floating.m_nview;
            if (!view || !view.IsValid() || view.GetZDO().GetPrefab() != s_iceFloePrefab ||
                !view.GetZDO().GetBool(SeasonsVars.s_iceFloeWatermark))
                return;
            Rigidbody body = floating.m_body;
            ZSyncTransform sync = floating.GetComponent<ZSyncTransform>();
            if (!body || !sync || !floating.m_collider)
                return;
            State state = new State
            {
                Floating = floating, View = view, Sync = sync, Body = body,
                Root = floating.transform, Collider = floating.m_collider,
                Gravity = body.useGravity, Kinematic = body.isKinematic,
                SyncGravity = sync.m_useGravity, SyncKinematic = sync.m_isKinematicBody,
                Owner = view.GetZDO().GetOwner(), Index = states.Count,
                NextMode = Time.time, NextWave = Time.time + (states.Count % 8) * 0.025f,
                LastForce = Time.fixedTime
            };
            if (ZNet.instance && !ZNet.instance.IsDedicated())
                state.Visual = new SeasonalIceFloeVisual(state.Root);
            floatingStates.Add(floating, state);
            syncStates.Add(sync, state);
            states.Add(state);
            state.Freeze();
        }

        internal static void Untrack(Floating floating)
        {
            if (ReferenceEquals(floating, null) || !floatingStates.TryGetValue(floating, out State state))
                return;
            state.Visual?.Dispose();
            state.RestoreProperties();
            floatingStates.Remove(floating);
            syncStates.Remove(state.Sync);
            int last = states.Count - 1;
            states[state.Index] = states[last];
            states[state.Index].Index = state.Index;
            states.RemoveAt(last);
            visualPart = 0;
        }

        internal static void Reset()
        {
            foreach (State state in states)
            {
                state.Visual?.Dispose();
                state.RestoreProperties();
            }
            states.Clear();
            floatingStates.Clear();
            syncStates.Clear();
            players.Clear();
            waveSampler = null;
            stateCursor = forceCursor = visualCursor = visualPart = 0;
            lastForceFrame = -1;
            nextPlayers = WaterDistance = WaterDistanceSquared = 0f;
            enterDistanceSquared = exitDistanceSquared = 0f;
        }

        private static bool Valid(State state) => state.Floating && state.Floating.isActiveAndEnabled &&
            state.Body && state.Sync && state.View && state.View.IsValid();

        private static bool ValidLevel(float level) => level > MissingLiquid &&
            !float.IsNaN(level) && !float.IsInfinity(level);

        private static bool HasWater(State state) => ValidLevel(state.Floating.m_waterLevel) &&
            (!state.WaterObserved || (state.Water && state.Water.isActiveAndEnabled && state.Water.m_collider &&
                state.Water.m_collider.enabled && state.Water.m_collider.bounds.Contains(state.Root.position)));

        private static float DistanceSquared(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return x * x + z * z;
        }

        private static bool InGameplayRange(State state)
        {
            float limit = state.Physical || state.Returning ? exitDistanceSquared : enterDistanceSquared;
            for (int i = 0; i < players.Count; i++)
                if (DistanceSquared(players[i], state.Root.position) <= limit)
                    return true;
            return false;
        }

        private static void RefreshPlayers(float now)
        {
            if (now < nextPlayers)
                return;
            nextPlayers = now + ModeInterval;
            players.Clear();
            foreach (Player player in Player.s_players)
                if (player && !player.IsDead())
                    players.Add(player.transform.position);
            if (ZNet.instance.IsServer())
                foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                    if (peer.IsReady() && !peer.m_characterID.IsNone())
                        players.Add(peer.GetRefPos());
        }

        private static void ObserveOwner(State state)
        {
            long owner = state.View.GetZDO().GetOwner();
            if (owner == state.Owner)
                return;
            // Never copy a cosmetic pose into the authoritative root on handoff.
            state.Visual?.Restore();
            state.RestoreProperties();
            state.Owner = owner;
            state.Physical = state.Returning = false;
            state.VisualEligible = state.RestoreVisuals = false;
            state.NextMode = 0f;
        }

        private static void SetMode(State state)
        {
            bool near = HasWater(state) && InGameplayRange(state);
            if (!near)
            {
                state.Returning = false;
                state.Freeze();
                return;
            }
            if (state.Physical)
                return;
            if (state.Visual != null && state.Visual.ActiveCount != 0)
            {
                state.Returning = true;
                state.Visual.SetTarget(0f, Quaternion.identity);
                // Optional mode changes restore one visual binding at a time under the
                // transform budget. Only interaction/handoff/unload restore immediately.
                state.RestoreVisuals = state.Visual.IsAligned;
                return;
            }
            state.RestoreProperties();
            // Nonowners follow native kinematic ClientSync, never independent buoyancy.
            if (!state.View.IsOwner())
            {
                state.Sync.m_isKinematicBody = true;
                state.Sync.m_useGravity = false;
                state.Body.useGravity = false;
                state.Body.isKinematic = true;
                state.Applied = true;
            }
            state.Physical = true;
            state.Returning = false;
        }

        private static bool WithinBudget(long start) =>
            (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency < WorkMilliseconds;

        internal static void Update()
        {
            if (states.Count == 0 || !ZNet.instance || Game.IsPaused() || Time.timeScale <= 0f)
                return;
            float now = Time.time;
            RefreshPlayers(now);
            bool showMotion = SeasonState.IsActive && IsTimeForIceFloes();
            long start = Stopwatch.GetTimestamp();
            int samples = 0;
            int visits = Math.Min(states.Count, StateVisitsPerFrame);
            for (int i = 0; i < visits && WithinBudget(start); i++)
            {
                if (stateCursor >= states.Count)
                    stateCursor = 0;
                State state = states[stateCursor++];
                if (!Valid(state))
                {
                    Untrack(state.Floating);
                    continue;
                }
                ObserveOwner(state);
                if (now >= state.NextMode)
                {
                    state.NextMode = now + ModeInterval;
                    SetMode(state);
                }
                if (state.Physical || state.Returning || state.Visual == null)
                    continue;
                Camera camera = GameCamera.instance ? GameCamera.instance.m_camera : null;
                float distance = camera ? DistanceSquared(camera.transform.position, state.Root.position) : float.PositiveInfinity;
                state.VisualEligible = showMotion && camera && distance <= camera.farClipPlane * camera.farClipPlane && state.Visual.Visible;
                if (!state.VisualEligible)
                {
                    state.RestoreVisuals = state.Visual.ActiveCount != 0;
                    continue;
                }
                state.RestoreVisuals = false;
                if (samples < WaveTargetsPerFrame && now >= state.NextWave && waveSampler &&
                    WaterVolume.s_createWaveTangents != null)
                {
                    state.NextWave = now + (distance <= WaterDistanceSquared ? NearWaveInterval : FarWaveInterval);
                    SampleVisual(state);
                    samples++;
                }
            }
        }

        private static float SampleWave(State state, Vector3 point)
        {
            float big = 1f - (float)WorldGenerator.DeepNorthWaveFade(point.x, point.z);
            return waveSampler.CalcWave(point, state.Depth, WaterVolume.s_wrappedDayTimeSeconds, 1f, big);
        }

        private static void SampleVisual(State state)
        {
            Vector3 point = state.Root.position;
            float radius = Mathf.Max(1f, state.Collider.bounds.extents.x);
            float center = SampleWave(state, point);
            float x = SampleWave(state, point + Vector3.right * radius);
            float z = SampleWave(state, point + Vector3.forward * radius);
            Vector3 normal = new Vector3((center - x) / radius, 1f, (center - z) / radius).normalized;
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, normal);
            tilt = Quaternion.Slerp(Quaternion.identity, tilt, 0.35f);
            float meanY = ZoneSystem.instance.m_waterLevel + state.Floating.m_waterLevelOffset -
                (state.Body.worldCenterOfMass.y - point.y);
            float bob = center / (1f + Mathf.Abs(center));
            state.Visual.SetTarget(meanY + bob - point.y, tilt);
        }

        internal static void UpdateVisuals()
        {
            if (states.Count == 0 || !ZNet.instance || ZNet.instance.IsDedicated() ||
                Game.IsPaused() || Time.timeScale <= 0f)
                return;
            long start = Stopwatch.GetTimestamp();
            int operations = 0, visited = 0;
            int visitLimit = Math.Min(states.Count, StateVisitsPerFrame);
            while (operations < VisualTransformsPerFrame && visited < visitLimit && WithinBudget(start))
            {
                if (visualCursor >= states.Count)
                    visualCursor = 0;
                State state = states[visualCursor];
                if (Valid(state) && !state.Physical && state.Visual != null &&
                    (state.RestoreVisuals || state.Returning || state.VisualEligible) && visualPart < state.Visual.Count)
                {
                    if (state.RestoreVisuals)
                        state.Visual.Restore(visualPart++);
                    else
                        state.Visual.Apply(visualPart++, Time.deltaTime);
                    operations++;
                }
                else
                {
                    if (state.Visual != null && state.Visual.ActiveCount == 0)
                        state.RestoreVisuals = false;
                    visualPart = 0;
                    visualCursor++;
                    visited++;
                }
            }
        }

        internal static void AddForces(float dt)
        {
            if (states.Count == 0 || lastForceFrame == Time.frameCount || Game.IsPaused())
                return;
            lastForceFrame = Time.frameCount;
            long start = Stopwatch.GetTimestamp();
            int forces = 0, visits = Math.Min(states.Count, StateVisitsPerFrame);
            for (int i = 0; i < visits && forces < ForceFloesPerFrame && WithinBudget(start); i++)
            {
                if (forceCursor >= states.Count)
                    forceCursor = 0;
                State state = states[forceCursor++];
                if (!Valid(state) || !state.Physical || !state.View.IsOwner() || !HasWater(state))
                    continue;
                float elapsed = Mathf.Clamp(Time.fixedTime - state.LastForce, dt, 0.1f);
                state.LastForce = Time.fixedTime;
                Vector3 wind = WaterVolume.s_globalWindAlpha == 0f ? WaterVolume.s_globalWind1 :
                    Vector4.Lerp(WaterVolume.s_globalWind1, WaterVolume.s_globalWind2, WaterVolume.s_globalWindAlpha);
                Vector3 side = Vector3.Cross(wind, state.Root.up);
                Vector3 center = state.Body.worldCenterOfMass;
                AddProbeForce(state, state.Collider.ClosestPoint(center + wind * 100f), elapsed);
                AddProbeForce(state, state.Collider.ClosestPoint(center - wind * 100f), elapsed);
                AddProbeForce(state, state.Collider.ClosestPoint(center + side * 100f), elapsed);
                AddProbeForce(state, state.Collider.ClosestPoint(center - side * 100f), elapsed);
                forces++;
            }
        }

        private static void AddProbeForce(State state, Vector3 point, float dt)
        {
            float water = Floating.GetLiquidLevel(point);
            // Each edge is independent; a valid center does not validate this probe.
            if (!ValidLevel(water))
                return;
            float delta = point.y - water;
            float amount = 0.5f * Mathf.Clamp01(Mathf.Abs(delta / 4f)) * (dt * 50f) * Mathf.Abs(delta);
            Vector3 force = delta < 0f ? Vector3.up * (amount * 0.6f) : Vector3.down * amount;
            state.Body.AddForceAtPosition(force * 0.02f * state.Body.mass * 0.25f, point, ForceMode.Impulse);
        }

        internal static bool BeforeFloating(Floating floating)
        {
            if (!floatingStates.TryGetValue(floating, out State state))
                return true;
            if (!Valid(state))
                return false;
            ObserveOwner(state);
            if (!state.Physical || !HasWater(state))
            {
                state.Freeze();
                return false;
            }
            return true;
        }

        internal static void BeforeSync(ZSyncTransform sync)
        {
            if (!syncStates.TryGetValue(sync, out State state) || !Valid(state))
                return;
            ObserveOwner(state);
            if (!state.Physical || !HasWater(state))
            {
                state.Freeze();
                // Native ownership transfer restores the new owner's authoritative pose/velocities.
                // Allow that one handoff while keeping gravity off, then freeze in the postfix.
                if (state.View.IsOwner() && !sync.m_wasOwner)
                    state.Body.isKinematic = state.Kinematic;
            }
        }

        internal static void AfterSync(ZSyncTransform sync)
        {
            if (!syncStates.TryGetValue(sync, out State state) || !Valid(state))
                return;
            if (!state.Physical || !HasWater(state))
                state.Freeze();
        }

        internal static void LiquidChanged(Floating floating, float level, LiquidType type, Component liquidObj)
        {
            if (type != LiquidType.Water || !floatingStates.TryGetValue(floating, out State state))
                return;
            if (ValidLevel(level) && liquidObj is WaterVolume water)
            {
                state.Water = water;
                state.WaterObserved = true;
                state.Depth = water.Depth(state.Root.position);
            }
            else if (!ValidLevel(level) && Valid(state))
                state.Freeze();
        }

        internal static void PrepareInteraction(Floating floating)
        {
            if (!floating || !floatingStates.TryGetValue(floating, out State state) || !Valid(state))
                return;
            state.Visual?.Restore();
            state.Returning = false;
            state.NextMode = 0f;
            SetMode(state);
        }

        [HarmonyPatch(typeof(Water), nameof(Water.ApplySettings))]
        private static class Water_ApplySettings_Distance
        {
            private static void Postfix() => RefreshDistance();
        }

        // ApplySettingsOnAll is also called when no Water instance exists (for example on a server).
        [HarmonyPatch(typeof(Water), nameof(Water.ApplySettingsOnAll))]
        private static class Water_ApplySettingsOnAll_Distance
        {
            private static void Postfix() => RefreshDistance();
        }

        [HarmonyPatch(typeof(Floating), nameof(Floating.CustomFixedUpdate))]
        private static class Floating_CustomFixedUpdate_Motion
        {
            private static bool Prefix(Floating __instance) => BeforeFloating(__instance);
        }

        [HarmonyPatch(typeof(Floating), nameof(Floating.OnEnable))]
        private static class Floating_OnEnable_Motion
        {
            private static void Postfix(Floating __instance)
            {
                // First registration waits for Start, after every native Awake has cached
                // its original body policy. Re-enabling just Floating is also supported.
                IceFloeClimb climb = __instance.GetComponent<IceFloeClimb>();
                if (climb && climb.Started)
                    Track(__instance);
            }
        }

        [HarmonyPatch(typeof(Floating), nameof(Floating.OnDisable))]
        private static class Floating_OnDisable_Motion
        {
            private static void Postfix(Floating __instance) => Untrack(__instance);
        }

        [HarmonyPatch(typeof(Floating), nameof(Floating.SetLiquidLevel))]
        private static class Floating_SetLiquidLevel_Motion
        {
            private static void Postfix(Floating __instance, float level, LiquidType type, Component liquidObj) =>
                LiquidChanged(__instance, level, type, liquidObj);
        }

        [HarmonyPatch(typeof(Floating), nameof(Floating.TerrainCheck))]
        private static class Floating_TerrainCheck_Stable
        {
            private static bool Prefix(Floating __instance) =>
                !floatingStates.TryGetValue(__instance, out State state) || state.Physical;
        }

        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.OwnerSync))]
        private static class ZSyncTransform_OwnerSync_Motion
        {
            private static void Prefix(ZSyncTransform __instance) => BeforeSync(__instance);
            private static void Postfix(ZSyncTransform __instance) => AfterSync(__instance);
        }

        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.ClientSync))]
        private static class ZSyncTransform_ClientSync_Motion
        {
            private static void Prefix(ZSyncTransform __instance) => BeforeSync(__instance);
            private static void Postfix(ZSyncTransform __instance) => AfterSync(__instance);
        }

        [HarmonyPatch(typeof(MonoUpdaters), nameof(MonoUpdaters.FixedUpdate))]
        private static class MonoUpdaters_FixedUpdate_Forces
        {
            private static void Postfix() => AddForces(Time.fixedDeltaTime);
        }

        // These native lifecycle paths also run on a headless server, where the
        // seasonal texture controller and a camera need not exist.
        [HarmonyPatch(typeof(MonoUpdaters), nameof(MonoUpdaters.Update))]
        private static class MonoUpdaters_Update_Motion
        {
            private static void Postfix() => Update();
        }

        [HarmonyPatch(typeof(MonoUpdaters), nameof(MonoUpdaters.LateUpdate))]
        private static class MonoUpdaters_LateUpdate_Visuals
        {
            private static void Postfix() => UpdateVisuals();
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
        private static class ZoneSystem_Start_Motion
        {
            private static void Postfix(ZoneSystem __instance) => Initialize(__instance);
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.OnDestroy))]
        private static class ZoneSystem_OnDestroy_Motion
        {
            private static void Prefix() => Reset();
        }
    }
}
