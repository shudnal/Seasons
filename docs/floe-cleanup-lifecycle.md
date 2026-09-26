# Immediate seasonal floe cleanup

This contract supersedes the sliced cleanup introduced in `85b3c29`.
Implementation base: `b03fffacce39a8f0d1a4e461701bb82a4de21eb2`, PR #45.

When the authoritative configuration disables floes, the eligible winter period
ends, or the ocean freezes, the server selects seasonal floes and their zone
markers in one traversal of `ZDOMan.m_objectsByID`. Selection stores only matching
IDs in temporary lists; it does not snapshot every world object.

After enumeration, all selected spawn markers are reset and all selected floes
are removed in the same call. Loaded objects use the existing scene-destruction
path. Unloaded objects need only native ZDO destruction. Native `SendDestroyed`
is flushed once for the batch, retaining network notification and dead-ZDO
tracking. Dictionary entries are not deleted during their enumeration. The
engine may finish destroying scene GameObjects at the end of the frame and
remote clients receive deletion according to normal network delivery.

There is no custom deletion queue, per-frame removal limit, priority-sector
cleanup, background floe scan or terrain/biome-readiness gate. Terrain
maintenance retains its own existing budgets and readiness checks. Placement
also remains budgeted; its rules and density are not changed.

Config/season callbacks coalesce a request for the next connected main-thread
update; that update finishes the whole cleanup rather than spreading it over
frames. Initial known ineligibility is observed too. An unknown season during
startup is not treated as summer. A marked instance streamed in while cleanup
is required requests another complete pass. This is not a recurring world scan.

Only watermarked seasonal `ice1` records are deleted. Native ice without the
seasonal watermark is untouched. Zone controllers survive; only our spawn marker
is reset. Cleanup is server-only, including the single-player host. A client's
local physics/lease switches do not delete world objects.

`Seasons.SeasonalWorldMaintenance.GetFloeCleanupStatus()` remains available. It
now reports `pending`, `running`, `passes`, `lastRemovedFloes` and
`lastResetMarkers`, not queued discovery progress. These are last-pass counts,
not persistent object counts. Re-enabling floes in an eligible winter period
uses normal placement in the cleared zones without a world reload.

Check disable, re-enable, a real season change and loading an already ineligible
world. Measure the no-floe FPS after the removal frame, without profiling. A
single transition pause is accepted by design; no duration is claimed without
a measurement. No mod build, automated mod tests or game execution was performed.
