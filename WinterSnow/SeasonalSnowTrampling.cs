using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    /// <summary>Report an owned character's step to the piece owner, without claiming ownership.</summary>
    internal static class SnowTramplingAction
    {
        private const string RequestRpc = "Seasons_TrampleSnow";
        private static ZNetScene scene;
        private static ZRoutedRpc router;
        private static object requestHandler;
        private static long sequence;
        private static ConditionalWeakTable<Character, StepRecord> steps = new ConditionalWeakTable<Character, StepRecord>();

        private sealed class StepRecord
        {
            internal int Frame = -1;
            internal long Sender;
            internal long Sequence;
            internal float Budget = 2f;
            internal float ReceivedAt;
        }

        private static StepRecord CreateStepRecord(Character actor) => new StepRecord();

        internal static void OnStep(WearNTear piece, Character actor)
        {
            if (!SeasonalSnow.WinterReady || !actor || actor.IsDead() || !actor.IsOnGround() ||
                !actor.m_nview || !actor.m_nview.IsValid() || !actor.m_nview.IsOwner() ||
                !SeasonalSnowController.Instance.HasSnowToTrample(piece))
                return;

            StepRecord record = steps.GetValue(actor, CreateStepRecord);
            if (record.Frame == Time.frameCount)
                return;
            record.Frame = Time.frameCount;

            ZDO target = piece.m_nview.GetZDO();
            long owner = target.GetOwner();
            if (owner == 0L || piece.m_nview.IsOwner())
            {
                SeasonalSnowController.Instance.TrampleSnow(piece, actor, remote: false);
                return;
            }
            if (!scene || scene != ZNetScene.instance || router == null || !ReferenceEquals(router, ZRoutedRpc.instance))
                return;

            // Send identities, not an amount or a client-selected lower limit. No reply or retry is needed.
            router.InvokeRoutedRPC(owner, RequestRpc, target.m_uid, actor.m_nview.GetZDO().m_uid, ++sequence);
        }

        private static void ReceiveRequest(long sender, ZDOID targetId, ZDOID actorId, long requestSequence)
        {
            if (!SeasonalSnow.WinterReady || !scene || scene != ZNetScene.instance || router == null ||
                !ReferenceEquals(router, ZRoutedRpc.instance) || ZDOMan.instance == null ||
                sender == 0L || targetId.IsNone() || actorId.IsNone() || requestSequence <= 0L)
                return;

            ZDO actorZdo = ZDOMan.instance.GetZDO(actorId);
            ZNetView actorView = actorZdo != null ? scene.FindInstance(actorZdo) : null;
            if (!actorView || !actorView.IsValid() || actorZdo.GetOwner() != sender)
                return;
            Character actor = actorView.GetComponent<Character>();
            if (!actor || actor.IsDead())
                return;

            StepRecord record = steps.GetValue(actor, CreateStepRecord);
            if (record.Sender == sender && requestSequence <= record.Sequence)
                return;
            record.Sender = sender;
            record.Sequence = requestSequence;
            // Allow a small delivery burst and twice the native walking cadence. Do not accept an unbounded stream.
            float now = Time.unscaledTime;
            record.Budget = Mathf.Min(2f, record.Budget + Mathf.Max(0f, now - record.ReceivedAt) * 10f);
            record.ReceivedAt = now;
            if (record.Budget < 1f)
                return;
            record.Budget -= 1f;

            ZDO targetZdo = ZDOMan.instance.GetZDO(targetId);
            ZNetView targetView = targetZdo != null ? scene.FindInstance(targetZdo) : null;
            // An ownership change in flight rejects the step instead of forwarding or taking ownership.
            if (!targetView || !targetView.IsValid() || !targetView.IsOwner())
                return;
            WearNTear piece = targetView.GetComponent<WearNTear>();
            if (piece)
                SeasonalSnowController.Instance.TrampleSnow(piece, actor, remote: true);
        }

        private static void Register(ZNetScene currentScene)
        {
            if (scene == currentScene && ReferenceEquals(router, ZRoutedRpc.instance))
                return;
            Reset();
            ZRoutedRpc current = ZRoutedRpc.instance;
            if (current == null)
                return;
            int hash = RequestRpc.GetStableHashCode();
            if (current.m_functions.ContainsKey(hash))
            {
                LogWarning("Snow-trampling RPC name is already registered; remote trampling is disabled.");
                return;
            }
            current.Register<ZDOID, ZDOID, long>(RequestRpc, ReceiveRequest);
            scene = currentScene;
            router = current;
            requestHandler = current.m_functions[hash];
        }

        private static void Reset()
        {
            int hash = RequestRpc.GetStableHashCode();
            if (router != null && router.m_functions.TryGetValue(hash, out var handler) && ReferenceEquals(handler, requestHandler))
                router.m_functions.Remove(hash);
            scene = null;
            router = null;
            requestHandler = null;
            steps = new ConditionalWeakTable<Character, StepRecord>();
            // Retain sequence across worlds so an old request cannot match a later step.
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        private static class ZNetScene_Awake_SnowTrampling
        {
            private static void Postfix(ZNetScene __instance) => Register(__instance);
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.OnDestroy))]
        private static class ZNetScene_OnDestroy_SnowTrampling
        {
            private static void Prefix(ZNetScene __instance)
            {
                if (scene == __instance)
                    Reset();
            }
        }
    }

    internal sealed partial class SeasonalSnowController
    {
        private static float FootstepSnowReduction => SeasonalSnowStorage.Sanitize(seasonalSnowFootstepReduction.Value);

        private static float FootstepSnowMinimum(WearNTear piece) => seasonalSnowFootstepsCanClear.Value
            ? 0f : SeasonalSnowStorage.Sanitize(SeasonalSnow.GetSnowBuildupRange(piece).x);

        internal bool HasSnowToTrample(WearNTear piece)
        {
            if (!SeasonalSnow.WinterReady || FootstepSnowReduction <= 0f || !SeasonalSnow.SupportsSeasonalSnow(piece))
                return false;
            return GetSnowValue(piece) > FootstepSnowMinimum(piece);
        }

        internal void TrampleSnow(WearNTear piece, Character actor, bool remote)
        {
            if (!SeasonalSnow.WinterReady || !SeasonalSnow.WeatherReady || endingSnowWinter || !EnsureSnowScene() ||
                !actor || actor.IsDead() || Game.IsPaused() || Time.timeScale <= 0f || !HasSnowToTrample(piece))
                return;
            if (!snowPieces.TryGetValue(piece, out SnowPiece state) || !state.Valid || !state.Confirmed ||
                !state.Region.Ready || state.ReadyGeneration != state.Region.ReadyGeneration ||
                !double.IsNaN(state.CatchUpFrom) || state.Epoch != SeasonalSnowStorage.CurrentWinterEpoch ||
                state.Epoch != winterEpoch || simulationScene.OutsideActiveArea(state.Position) ||
                !SeasonalSnowStorage.CanWrite(state.View, state.Zdo))
                return;
            // Remote ground contact is not replicated. Validate proximity to this piece's cached colliders instead.
            if (remote && !NearTrampledPiece(state, actor.transform.position))
                return;
            double now = ZNet.instance.GetTimeSeconds();
            if (!ContinuousSnowTime(now) || state.Owner != state.Zdo.GetOwner() ||
                !SnapshotUnchanged(state, new SeasonalSnowStorage.Snapshot(state.Zdo)))
            {
                QueueRefresh(state, SnowRefresh.Snapshot);
                return;
            }

            // Consume accumulation/melting first, so the old interval cannot refill a trampled cap after reloading.
            IntegratePiece(state, now, snowClock);
            float previous = state.Snow;
            float minimum = FootstepSnowMinimum(piece);
            if (!state.Valid || !SeasonalSnowStorage.CanWrite(state.View, state.Zdo) || !(previous > minimum))
                return;
            float reduced = Mathf.Max(minimum, previous - FootstepSnowReduction);
            if (!(reduced < previous))
                return;

            state.Snow = reduced;
            state.Saved = state.AppearanceChosen = true;
            state.WeatherTime = now;
            state.WeatherGain = SeasonalSnow.GetCumulativeSnowGainAt(state.Biome, now);
            state.LastHeatTime = snowClock;
            state.CatchUpFrom = double.NaN;
            state.ReplacedWeatherUntil = 0d;
            state.AllowInitialPublication = state.AllowOwnerlessPublication = false;
            RecordSnowValueChange(state, previous, SnowValueCause.Trampling);
            SeasonalSnowStorage.Write(state.Zdo, reduced, state.Epoch, now);
            RememberWrittenSnapshot(state, now);
            Classify(state);
            QueueRuntimeVisual(state, force: reduced <= minimum);
            SeasonalSnowDiagnostics.ClearCache();
        }

        private static bool NearTrampledPiece(SnowPiece state, Vector3 position)
        {
            // Tolerate movement replication delay, including near the edge of a large floor or roof.
            const float squaredDistance = 4f;
            if (state.Colliders != null)
                foreach (Collider collider in state.Colliders)
                    if (collider && collider.enabled && collider.gameObject.activeInHierarchy && !collider.isTrigger &&
                        collider.bounds.SqrDistance(position) <= squaredDistance)
                        return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(FootStep), nameof(FootStep.GetGroundMaterial))]
    internal static class FootStep_GetGroundMaterial_TrampleSeasonalSnow
    {
        [HarmonyPriority(Priority.Last)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();
            FieldInfo material = AccessTools.Field(typeof(WearNTear), nameof(WearNTear.m_materialType));
            int index = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].LoadsField(material))
                    continue;
                if (index >= 0)
                {
                    LogWarning("FootStep.GetGroundMaterial has multiple WearNTear material reads; snow trampling was not patched.");
                    return codes;
                }
                index = i;
            }
            if (index < 0)
            {
                LogWarning("FootStep.GetGroundMaterial WearNTear material branch was not found; snow trampling was not patched.");
                return codes;
            }

            // The native non-null branch already put componentInParent on the stack.
            // Keep it for the existing material switch; do not repeat GetComponentInParent or change the result.
            CodeInstruction duplicate = new CodeInstruction(OpCodes.Dup);
            duplicate.labels.AddRange(codes[index].labels);
            duplicate.blocks.AddRange(codes[index].blocks);
            codes[index].labels.Clear();
            codes[index].blocks.Clear();
            codes.InsertRange(index, new[]
            {
                duplicate,
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SnowTramplingAction), nameof(SnowTramplingAction.OnStep)))
            });
            return codes;
        }
    }
}
