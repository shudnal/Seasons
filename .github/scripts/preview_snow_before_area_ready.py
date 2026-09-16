from pathlib import Path
import re

path = Path("SeasonState/SeasonalSnow.cs")
text = path.read_text(encoding="utf-8-sig")


def replace_once(old: str, new: str, label: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected one {label}, found {count}")
    text = text.replace(old, new, 1)


def replace_pattern(pattern: str, replacement: str, label: str) -> None:
    global text
    text, count = re.subn(pattern, replacement, text, count=1, flags=re.S)
    if count != 1:
        raise RuntimeError(f"Expected one {label}, found {count}")


replace_once(
    """            public bool reconciliationPending = true;
            public bool initializationComplete;
            public long observedOwner = long.MinValue;""",
    """            public bool reconciliationPending = true;
            public bool initializationComplete;
            public bool localVisualApplied;
            public float localVisualSnow;
            public long observedOwner = long.MinValue;""",
    "local visual state fields",
)

replace_pattern(
    r"        private static void ApplyPersistedSnowVisual\(WearNTear instance, SnowActivityState state\)\n"
    r"        \{.*?\n        \}\n\n"
    r"        private static bool CanInitializeSnowState",
    """        private static bool ClearLocalSnowVisual(SnowActivityState state)
        {
            if (state == null || !state.localVisualApplied)
                return false;

            state.localVisualApplied = false;
            state.localVisualSnow = 0f;
            return true;
        }

        private static void ApplyPersistedSnowVisual(WearNTear instance, SnowActivityState state)
        {
            if (!instance || state == null)
                return;

            float snow = GetPersistedSnow(instance);
            bool changed = ClearLocalSnowVisual(state) ||
                Mathf.Abs(instance.m_snowBuildup - snow) > SnowChangeEpsilon;

            instance.m_snowBuildup = snow;

            if (changed)
                instance.UpdateSnowVisual();
        }

        private static void ApplyLocalSnowVisual(
            WearNTear instance,
            SnowActivityState state,
            float snow)
        {
            if (!instance || state == null)
                return;

            snow = Mathf.Clamp(snow, 0f, GetSnowBuildupRange(instance).y);
            bool changed = !state.localVisualApplied ||
                Mathf.Abs(state.localVisualSnow - snow) > SnowChangeEpsilon;

            state.localVisualApplied = true;
            state.localVisualSnow = snow;

            if (changed)
                instance.UpdateSnowVisual();
        }

        private static void UpdatePendingLocalSnowVisual(
            WearNTear instance,
            SnowActivityState state)
        {
            if (!instance || state == null || ZNet.instance == null ||
                ZNet.instance.IsDedicated() || instance.m_nview == null ||
                !instance.m_nview.IsValid())
            {
                return;
            }

            ZDO zdo = instance.m_nview.GetZDO();
            bool shouldShowSnow = zdo != null
                && IsWinterStateReady()
                && Enabled
                && controlEnvironments.Value
                && CanInitializeSnowState(instance, zdo)
                && IsSeasonalSnowPosition(instance)
                && !IsDeepNorth(instance)
                && !zdo.GetBool(SeasonsVars.s_seasonalSnowWinter)
                && instance.m_snowBuildup <= SnowChangeEpsilon
                && zdo.GetFloat(ZDOVars.s_snow, 0f) <= SnowChangeEpsilon;

            if (!shouldShowSnow)
            {
                if (state.localVisualApplied)
                    ApplyPersistedSnowVisual(instance, state);
                return;
            }

            ApplyLocalSnowVisual(
                instance,
                state,
                GetPassiveSeasonalSnowTarget(instance));
        }

        private static bool CanInitializeSnowState""",
    "local visual helpers",
)

replace_once(
    """                Vector2s zone = ZoneSystem.GetZone(instance.transform.position);
                if (!PendingSnowAreaReadiness.TryGetValue(zone, out bool areaReady))
                {
                    areaReady = ZNetScene.instance.IsAreaReady(instance.transform.position);
                    PendingSnowAreaReadiness[zone] = areaReady;
                }

                if (!areaReady)
                    continue;

                SnowActivityState state = SeasonalSnowActivityStates.GetValue(
                    instance,
                    _ => new SnowActivityState());
                if (TryInitializeLoadedSnow(""",
    """                SnowActivityState state = SeasonalSnowActivityStates.GetValue(
                    instance,
                    _ => new SnowActivityState());
                UpdatePendingLocalSnowVisual(instance, state);

                Vector2s zone = ZoneSystem.GetZone(instance.transform.position);
                if (!PendingSnowAreaReadiness.TryGetValue(zone, out bool areaReady))
                {
                    areaReady = ZNetScene.instance.IsAreaReady(instance.transform.position);
                    PendingSnowAreaReadiness[zone] = areaReady;
                }

                if (!areaReady)
                    continue;

                if (TryInitializeLoadedSnow(""",
    "pending initialization processing",
)

replace_once(
    """        private static void SetInitialSnowBuildup(
            WearNTear instance,
            ZDO zdo,
            float value,
            bool markSeasonalSnow)
        {
            Vector2 range = GetSnowBuildupRange(instance);""",
    """        private static void SetInitialSnowBuildup(
            WearNTear instance,
            SnowActivityState state,
            ZDO zdo,
            float value,
            bool markSeasonalSnow)
        {
            ClearLocalSnowVisual(state);

            Vector2 range = GetSnowBuildupRange(instance);""",
    "initial snow setter signature",
)

replace_once(
    """                zdo.Set(SeasonsVars.s_seasonalSnowWatermark, true);
                instance.m_snowBuildup = currentSnow;
                instance.UpdateSnowVisual();""",
    """                zdo.Set(SeasonsVars.s_seasonalSnowWatermark, true);
                ClearLocalSnowVisual(state);
                instance.m_snowBuildup = currentSnow;
                instance.UpdateSnowVisual();""",
    "existing snow initialization",
)

old_initial_call = """                    SetInitialSnowBuildup(
                        instance,
                        zdo,"""
new_initial_call = """                    SetInitialSnowBuildup(
                        instance,
                        state,
                        zdo,"""
if text.count(old_initial_call) != 2:
    raise RuntimeError(
        f"Expected two initial snow setter calls, found {text.count(old_initial_call)}"
    )
text = text.replace(old_initial_call, new_initial_call)

replace_once(
    """            if (IsInteractiveObjectMeltActive(instance) &&
                InteractiveObjectMeltStates.TryGetValue(
                    instance,
                    out InteractiveMeltState interactiveState) &&
                interactiveState.initialized)
            {
                snow = Mathf.Clamp(
                    interactiveState.targetSnow,
                    0f,
                    GetSnowBuildupRange(instance).y);
                return true;
            }

            return false;""",
    """            if (IsInteractiveObjectMeltActive(instance) &&
                InteractiveObjectMeltStates.TryGetValue(
                    instance,
                    out InteractiveMeltState interactiveState) &&
                interactiveState.initialized)
            {
                snow = Mathf.Clamp(
                    interactiveState.targetSnow,
                    0f,
                    GetSnowBuildupRange(instance).y);
                return true;
            }

            if (SeasonalSnowActivityStates.TryGetValue(
                    instance,
                    out SnowActivityState activityState) &&
                activityState.localVisualApplied)
            {
                snow = Mathf.Clamp(
                    activityState.localVisualSnow,
                    0f,
                    GetSnowBuildupRange(instance).y);
                return true;
            }

            return false;""",
    "snow visual override",
)

replace_once(
    """        private static void MarkPlacedDuringWinter(WearNTear instance)
        {
            if (!UpdateActiveAreaState(instance) || !CanOwnSnowState(instance) ||""",
    """        private static void MarkPlacedDuringWinter(WearNTear instance)
        {
            PendingSnowInitializations.Remove(instance);
            if (SeasonalSnowActivityStates.TryGetValue(
                    instance,
                    out SnowActivityState activityState) &&
                ClearLocalSnowVisual(activityState))
            {
                instance.UpdateSnowVisual();
            }

            if (!UpdateActiveAreaState(instance) || !CanOwnSnowState(instance) ||""",
    "placed piece preview clearing",
)

replace_once(
    """                bool deepNorth = IsDeepNorth(wearNTear);
                if (!deepNorth)""",
    """                if (SeasonalSnowActivityStates.TryGetValue(
                        wearNTear,
                        out SnowActivityState activityState) &&
                    ClearLocalSnowVisual(activityState))
                {
                    wearNTear.UpdateSnowVisual();
                }

                bool deepNorth = IsDeepNorth(wearNTear);
                if (!deepNorth)""",
    "loaded preview clearing",
)

if text.count("SetInitialSnowBuildup(") != 3:
    raise RuntimeError(
        f"Expected one initial snow setter and two calls, found {text.count('SetInitialSnowBuildup(')}"
    )
if text.count("                        state,\n                        zdo,") != 2:
    raise RuntimeError("Not all initial snow setter calls received the visual state")

path.write_text(text, encoding="utf-8")
