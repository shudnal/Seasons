using System.Runtime.CompilerServices;

namespace Seasons
{
    // Restore only values still owned by Seasons. Do not impose vanilla defaults
    // on a torch configured by another mod, or replace unrelated EffectArea flags.
    internal static class SeasonalTorchOverrides
    {
        private sealed class DrainOverride
        {
            internal float Original;
            internal float Applied;
        }

        private sealed class HeatOverride
        {
            internal bool OriginalHeat;
            internal bool OriginalHeatType;
        }

        private static readonly ConditionalWeakTable<ItemDrop.ItemData.SharedData, DrainOverride> drains =
            new ConditionalWeakTable<ItemDrop.ItemData.SharedData, DrainOverride>();
        private static readonly ConditionalWeakTable<EffectArea, HeatOverride> areas =
            new ConditionalWeakTable<EffectArea, HeatOverride>();

        internal static void UpdateDurability(ItemDrop.ItemData item, bool enabled, float requested)
        {
            ItemDrop.ItemData.SharedData shared = item?.m_shared;
            if (shared == null)
                return;

            if (!drains.TryGetValue(shared, out DrainOverride state))
            {
                ItemDrop.ItemData.SharedData template = item.m_dropPrefab
                    ? item.m_dropPrefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
                if (template != null && !ReferenceEquals(template, shared) &&
                    drains.TryGetValue(template, out DrainOverride inherited) && shared.m_durabilityDrain == inherited.Applied)
                    state = new DrainOverride { Original = inherited.Original, Applied = inherited.Applied };
                else if (enabled)
                    state = new DrainOverride { Original = shared.m_durabilityDrain, Applied = shared.m_durabilityDrain };
                else
                    return;
                drains.Add(shared, state);
            }

            if (!enabled)
            {
                if (shared.m_durabilityDrain == state.Applied)
                    shared.m_durabilityDrain = state.Original;
                drains.Remove(shared);
                return;
            }

            if (shared.m_durabilityDrain != state.Applied)
                state.Original = shared.m_durabilityDrain;
            state.Applied = requested;
            shared.m_durabilityDrain = requested;
        }

        internal static void UpdateArea(EffectArea area, bool enabled, EffectArea template = null)
        {
            if (!area)
                return;

            bool heat = (area.m_type & EffectArea.Type.Heat) != 0;
            if (!areas.TryGetValue(area, out HeatOverride state))
            {
                if (template && areas.TryGetValue(template, out HeatOverride inherited) && heat && area.m_isHeatType)
                    state = new HeatOverride { OriginalHeat = inherited.OriginalHeat, OriginalHeatType = inherited.OriginalHeatType };
                else if (enabled)
                    state = new HeatOverride { OriginalHeat = heat, OriginalHeatType = area.m_isHeatType };
                else
                    return;
                areas.Add(area, state);
            }

            if (!enabled)
            {
                // Seasons only adds Heat / true. A later removal by another mod wins;
                // all other area flags remain untouched in both directions.
                if (heat && !state.OriginalHeat)
                    area.m_type &= ~EffectArea.Type.Heat;
                if (area.m_isHeatType)
                    area.m_isHeatType = state.OriginalHeatType;
                areas.Remove(area);
                return;
            }

            if (!heat)
                state.OriginalHeat = false;
            if (!area.m_isHeatType)
                state.OriginalHeatType = false;
            area.m_type |= EffectArea.Type.Heat;
            area.m_isHeatType = true;
        }
    }
}
