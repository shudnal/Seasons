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
        // Reads existing caches and save fields only. In particular, do not use the
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

            if (state != null)
            {
                DiagnosticLine(text, "Runtime: valid={0} confirmed={1} simulates={2} bucket={3} | Snow={4:F5} [{5:F2}, {6:F2}]",
                    state.Valid, state.Confirmed, state.Simulates, state.Bucket, state.Snow, state.Minimum, state.Maximum);
                DiagnosticLine(text, "State: saved={0} construction={1} epoch={2} | publish={3} initial={4} ownerless={5}",
                    state.Saved, state.Construction, state.Epoch, state.MayPublish, state.AllowInitialPublication, state.AllowOwnerlessPublication);
                DiagnosticLine(text, "Queues: refresh={0} ({1}) publish={2}/{3} visual={4}/{5}",
                    state.RefreshQueued, state.Refresh, state.PublishQueued, state.ForcePublish, state.VisualQueued, state.ForceVisual);
                SnowRegion region = state.Region;
                DiagnosticLine(text, "Area: ready={0} dirty={1} generation={2}/{3} geometry={4}/{5} | region queue={6} ({7}; {8})",
                    region != null && region.Ready, region != null && region.ReadinessDirty,
                    state.ReadyGeneration, region?.ReadyGeneration ?? -1, state.GeometryRevision, region?.GeometryRevision ?? -1,
                    region != null && region.Queued, region?.Scanning ?? SnowRefresh.None, region?.Pending ?? SnowRefresh.None);
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

        private static void DiagnosticLine(StringBuilder text, string format, params object[] values) =>
            text.AppendFormat(CultureInfo.InvariantCulture, format, values).Append('\n');
    }
}
