# Winter Pickable perishing

`Season` / `Pickables can perish in winter` is a server-controlled setting,
enabled by default. Disable it to prevent Seasons from freezing and removing
available harvests from mature crops and other growth-controlled `Pickable`
objects. It does not restore harvests that have already been removed.

## Runtime behavior

The switch participates in `IsVulnerableToWinter`, shared by the initial check,
periodic checks and the delayed coroutine's final eligibility check. A pending
perish cannot commit while the switch is disabled. No new scan or polling loop
is introduced. The existing `UpdateRespawn` schedule is retained even when Awake
queues a perish, because disabling can cancel that pending operation.

Disabled perishing cannot start a freezing timer, reports no freezing time and
hides the additional winter-status hover line. The existing owner-side check
clears saved freezing timestamps on its next scheduled update or object load;
hover code never writes to the ZDO. Re-enabling uses the existing winter rules
and timer state; it does not grant a new guaranteed grace period. The loading-screen
perishing tip is hidden while the switch is off.

## Configuration scope

Existing config section names, keys, types, defaults and ranges are unchanged.
Their descriptions now distinguish the two stages of the crop lifecycle:

- The death day, survival chance and freezing duration apply to mature `Pickable`
  objects only. The day is within the current winter, not the crop's age. A zero
  day or zero duration does not disable perishing.
- The frost-resistant prefab list and nearby heat protection also affect the
  seasonal `TooCold` status of growing `Plant` objects.
- Winter loss of cultivated ground is independent. Disabling it prevents future
  removal, but does not recultivate terrain that has already changed.

Growing `Plant` health, seasonal growth and respawn multipliers, cultivation,
harvesting, ordinary damage and native growth requirements are not overridden.
The sliding implementation and release version are unchanged.

## Manual verification

Check enabled and disabled perishing on a vulnerable mature harvest; disable
while it is freezing and while a perish is queued. Confirm that the harvest and
ordinary interaction remain available, the winter warning disappears, and the
existing update schedule still runs after cancellation. Re-enable and verify the
normal day, timer, resistance and heat rules. Confirm that growing Plants still
use their existing health rules and that winter growth remains paused when its
multiplier is zero. Repeat with the harvest owned by another updated client.

Validation is limited to source, diff and configuration inspection. No mod build
or gameplay tests were run.
