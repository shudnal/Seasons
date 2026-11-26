using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    /// <summary>Forward a native footstep to the owner of the tracked snow piece.</summary>
    internal static class SnowTramplingAction
    {
        private const string RequestRpc = "Seasons_TrampleSnowPiece";

        internal static bool OnStep(WearNTear piece) => SeasonalSnowController.Instance.TrampleSnow(piece);

        internal static void Request(long owner, ZDOID target) =>
            ZRoutedRpc.instance.InvokeRoutedRPC(owner, RequestRpc, target);

        private static void ReceiveRequest(long sender, ZDOID target) =>
            SeasonalSnowController.Instance.ReceiveSnowTrampling(target);

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        private static class ZNetScene_Awake_SnowTrampling
        {
            // The router belongs to this world. The handler does not retain scene objects.
            private static void Postfix() => ZRoutedRpc.instance.Register<ZDOID>(RequestRpc, ReceiveRequest);
        }
    }

    internal sealed partial class SeasonalSnowController
    {
        internal bool TrampleSnow(WearNTear piece)
        {
            float reduction = seasonalSnowFootstepReduction.Value;
            if (!(reduction > 0f) || !snowPieces.TryGetValue(piece, out SnowPiece state))
                return false;
            float minimum = seasonalSnowFootstepsCanClear.Value ? 0f : state.Minimum;
            if (!(state.Snow > minimum))
                return false;

            long owner = state.Zdo.GetOwner();
            if (owner == 0L || state.View.IsOwner())
                return ApplySnowTrampling(state, reduction, minimum);

            SnowTramplingAction.Request(owner, state.Id);

            return true;
        }

        internal void ReceiveSnowTrampling(ZDOID target)
        {
            float reduction = seasonalSnowFootstepReduction.Value;
            if (!(reduction > 0f) || !snowIds.TryGetValue(target, out SnowPiece state) || !state.View.IsOwner())
                return;
            float minimum = seasonalSnowFootstepsCanClear.Value ? 0f : state.Minimum;
            if (state.Snow > minimum)
                ApplySnowTrampling(state, reduction, minimum);
        }

        private bool ApplySnowTrampling(SnowPiece state, float reduction, float minimum)
        {
            // The receive hooks already queue snapshot refreshes on data or owner changes.
            // Skip this step until the existing refresh consumes the new snapshot.
            if ((state.Refresh & SnowRefresh.Snapshot) != 0)
                return false;

            float previous = state.Snow;
            state.Snow = Mathf.Max(minimum, previous - reduction);
            state.Saved = state.AppearanceChosen = true;

            // Rebase the current amount without integrating weather or refreshing the area.
            // A full cap can have an old cursor; that old snowfall must not refill this step.
            double now = ZNet.instance.GetTimeSeconds();
            state.WeatherTime = now;
            state.WeatherGain = SeasonalSnow.GetCumulativeSnowGainAt(state.Biome, now);
            state.LastHeatTime = snowClock;
            state.CatchUpFrom = double.NaN;
            state.AllowInitialPublication = state.AllowOwnerlessPublication = false;
            SeasonalSnowStorage.Write(state.Zdo, state.Snow, state.Epoch, now);
            RememberWrittenSnapshot(state, now);
            Classify(state);
            QueueRuntimeVisual(state, force: state.Snow <= minimum);
            RecordSnowValueChange(state, previous, SnowValueCause.Trampling);
            return true;
        }
    }

    [HarmonyPatch(typeof(FootStep), nameof(FootStep.GetGroundMaterial))]
    internal static class FootStep_GetGroundMaterial_TrampleSeasonalSnow
    {
        [HarmonyPriority(Priority.Last)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
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

            // Reuse the WearNTear already on the stack. A trampled step uses the native snow effect;
            // otherwise keep the receiver for the original material switch.
            Label originalMaterial = generator.DefineLabel();
            CodeInstruction duplicate = new CodeInstruction(OpCodes.Dup);
            duplicate.labels.AddRange(codes[index].labels);
            duplicate.blocks.AddRange(codes[index].blocks);
            codes[index].labels.Clear();
            codes[index].blocks.Clear();
            codes[index].labels.Add(originalMaterial);
            codes.InsertRange(index, new[]
            {
                duplicate,
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SnowTramplingAction), nameof(SnowTramplingAction.OnStep))),
                new CodeInstruction(OpCodes.Brfalse, originalMaterial),
                new CodeInstruction(OpCodes.Pop),
                new CodeInstruction(OpCodes.Ldc_I4, (int)FootStep.GroundMaterial.Snow),
                new CodeInstruction(OpCodes.Ret)
            });
            return codes;
        }
    }
}
