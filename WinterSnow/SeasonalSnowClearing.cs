using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Seasons
{
    internal enum SnowClearResult
    {
        NoSnow,
        Cleared,
        AccessDenied
    }

    /// <summary>One explicit action request to the current owner; no new per-piece RPCs or polling.</summary>
    internal static class SnowClearingAction
    {
        private const string RequestRpc = "Seasons_ClearSnow_Request";
        private const string ResultRpc = "Seasons_ClearSnow_Result";
        private const float RequestTimeout = 3f;
        private static readonly Dictionary<long, Receipt> receipts = new Dictionary<long, Receipt>();
        private static ZRoutedRpc router;
        private static ZNetScene scene;
        private static object requestHandler;
        private static object resultHandler;
        private static Pending pending;
        private static int sequence;

        private sealed class Pending
        {
            internal int Sequence;
            internal long Owner;
            internal ZRoutedRpc Router;
            internal ZNetScene Scene;
            internal Player Player;
            internal ItemDrop.ItemData Tool;
            internal Vector3 Position;
            internal float Stamina;
            internal float Eitr;
            internal float Durability;
        }

        private sealed class Receipt
        {
            internal Player Player;
            internal int Sequence;
            internal ZDOID Target;
            internal SnowClearResult Result;
            internal float NextUse;
        }

        internal static void Clear(Player player, ItemDrop.ItemData tool)
        {
            if (!player || player != Player.m_localPlayer || !player.m_nview || !player.m_nview.IsOwner() ||
                player.IsDead() || player.IsTeleporting() || !player.InPlaceMode() ||
                !SnowClearingTool.IsHoe(tool) || !ReferenceEquals(player.GetRightItem(), tool))
                return;
            if (pending != null)
                return;
            Piece target = player.GetHoveringPiece();
            WearNTear wear = target ? target.GetComponent<WearNTear>() : null;
            if (!wear || !wear.m_nview || !wear.m_nview.IsValid() ||
                SeasonalSnowController.Instance.GetSnowValue(wear) <= 0f)
            {
                ShowResult(player, SnowClearResult.NoSnow);
                return;
            }
            if (!PrivateArea.CheckAccess(target.transform.position))
            {
                ShowResult(player, SnowClearResult.AccessDenied);
                return;
            }
            float stamina = player.GetBuildStamina();
            if (!player.HaveStamina(stamina))
            {
                Hud.instance?.StaminaBarEmptyFlash();
                return;
            }
            float eitr = tool.m_shared.m_attack.m_attackEitr;
            if (eitr > player.GetEitr() || (tool.m_shared.m_useDurability && tool.m_durability <= 0f))
            {
                ShowResult(player, SnowClearResult.NoSnow);
                return;
            }
            ZDO zdo = wear.m_nview.GetZDO();
            var operation = new Pending
            {
                Owner = zdo.GetOwner(), Router = ZRoutedRpc.instance, Scene = ZNetScene.instance,
                Player = player, Tool = tool, Position = target.transform.position,
                Stamina = stamina, Eitr = eitr,
                Durability = tool.m_shared.m_useDurability ? player.GetPlaceDurability(tool) * Game.m_durabilityRate : 0f
            };
            if (operation.Owner == 0L || wear.m_nview.IsOwner())
            {
                Complete(operation, SeasonalSnowController.Instance.ClearSnowManually(wear, player));
                return;
            }
            if (router == null || !ReferenceEquals(router, operation.Router) || scene != operation.Scene)
            {
                ShowResult(player, SnowClearResult.NoSnow);
                return;
            }
            // Only one outstanding local action. A reply from another peer or another world cannot finish it.
            operation.Sequence = ++sequence;
            pending = operation;
            player.m_lastToolUseTime = Time.time;
            Seasons.instance.StartCoroutine(Expire(operation));
            router.InvokeRoutedRPC(operation.Owner, RequestRpc, zdo.m_uid, operation.Sequence);
        }

        private static IEnumerator Expire(Pending operation)
        {
            yield return new WaitForSecondsRealtime(RequestTimeout);
            if (!ReferenceEquals(pending, operation))
                yield break;
            pending = null;
            if (operation.Scene == ZNetScene.instance && operation.Player == Player.m_localPlayer &&
                operation.Player && !operation.Player.IsDead())
                ShowResult(operation.Player, SnowClearResult.NoSnow);
        }

        private static void Complete(Pending operation, SnowClearResult result)
        {
            Player player = operation.Player;
            if (!player || player != Player.m_localPlayer || player.IsDead() || operation.Scene != ZNetScene.instance)
                return;
            if (result != SnowClearResult.Cleared)
            {
                ShowResult(player, result);
                return;
            }
            player.m_lastToolUseTime = Time.time;
            player.UseStamina(operation.Stamina);
            player.UseEitr(operation.Eitr);
            // The saved item reference, never a newly equipped item, pays for the completed action.
            if (player.GetInventory().GetAllItems().Contains(operation.Tool))
                operation.Tool.m_durability = Mathf.Max(0f, operation.Tool.m_durability - operation.Durability);
            if (ReferenceEquals(player.GetRightItem(), operation.Tool))
            {
                player.FaceLookDirection();
                player.m_zanim.SetTrigger(operation.Tool.m_shared.m_attack.m_attackAnimation);
            }
            operation.Tool.m_shared.m_buildEffect?.Create(operation.Position, Quaternion.identity, null, 1f, -1, player.GetZDOID());
        }

        private static void ShowResult(Player player, SnowClearResult result) =>
            player.Message(MessageHud.MessageType.TopLeft, result == SnowClearResult.AccessDenied ? "$msg_privatezone" : "$msg_nosnow");

        private static void ReceiveRequest(long sender, ZDOID target, int requestSequence)
        {
            if (router == null || !ReferenceEquals(router, ZRoutedRpc.instance) || !scene || scene != ZNetScene.instance ||
                sender == 0L || requestSequence <= 0 || target.IsNone())
                return;
            Player actor = null;
            foreach (Player candidate in Player.GetAllPlayers())
                if (candidate && !candidate.IsDead() && candidate.m_nview && candidate.m_nview.IsValid() &&
                    candidate.m_nview.GetZDO().GetOwner() == sender)
                {
                    actor = candidate;
                    break;
                }
            if (!actor)
                return;
            if (!receipts.TryGetValue(sender, out Receipt receipt) || receipt.Player != actor)
                receipts[sender] = receipt = new Receipt { Player = actor };
            if (requestSequence == receipt.Sequence && target == receipt.Target)
            {
                router.InvokeRoutedRPC(sender, ResultRpc, requestSequence, (int)receipt.Result);
                return;
            }
            if (requestSequence <= receipt.Sequence || Time.unscaledTime < receipt.NextUse)
                return;
            receipt.Sequence = requestSequence;
            receipt.Target = target;
            receipt.NextUse = Time.unscaledTime + Mathf.Max(0.2f, actor.m_placeDelay);
            receipt.Result = SnowClearResult.NoSnow;
            ZDO targetZdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(target) : null;
            ZNetView view = targetZdo != null ? scene.FindInstance(targetZdo) : null;
            // Never claim or steal ownership. A handoff in flight rejects the old request instead of forwarding it.
            if (view && view.IsValid() && view.IsOwner() &&
                actor.m_nview.GetZDO().GetInt(ZDOVars.s_rightItem) == SnowClearingTool.HoeHash)
            {
                WearNTear wear = view.GetComponent<WearNTear>();
                if (wear)
                    receipt.Result = SeasonalSnowController.Instance.ClearSnowManually(wear, actor);
            }
            router.InvokeRoutedRPC(sender, ResultRpc, requestSequence, (int)receipt.Result);
        }

        private static void ReceiveResult(long sender, int requestSequence, int result)
        {
            Pending operation = pending;
            if (operation == null || operation.Sequence != requestSequence || operation.Owner != sender ||
                operation.Scene != scene || !ReferenceEquals(operation.Router, router) ||
                !ReferenceEquals(router, ZRoutedRpc.instance) || result < 0 || result > (int)SnowClearResult.AccessDenied)
                return;
            pending = null;
            Complete(operation, (SnowClearResult)result);
        }

        private static void Register(ZNetScene currentScene)
        {
            if (scene == currentScene && ReferenceEquals(router, ZRoutedRpc.instance))
                return;
            Reset();
            ZRoutedRpc current = ZRoutedRpc.instance;
            if (current == null)
                return;
            int requestHash = RequestRpc.GetStableHashCode();
            int resultHash = ResultRpc.GetStableHashCode();
            if (current.m_functions.ContainsKey(requestHash) || current.m_functions.ContainsKey(resultHash))
            {
                Seasons.LogWarning("Snow-clearing RPC names are already registered; remote clearing is disabled.");
                return;
            }
            current.Register<ZDOID, int>(RequestRpc, ReceiveRequest);
            current.Register<int, int>(ResultRpc, ReceiveResult);
            router = current;
            scene = currentScene;
            requestHandler = current.m_functions[requestHash];
            resultHandler = current.m_functions[resultHash];
        }

        private static void Reset()
        {
            if (router != null)
            {
                int requestHash = RequestRpc.GetStableHashCode();
                int resultHash = ResultRpc.GetStableHashCode();
                if (router.m_functions.TryGetValue(requestHash, out var request) && ReferenceEquals(request, requestHandler))
                    router.m_functions.Remove(requestHash);
                if (router.m_functions.TryGetValue(resultHash, out var result) && ReferenceEquals(result, resultHandler))
                    router.m_functions.Remove(resultHash);
            }
            pending = null;
            receipts.Clear();
            router = null;
            scene = null;
            requestHandler = resultHandler = null;
            // Keep request identities monotonic across worlds; late replies must not match a new request.
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Start))]
        private static class Game_Start_SnowClearing
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (ZNetScene.instance)
                    Register(ZNetScene.instance);
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.OnDestroy))]
        private static class ZNetScene_OnDestroy_SnowClearing
        {
            [HarmonyPrefix]
            private static void Prefix(ZNetScene __instance)
            {
                if (scene == __instance)
                    Reset();
            }
        }

        [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.RemovePeer))]
        private static class ZRoutedRpc_RemovePeer_SnowClearing
        {
            [HarmonyPrefix]
            private static void Prefix(ZNetPeer peer)
            {
                if (peer != null)
                    receipts.Remove(peer.m_uid);
            }
        }
    }

    internal sealed partial class SeasonalSnowController
    {
        /// <summary>Consume the old accumulation interval and publish an explicit zero without retiring the piece.</summary>
        internal SnowClearResult ClearSnowManually(WearNTear piece, Player actor)
        {
            if (!SeasonalSnow.WinterReady || !SeasonalSnow.WeatherReady || endingSnowWinter || !EnsureSnowScene() ||
                !piece || !actor || actor.IsDead() || !actor.m_nview || !actor.m_nview.IsValid() ||
                Game.IsPaused() || Time.timeScale <= 0f)
                return SnowClearResult.NoSnow;
            RegisterSnow(piece);
            if (!snowPieces.TryGetValue(piece, out SnowPiece state) || !state.Valid || !state.Confirmed ||
                !state.Region.Ready || state.ReadyGeneration != state.Region.ReadyGeneration ||
                !double.IsNaN(state.CatchUpFrom) || state.Epoch != SeasonalSnowStorage.CurrentWinterEpoch ||
                state.Epoch != winterEpoch || simulationScene.OutsideActiveArea(state.Position) ||
                !SeasonalSnowStorage.CanWrite(state.View, state.Zdo))
                return SnowClearResult.NoSnow;
            if (!InSnowClearingRange(state, actor))
                return SnowClearResult.NoSnow;
            if (!HasSnowClearingAccess(state.Position, actor))
                return SnowClearResult.AccessDenied;
            double now = ZNet.instance.GetTimeSeconds();
            if (!ContinuousSnowTime(now) || state.Owner != state.Zdo.GetOwner() ||
                !SnapshotUnchanged(state, new SeasonalSnowStorage.Snapshot(state.Zdo)))
            {
                QueueRefresh(state, SnowRefresh.Snapshot);
                return SnowClearResult.NoSnow;
            }
            IntegratePiece(state, now, snowClock);
            if (!state.Valid || !SeasonalSnowStorage.CanWrite(state.View, state.Zdo) || !(state.Snow > 0f))
                return SnowClearResult.NoSnow;

            float previous = state.Snow;
            state.Snow = 0f;
            state.Saved = state.AppearanceChosen = true;
            state.WeatherTime = now;
            state.WeatherGain = SeasonalSnow.GetCumulativeSnowGainAt(state.Biome, now);
            state.LastHeatTime = snowClock;
            state.CatchUpFrom = double.NaN;
            state.ReplacedWeatherUntil = 0d;
            state.AllowInitialPublication = state.AllowOwnerlessPublication = false;
            RecordSnowValueChange(state, previous, SnowValueCause.ManualClear);
            // Clear() would delete the snapshot and could seed snow again on reload. Write zero instead.
            SeasonalSnowStorage.Write(state.Zdo, 0f, state.Epoch, now);
            RememberWrittenSnapshot(state, now);
            Classify(state);
            QueueRuntimeVisual(state, force: true);
            SeasonalSnowDiagnostics.ClearCache();
            return SnowClearResult.Cleared;
        }

        private static bool InSnowClearingRange(SnowPiece state, Player actor)
        {
            Vector3 eye = actor.m_eye ? actor.m_eye.position : actor.transform.position;
            float range = Mathf.Max(0f, actor.m_maxPlaceDistance) + (actor == Player.m_localPlayer ? 0f : 1f);
            float squared = range * range;
            // Use cached collider bounds so large pieces are reachable at their visible edge, not only their pivot.
            if (state.Colliders != null)
                foreach (Collider collider in state.Colliders)
                    if (collider && collider.enabled && collider.gameObject.activeInHierarchy && !collider.isTrigger &&
                        collider.bounds.SqrDistance(eye) <= squared)
                        return true;
            return (state.Position - eye).sqrMagnitude <= squared;
        }

        private static bool HasSnowClearingAccess(Vector3 position, Player actor)
        {
            if (actor == Player.m_localPlayer)
                return PrivateArea.CheckAccess(position);
            // CheckAccess uses the local player's identity. Preserve its default overlapping-ward rule for the sender.
            long playerId = actor.GetPlayerID();
            bool protectedArea = false;
            foreach (PrivateArea area in PrivateArea.m_allAreas)
            {
                if (!area || !area.m_nview || !area.IsEnabled() || !area.IsInside(position, 0f))
                    continue;
                protectedArea = true;
                if ((area.m_piece && area.m_piece.GetCreator() == playerId) || area.IsPermitted(playerId))
                    return true;
            }
            return !protectedArea;
        }
    }
}
