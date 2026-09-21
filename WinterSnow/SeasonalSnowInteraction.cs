using System;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    internal sealed partial class SeasonalSnowController
    {
        private CraftingStation observedStation;
        private WearNTear stationSnowPiece;
        private Transform observedAttachment;
        private WearNTear attachedSnowPiece;

        internal void StationUsed(CraftingStation station)
        {
            Player player = Player.m_localPlayer;
            if (!player || player.GetCurrentCraftingStation() != station || !SeasonalSnow.WinterReady)
                return;
            if (observedStation != station)
            {
                observedStation = station;
                stationSnowPiece = station.GetComponent<WearNTear>();
                if (!stationSnowPiece)
                    stationSnowPiece = station.GetComponentInParent<WearNTear>();
                if (!stationSnowPiece)
                    stationSnowPiece = station.GetComponentInChildren<WearNTear>(true);
            }
            TouchSnowUse(stationSnowPiece);
        }

        internal void AttachedObjectUsed(Player player)
        {
            if (!player || player != Player.m_localPlayer || !player.m_attached || !player.m_attachPoint || !SeasonalSnow.WinterReady)
                return;
            if (observedAttachment != player.m_attachPoint)
            {
                observedAttachment = player.m_attachPoint;
                attachedSnowPiece = observedAttachment.GetComponentInParent<WearNTear>();
                if (!attachedSnowPiece && player.m_attachColliders != null)
                    foreach (Collider collider in player.m_attachColliders)
                    {
                        if (!collider)
                            continue;
                        attachedSnowPiece = collider.GetComponentInParent<WearNTear>();
                        if (attachedSnowPiece)
                            break;
                    }
            }
            TouchSnowUse(attachedSnowPiece);
        }

        internal void StopAttachedSnowUse(Player player)
        {
            if (player != Player.m_localPlayer || player.m_sleeping)
                return;
            if (attachedSnowPiece && snowPieces.TryGetValue(attachedSnowPiece, out SnowPiece state))
            {
                state.InteractiveUntil = 0f;
                QueueRefresh(state, SnowRefresh.Heat);
            }
            observedAttachment = null;
            attachedSnowPiece = null;
        }

        private void TouchSnowUse(WearNTear piece)
        {
            if (!piece || seasonalSnowInteractiveObjectMeltMultiplier.Value <= 0f ||
                !snowPieces.TryGetValue(piece, out SnowPiece state) || !state.Valid ||
                !state.Confirmed || !state.Region.Ready || ZNetScene.instance.OutsideActiveArea(state.Position))
                return;
            bool starting = state.InteractiveUntil <= Time.time;
            state.InteractiveUntil = Time.time + 1f;
            interactingPieces.Add(state);
            if (starting)
                QueueRefresh(state, SnowRefresh.Heat);
            long owner = state.Zdo.GetOwner();
            if (owner != 0L && !state.View.IsOwner() && Time.time >= state.NextUseSignal)
            {
                state.NextUseSignal = Time.time + 0.2f;
                // Send activity, never a client-selected snow amount.
                state.View.InvokeRPC(owner, UseSnowRpc);
            }
        }

        private void ReceiveUse(SnowPiece state, long sender)
        {
            if (!state.Valid || !state.View.IsOwner() || !state.Confirmed || !state.Region.Ready ||
                !SeasonalSnow.WinterReady || seasonalSnowInteractiveObjectMeltMultiplier.Value <= 0f ||
                ZNetScene.instance.OutsideActiveArea(state.Position))
                return;
            bool near = false;
            foreach (Player player in Player.GetAllPlayers())
            {
                if (!player || !player.m_nview || !player.m_nview.IsValid() ||
                    player.m_nview.GetZDO().GetOwner() != sender)
                    continue;
                if ((player.transform.position - state.Position).sqrMagnitude <= 100f)
                {
                    near = true;
                    break;
                }
            }
            if (!near)
                return;
            bool starting = state.InteractiveUntil <= Time.time;
            state.InteractiveUntil = Time.time + 1f;
            interactingPieces.Add(state);
            if (starting)
                QueueRefresh(state, SnowRefresh.Heat);
        }

        private void ExpireSnowInteractions()
        {
            expiredInteractions.Clear();
            foreach (SnowPiece state in interactingPieces)
                if (state.Retired || !state.Region.Ready || state.InteractiveUntil <= Time.time ||
                    ZNetScene.instance.OutsideActiveArea(state.Position))
                    expiredInteractions.Add(state);
            foreach (SnowPiece state in expiredInteractions)
            {
                state.InteractiveUntil = 0f;
                interactingPieces.Remove(state);
                QueueRefresh(state, SnowRefresh.Heat);
            }
            expiredInteractions.Clear();
        }
    }
}
