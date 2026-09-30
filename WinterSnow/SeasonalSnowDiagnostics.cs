using HarmonyLib;
using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    internal static class SeasonalSnowDiagnostics
    {
        private const float RefreshInterval = 0.2f;
        private const string BlockStart = "\n\n<size=70%><color=#ABDCEF>Seasons diagnostics</color>\n";
        private const string BlockEnd = "</size>";
        private static ZNetScene cachedScene;
        private static GameObject cachedTarget;
        private static WearNTear cachedPiece;
        private static string cachedBlock = String.Empty;
        private static float refreshAt;

        internal static void ClearCache()
        {
            cachedScene = null;
            cachedTarget = null;
            cachedPiece = null;
            cachedBlock = String.Empty;
            refreshAt = 0f;
        }

        private static void AppendHover(Hud hud, Player player)
        {
            // This client preference gates all HUD, component and ZDO access.
            if (!(showSnowDiagnosticsOnHover?.Value ?? false))
                return;
            if (!hud || !hud.m_hoverName || !ZNet.instance || !ZNetScene.instance ||
                ZNet.instance.IsDedicated() || !player)
            {
                ClearCache();
                return;
            }

            string text = hud.m_hoverName.text ?? String.Empty;
            // Vanilla normally replaces the text each frame. The unique header also
            // identifies our old block after a cache reset if another mod keeps it.
            int previous = text.IndexOf(BlockStart, StringComparison.Ordinal);
            while (previous >= 0)
            {
                int end = text.IndexOf(BlockEnd, previous + BlockStart.Length, StringComparison.Ordinal);
                if (end < 0)
                    break;
                hud.m_hoverName.text = text = text.Remove(previous, end + BlockEnd.Length - previous);
                previous = text.IndexOf(BlockStart, StringComparison.Ordinal);
            }
            if (TextViewer.instance && TextViewer.instance.IsVisible())
            {
                ClearCache();
                return;
            }

            Piece placementPiece = player.GetHoveringPiece();
            GameObject target = placementPiece ? placementPiece.gameObject : player.GetHoverObject();
            if (!target)
            {
                ClearCache();
                return;
            }
            if (cachedScene != ZNetScene.instance || cachedTarget != target)
            {
                ClearCache();
                cachedScene = ZNetScene.instance;
                cachedTarget = target;
                Piece piece = placementPiece ? placementPiece : target.GetComponentInParent<Piece>();
                cachedPiece = piece ? piece.GetComponent<WearNTear>() : null;
            }
            if (!cachedPiece)
                return;
            if (cachedBlock.Length == 0 || Time.unscaledTime >= refreshAt)
            {
                cachedBlock = BlockStart + SeasonalSnowController.Instance.ReadSnowDiagnostics(cachedPiece) + BlockEnd;
                refreshAt = Time.unscaledTime + RefreshInterval;
            }
            hud.m_hoverName.text = text + cachedBlock;
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.UpdateCrosshair))]
        private static class Hud_UpdateCrosshair_SnowDiagnostics
        {
            [HarmonyPostfix, HarmonyPriority(Priority.Last)]
            private static void Postfix(Hud __instance, Player player) => AppendHover(__instance, player);
        }
    }

    internal sealed partial class SeasonalSnowController
    {
        // Reads existing state, transforms and save fields only. In particular, do not use the
        // registration, biome resolution, readiness, heat or visual request paths here.
        internal string ReadSnowDiagnostics(WearNTear piece)
        {
            if (!piece)
                return "Seasonal snow: no piece";

            snowPieces.TryGetValue(piece, out SnowPiece state);
            visuals.TryGetValue(piece, out VisualState visual);
            ZNetView view = piece.m_nview;
            ZDO zdo = view ? view.GetZDO() : null;
            bool localOwner = view && view.IsValid() && view.IsOwner();
            Heightmap.Biome biome = state != null ? state.Biome : piece.m_biome;
            double now = ZNet.instance ? ZNet.instance.GetTimeSeconds() : 0d;
            StringBuilder text = new StringBuilder(2048);
            DiagnosticLine(text, "Seasonal snow: {0} | WinterReady={1}",
                state != null ? "registered" : "not registered", SeasonalSnow.WinterReady);
            DiagnosticLine(text, "Identity: {0} #{1} | ZDO={2} owner={3} local={4} biome={5}",
                Utils.GetPrefabName(piece.gameObject), piece.GetInstanceID(),
                zdo != null ? zdo.m_uid.ToString() : "<none>", zdo != null ? zdo.GetOwner() : 0L, localOwner, biome);
            AppendSnowMeshDiagnostics(text, piece, visual);

            if (state != null)
            {
                DiagnosticLine(text, "Runtime: valid={0} confirmed={1} simulates={2} bucket={3} | Snow={4:F5} [{5:F2}, {6:F2}]",
                    state.Valid, state.Confirmed, state.Simulates, state.Bucket, state.Snow, state.Minimum, state.Maximum);
                AppendSnowValueDiagnostics(text, state);
                DiagnosticLine(text, "State: saved={0} construction={1} epoch={2} | publish={3} initial={4} ownerless={5}",
                    state.Saved, state.Construction, state.Epoch, state.MayPublish, state.AllowInitialPublication, state.AllowOwnerlessPublication);
                DiagnosticLine(text, "Queues: refresh={0} ({1}) publish={2}/{3} visual={4}/{5}",
                    state.RefreshQueued, state.Refresh, state.PublishQueued, state.ForcePublish, state.VisualQueued, state.ForceVisual);
                SnowRegion region = state.Region;
                DiagnosticLine(text, "Area: ready={0} dirty={1} generation={2}/{3} geometry={4}/{5} | region queue={6} ({7}; {8})",
                    region != null && region.Ready, region != null && region.ReadinessDirty,
                    state.ReadyGeneration, region?.ReadyGeneration ?? -1, state.GeometryRevision, region?.GeometryRevision ?? -1,
                    region != null && region.Queued, region?.Scanning ?? SnowRefresh.None, region?.Pending ?? SnowRefresh.None);
                AppendRefreshDiagnostics(text, state);
                DiagnosticLine(text, "Cover/heat: covered={0} roof={1} leaky={2} shield={3} melting={4} | heat={5:F5} melt={6:F5} active={7} links={8}",
                    state.Covered, state.Roof, state.Leaky, state.Shielded, state.Melting, state.HeatRate, state.MeltRate,
                    state.InteractiveUntil > Time.time, state.HeatLinks?.Count ?? 0);
                DiagnosticLine(text, "Consumed: WeatherGain={0:F3} WeatherTime={1:F1} s | catch-up={2}",
                    state.WeatherGain, state.WeatherTime, double.IsNaN(state.CatchUpFrom) ? "none" :
                    String.Format(CultureInfo.InvariantCulture, "{0:F1} -> {1:F1} s", state.CatchUpFrom, now));
                DiagnosticLine(text, "Remembered: present={0} value={1:F5} baseline={2:F5} epoch={3} from={4:F1} s",
                    state.SnapshotPresent, state.SnapshotValue, state.SnapshotBaseline, state.SnapshotEpoch,
                    SeasonalSnowStorage.FromTimestamp(state.SnapshotTime));
            }

            long period = (long)Math.Floor(now / Math.Max(1L, SeasonalSnow.EnvironmentDuration));
            long index = period - SeasonalSnow.FirstEnvironmentPeriod;
            string record = "<unavailable>";
            if (SeasonalSnow.SeasonalSnowTimelines.TryGetValue(biome, out SeasonalSnow.BiomeSnowTimeline timeline) &&
                index >= 0L && index < timeline.Periods.Length)
            {
                SeasonalSnow.SnowPeriod entry = timeline.Periods[(int)index];
                record = String.Format(CultureInfo.InvariantCulture, "{0} | buildup={1:F2} | cumulative={2:F3}",
                    entry.EnvironmentName, entry.SnowBuildup, entry.CumulativeSnowGain);
            }
            DiagnosticLine(text, "Weather: period={0} | {1}", period, record);
            string liveSource = ReadLiveAccumulationSource(state, now, out float liveRate);
            DiagnosticLine(text, "Live accumulation: {0} | rate={1:F5}/s", liveSource, liveRate);
            DiagnosticLine(text, "Timeline: {0:F1} -> {1:F1} s | gain now={2:F3} ready={3}",
                SeasonalSnow.TimelineStartSeconds, SeasonalSnow.TimelineEndSeconds,
                SeasonalSnow.GetCumulativeSnowGainAt(biome, now), SeasonalSnow.WeatherReady);
            EnvMan envMan = EnvMan.instance;
            EnvSetup current = envMan ? envMan.GetCurrentEnvironment() : null;
            DiagnosticLine(text, "Environment: {0} buildup={1:F2} | native period={2} force={3} debug={4}",
                current?.m_name ?? "<none>", current?.m_snowBuildup ?? 0f, envMan ? envMan.m_environmentPeriod : -1L,
                envMan && !String.IsNullOrEmpty(envMan.m_forceEnv) ? envMan.m_forceEnv : "none",
                envMan && !String.IsNullOrEmpty(envMan.m_debugEnv) ? envMan.m_debugEnv : "none");
            DiagnosticLine(text, "Clock: world={0:F1} s calendar={1:F1} s | {2} day={3} | overrides: season={4}/{5} day={6}/{7}",
                now, seasonState != null && ZNet.instance ? seasonState.GetTotalSeconds() : 0d,
                seasonState != null ? seasonState.GetCurrentSeason().ToString() : "<none>", seasonState?.GetCurrentDay() ?? 0,
                overrideSeason?.Value ?? false, seasonOverrided?.Value.ToString() ?? "<none>",
                overrideSeasonDay?.Value ?? false, seasonDayOverrided?.Value ?? 0);

            SeasonalSnowStorage.Snapshot snapshot = new SeasonalSnowStorage.Snapshot(zdo);
            DiagnosticLine(text, "Stored: present={0} value={1:F5} baseline={2:F5} epoch={3} from={4:F1} s",
                snapshot.Present, snapshot.Value, snapshot.Baseline, snapshot.Epoch, SeasonalSnowStorage.FromTimestamp(snapshot.From));
            bool nativePresent = zdo != null && zdo.GetFloat(ZDOVars.s_snow, out _);
            DiagnosticLine(text, "Native: snow={0:F5} pre={1} heavy={2} | ZDO snow={3} pre={4} | caps N/W/B={5}/{6}/{7}",
                piece.m_snowBuildup, piece.m_addPreSnow, piece.m_heavySnow,
                nativePresent ? zdo.GetFloat(ZDOVars.s_snow).ToString("F5", CultureInfo.InvariantCulture) : "<absent>",
                zdo != null && zdo.GetBool(ZDOVars.s_preSnow), (bool)piece.m_snow, (bool)piece.m_snowWorn, (bool)piece.m_snowBroken);
            if (visual == null)
                DiagnosticLine(text, "Visual: no state/binding");
            else
                DiagnosticLine(text, "Visual: bound={0} disabled={1} queued={2} | target={3} snow={4:F5} level={5:F5} | applied={6} level={7:F5} set={8}",
                    visual.Bound, visual.Disabled, visual.Queued, visual.Target ? visual.Target.name : "<none>",
                    visual.TargetSnow, visual.TargetLevel, visual.Applied ? visual.Applied.name : "<none>", visual.AppliedLevel, visual.HasApplied);
            return text.ToString().TrimEnd('\n');
        }

        private static void AppendSnowMeshDiagnostics(StringBuilder text, WearNTear piece, VisualState visual)
        {
            // Prefer the applied cap: Target may still be waiting in the visual queue.
            MeshRenderer cap = visual?.Applied;
            if (!IsDiagnosticCapActive(cap) ||
                (cap != piece.m_snow && cap != piece.m_snowWorn && cap != piece.m_snowBroken))
            {
                cap = IsDiagnosticCapActive(piece.m_snowBroken) ? piece.m_snowBroken :
                    IsDiagnosticCapActive(piece.m_snowWorn) ? piece.m_snowWorn :
                    IsDiagnosticCapActive(piece.m_snow) ? piece.m_snow : null;
            }
            if (!cap)
            {
                DiagnosticLine(text, "Snow mesh: <none active>");
                return;
            }

            string variant = cap == piece.m_snow ? "normal" : cap == piece.m_snowWorn ? "worn" : "broken";
            MeshFilter filter = cap.GetComponent<MeshFilter>();
            Mesh mesh = filter ? filter.sharedMesh : null;
            Transform transform = cap.transform;
            // These are the local values edited by Seasonal snow.json, not world/lossy values.
            Vector3 position = transform.localPosition;
            Vector3 scale = transform.localScale;
            DiagnosticLine(text, "Snow mesh: {0} | cap={1} ({2}) | local position=({3:0.#####}, {4:0.#####}, {5:0.#####}) scale=({6:0.#####}, {7:0.#####}, {8:0.#####})",
                mesh ? mesh.name : "<none>", cap.name, variant,
                position.x, position.y, position.z, scale.x, scale.y, scale.z);
        }

        // Cap-root activity also covers caps whose root renderer is disabled by LOD selection.
        // Camera visibility must not change which transform the diagnostics report.
        private static bool IsDiagnosticCapActive(MeshRenderer cap) => cap && cap.gameObject.activeInHierarchy;

        private static void DiagnosticLine(StringBuilder text, string format, params object[] values) =>
            text.AppendFormat(CultureInfo.InvariantCulture, format, values).Append('\n');

        [Flags]
        private enum SnowValueCause : ushort
        {
            None = 0, PendingInitialization = 1, SavedSnapshot = 2, NewConstruction = 4,
            InitialBaseline = 8, CoveredInitialization = 16, WinterReset = 32,
            WeatherCatchUp = 64, Accumulation = 128, Melting = 256, ShieldClear = 512,
            RangeClamp = 1024, WinterEnd = 2048
        }

        private sealed class SnowValueDiagnostics
        {
            internal bool InitialObserved, ChangeObserved;
            internal SnowValueCause InitialCause, LastCause;
            internal float InitialValue, InitialAt, PreviousValue, Value, ChangedAt;
        }

        private static void RecordInitialSnowValue(SnowPiece state, SnowValueCause cause)
        {
            if (!CollectSnowDiagnostics)
                return;
            SnowValueDiagnostics trace = state.ValueDiagnostics ??= new SnowValueDiagnostics();
            trace.InitialObserved = true;
            trace.InitialCause = cause;
            trace.InitialValue = state.Snow;
            trace.InitialAt = Time.unscaledTime;
        }

        private static void RecordSnowValueChange(SnowPiece state, float previous, SnowValueCause cause)
        {
            // A refresh or accepted snapshot with the same value must not erase the
            // last real change. In particular, a loaded zero is not a new clearing event.
            if (!CollectSnowDiagnostics || previous.Equals(state.Snow))
                return;
            SnowValueDiagnostics trace = state.ValueDiagnostics ??= new SnowValueDiagnostics();
            trace.ChangeObserved = true;
            trace.LastCause = cause;
            trace.PreviousValue = previous;
            trace.Value = state.Snow;
            trace.ChangedAt = Time.unscaledTime;
        }

        private static void RecordRefreshedSnowValue(SnowPiece state, float previous, float target,
            SnowValueCause cause, bool shielded)
        {
            if (!CollectSnowDiagnostics || previous.Equals(state.Snow))
                return;
            if (shielded)
                cause = SnowValueCause.ShieldClear;
            else if (!state.Snow.Equals(target))
                cause |= SnowValueCause.RangeClamp;
            RecordSnowValueChange(state, previous, cause);
        }

        private static void AppendSnowValueDiagnostics(StringBuilder text, SnowPiece state)
        {
            SnowValueDiagnostics trace = state.ValueDiagnostics;
            if (trace == null || !trace.InitialObserved)
                DiagnosticLine(text, "Initial snow: not observed (diagnostics enabled after registration)");
            else
                DiagnosticLine(text, "Initial snow: {0} | value={1:F5} age={2:F2}s",
                    trace.InitialCause, trace.InitialValue, Time.unscaledTime - trace.InitialAt);
            if (trace == null || !trace.ChangeObserved)
                DiagnosticLine(text, "Last snow change: none observed");
            else
                DiagnosticLine(text, "Last snow change: {0} | {1:F5} -> {2:F5} age={3:F2}s",
                    trace.LastCause, trace.PreviousValue, trace.Value, Time.unscaledTime - trace.ChangedAt);
        }

        internal enum SnowGeometryCause : byte
        {
            Other, ObjectAdded, ObjectRemoved, TransformChanged, HealthVisualChanged,
            TerrainChanged, RockChanged, Placed, PieceMoved, AreaReady, WinterStart, Settings,
            Count
        }

        [Flags]
        private enum HeatReindexCause : byte
        {
            None = 0, Registration = 1, Removal = 2, Settings = 4, NativeColliderActivity = 8
        }

        private sealed class SnowGeometryDiagnostics
        {
            internal readonly long[] Counts = new long[(int)SnowGeometryCause.Count];
            internal long Total;
            internal SnowGeometryCause LastCause;
            internal UnityEngine.Object Source;
            internal int SourcePrefab;
            internal ZDOID SourceId;
            internal Vector3 Position;
            internal float At;
        }

        private struct SnowHeatDiagnostics
        {
            internal long NativeFireplaceSamples, OtherSourcePolls, ActivityChanges, Reindexes;
            internal UnityEngine.Object ActivitySource, ReindexSource;
            internal HeatReindexCause ReindexCause;
            internal int ActiveAreas;
            internal float ActivityAt, ReindexAt;
        }

        private SnowHeatDiagnostics heatDiagnostics;
        private static bool CollectSnowDiagnostics => showSnowDiagnosticsOnHover?.Value == true;

        private static void RecordSnowGeometry(SnowRegion region, SnowGeometryCause cause,
            UnityEngine.Object source, ZDO sourceZdo, Vector3 position)
        {
            if (!CollectSnowDiagnostics)
                return;
            SnowGeometryDiagnostics trace = region.Diagnostics ??= new SnowGeometryDiagnostics();
            trace.Total++;
            trace.Counts[(int)cause]++;
            trace.LastCause = cause;
            trace.Source = source;
            trace.SourcePrefab = sourceZdo != null ? sourceZdo.GetPrefab() : 0;
            trace.SourceId = sourceZdo != null ? sourceZdo.m_uid : ZDOID.None;
            trace.Position = position;
            trace.At = Time.unscaledTime;
        }

        private void RecordHeatActivity(HeatSource source)
        {
            if (!CollectSnowDiagnostics)
                return;
            heatDiagnostics.ActivityChanges++;
            heatDiagnostics.ActivitySource = source.Owner;
            heatDiagnostics.ActiveAreas = 0;
            foreach (HeatArea area in source.Areas)
                if (area.Active)
                    heatDiagnostics.ActiveAreas++;
            heatDiagnostics.ActivityAt = Time.unscaledTime;
        }

        private void RecordHeatReindex(HeatSource source)
        {
            if (!CollectSnowDiagnostics)
                return;
            heatDiagnostics.Reindexes++;
            heatDiagnostics.ReindexSource = source.Owner;
            heatDiagnostics.ReindexCause = source.ReindexCause;
            heatDiagnostics.ReindexAt = Time.unscaledTime;
        }

        private static string DiagnosticSourceName(UnityEngine.Object source, int prefab = 0)
        {
            GameObject obj = source as GameObject;
            if (!obj && source is Component component && component)
                obj = component.gameObject;
            if (obj)
                return Utils.GetPrefabName(obj) + " #" + obj.GetInstanceID();
            if (prefab != 0)
            {
                GameObject template = ZNetScene.instance ? ZNetScene.instance.GetPrefab(prefab) : null;
                return template ? template.name : "prefab hash " + prefab;
            }
            return ReferenceEquals(source, null) ? "<none>" : "<destroyed " + source.GetType().Name + ">";
        }

        private void AppendRefreshDiagnostics(StringBuilder text, SnowPiece state)
        {
            SnowGeometryDiagnostics trace = state.Region?.Diagnostics;
            DiagnosticLine(text, "Cover checks: region={0} piece={1} | piece cover hints={2}",
                state.Region?.DiagnosticCoverChecks ?? 0L, state.DiagnosticCoverChecks, state.DiagnosticCoverHints);
            if (trace == null)
                DiagnosticLine(text, "Geometry events: none observed");
            else
            {
                DiagnosticLine(text, "Geometry events: total={0} add/remove/move/health/terrain/rock/placed/local/ready/winter/settings/other={1}/{2}/{3}/{4}/{5}/{6}/{7}/{8}/{9}/{10}/{11}/{12}",
                    trace.Total,
                    trace.Counts[(int)SnowGeometryCause.ObjectAdded], trace.Counts[(int)SnowGeometryCause.ObjectRemoved],
                    trace.Counts[(int)SnowGeometryCause.TransformChanged], trace.Counts[(int)SnowGeometryCause.HealthVisualChanged],
                    trace.Counts[(int)SnowGeometryCause.TerrainChanged], trace.Counts[(int)SnowGeometryCause.RockChanged],
                    trace.Counts[(int)SnowGeometryCause.Placed], trace.Counts[(int)SnowGeometryCause.PieceMoved],
                    trace.Counts[(int)SnowGeometryCause.AreaReady], trace.Counts[(int)SnowGeometryCause.WinterStart],
                    trace.Counts[(int)SnowGeometryCause.Settings], trace.Counts[(int)SnowGeometryCause.Other]);
                DiagnosticLine(text, "Last geometry: {0} | source={1} ZDO={2} at=({3:F2}, {4:F2}, {5:F2}) age={6:F2}s",
                    trace.LastCause, DiagnosticSourceName(trace.Source, trace.SourcePrefab), trace.SourceId,
                    trace.Position.x, trace.Position.y, trace.Position.z, Time.unscaledTime - trace.At);
            }
            DiagnosticLine(text, "Heat watch (all): fireplace sources={0} native samples={1} | other sources={2} polls={3} | activity changes={4} reindexes={5}",
                heatSources.Count - polledHeatSources.Count, heatDiagnostics.NativeFireplaceSamples,
                polledHeatSources.Count, heatDiagnostics.OtherSourcePolls, heatDiagnostics.ActivityChanges, heatDiagnostics.Reindexes);
            if (heatDiagnostics.ActivityChanges != 0)
                DiagnosticLine(text, "Last heat activity: {0} | active areas={1} age={2:F2}s",
                    DiagnosticSourceName(heatDiagnostics.ActivitySource), heatDiagnostics.ActiveAreas,
                    Time.unscaledTime - heatDiagnostics.ActivityAt);
            if (heatDiagnostics.Reindexes != 0)
                DiagnosticLine(text, "Last heat reindex: {0} | source={1} age={2:F2}s",
                    heatDiagnostics.ReindexCause, DiagnosticSourceName(heatDiagnostics.ReindexSource),
                    Time.unscaledTime - heatDiagnostics.ReindexAt);
        }
    }
}
