using BepInEx;
using BepInEx.Configuration;
using ConditionalConfigSync;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    /// <summary>Persistent floe settings; runtime fields are updated on changes, never polled per floe.</summary>
    public static class SeasonalIceFloeSettings
    {
        public const string FileName = pluginID + ".IceFloes.cfg";
        public static ConfigFile Configuration { get; private set; }
        private const string WorldSection = "Season - Winter ocean";
        private static readonly List<Action> applyRuntime = new List<Action>();
        private static FileSystemWatcher watcher;
        private static int reloadRequested;
        private static float reloadAt = -1f;
        private static int reloadRetries;
        private static bool reloading;

        internal static void Initialize(ConfigFile main)
        {
            if (Configuration != null)
                return;
            string path = Path.Combine(Paths.ConfigPath, FileName);
            bool mainSave = main.SaveOnConfigSet;
            main.SaveOnConfigSet = false;
            try
            {
                Configuration = new ConfigFile(path, false, instance.Info.Metadata) { SaveOnConfigSet = false };
                enableIceFloes = World("Enable ice floes in winter", true,
                    "Enable seasonal ocean floes. Disabling removes marked floes and resets their placement markers on the server.");
                iceFloesInWinterDays = World("Fill the water with ice floes at given days from to", new Vector2(4f, 10f),
                    "Inclusive winter-day range for seasonal floes. Outside this interval the server removes them.", new OrderedRange(1f, 10000f, new Vector2(4f, 10f)));
                amountOfIceFloesInWinterDays = World("Amount of ice floes in one zone", new Vector2(10f, 15f),
                    "Random number of placement attempts per 64x64 zone, not a world-wide limit. Obstacles and spacing may reduce the final count. Changes affect new placement; disable and re-enable floes to regenerate existing zones.",
                    new OrderedRange(0f, 10000f, new Vector2(10f, 15f), integers: true));
                iceFloesScale = World("Scale of ice floes", new Vector2(1.25f, 2.5f),
                    "Random base scale range before the existing ocean-depth and vertical-scale adjustments. Applies to newly spawned floes; does not resize saved instances.",
                    new OrderedRange(0.1f, 10f, new Vector2(1.25f, 2.5f)));
                iceFloesHealth = World("Health of ice floes", 20f,
                    "Base health scaled by floe volume and world level during spawning. Existing floes require respawning to change health.", new FiniteRange(1f, 100000f, 20f));
                logFloes = Bind("Test", "Log ice floes", false,
                    "Log ice-floe placement and removal.", null, ConfigSyncMode.AlwaysClientControlled);

                BindRuntime("Ice floes - Surface", "ProbeDistance", 2f, "Wind-aligned sampling half-distance in world meters.", v => IceFloeClimb.ProbeDistance = v, 0.25f, 20f);
                BindRuntime("Ice floes - Surface", "ScaleProbeDistance", false, "Scale sampling distances with the floe's horizontal scale.", v => IceFloeClimb.ScaleProbeDistance = v);
                BindRuntime("Ice floes - Surface", "SecondarySwellWeight", 1f, "Weight of the four secondary large-wave terms used for tilt. Short ripples are excluded from tilt.", v => IceFloeClimb.SecondarySwellWeight = v, 0f, 1f);
                BindRuntime("Ice floes - Surface", "UseFullWaterHeight", true, "Use full-spectrum Ocean wave height at normalized depth 1 and surface offset 0, with filtered large-wave tilt.", v => IceFloeClimb.UseFullWaterHeight = v);
                BindRuntime("Ice floes - Buoyancy", "ApplyBuoyancy", true, "Enable dynamic buoyancy. This switch does not disable kinematic visual tracking.", v => IceFloeClimb.ApplyBuoyancy = v);
                BindRuntime("Ice floes - Buoyancy", "ApplyVerticalWaterDamping", true, "Damp dynamic vertical motion relative to water.", v => IceFloeClimb.ApplyVerticalWaterDamping = v);
                BindRuntime("Ice floes - Buoyancy", "ApplyHorizontalWaterDamping", true, "Damp dynamic horizontal motion.", v => IceFloeClimb.ApplyHorizontalWaterDamping = v);
                BindRuntime("Ice floes - Buoyancy", "MassMultiplier", 4f, "Actual collision mass relative to saved seasonal floe mass. Does not rewrite that saved base mass.", v => IceFloeClimb.MassMultiplier = v, 0.25f, 20f);
                BindRuntime("Ice floes - Buoyancy", "HullThickness", 1f, "Effective displacement thickness before vertical scale; not the measured collider thickness.", v => IceFloeClimb.HullThickness = v, 0.1f, 10f);
                BindRuntime("Ice floes - Buoyancy", "RelativeDensity", 0.9f, "Effective ice/water density ratio. Controls reserve lift, independently of geometric immersion.", v => IceFloeClimb.RelativeDensity = v, 0.5f, 0.99f);
                BindRuntime("Ice floes - Buoyancy", "RestingSubmergence", 0.7f, "Nominal fraction of collider thickness below the surface. 0.7 means 70 percent, not a submerged mesh-volume calculation.", v => IceFloeClimb.RestingSubmergence = v, 0.1f, 0.95f);
                BindRuntime("Ice floes - Buoyancy", "HeightOffset", 0f, "Additional waterline offset in world meters. Negative lowers the floe.", v => IceFloeClimb.HeightOffset = v, -10f, 10f);
                BindRuntime("Ice floes - Buoyancy", "VerticalDampingRatio", 1f, "Damping ratio against water-relative vertical velocity.", v => IceFloeClimb.VerticalDampingRatio = v, 0f, 5f);
                BindRuntime("Ice floes - Buoyancy", "MaxWaterDragAcceleration", 6f, "Maximum vertical water-drag acceleration in meters per second squared.", v => IceFloeClimb.MaxWaterDragAcceleration = v, 0f, 50f);
                BindRuntime("Ice floes - Buoyancy", "HorizontalDamping", 0.15f, "Horizontal water-drag rate in inverse seconds.", v => IceFloeClimb.HorizontalDamping = v, 0f, 10f);
                BindRuntime("Ice floes - Tilt", "ApplySurfaceAlignment", true, "Enable dynamic torque aligning floe up with the surface normal.", v => IceFloeClimb.ApplySurfaceAlignment = v);
                BindRuntime("Ice floes - Tilt", "ApplyTiltDamping", true, "Enable dynamic tilt damping relative to the moving surface.", v => IceFloeClimb.ApplyTiltDamping = v);
                BindRuntime("Ice floes - Tilt", "ApplyYawDamping", true, "Enable yaw drag without imposing a heading.", v => IceFloeClimb.ApplyYawDamping = v);
                BindRuntime("Ice floes - Tilt", "TiltFrequency", 0.65f, "Dynamic tilt response frequency in hertz.", v => IceFloeClimb.TiltFrequency = v, 0f, 3f);
                BindRuntime("Ice floes - Tilt", "TiltDampingRatio", 1f, "Dynamic tilt damping ratio.", v => IceFloeClimb.TiltDampingRatio = v, 0f, 5f);
                BindRuntime("Ice floes - Tilt", "MaxTiltAcceleration", 1.5f, "Maximum commanded tilt acceleration in radians per second squared.", v => IceFloeClimb.MaxTiltAcceleration = v, 0f, 20f);
                BindRuntime("Ice floes - Tilt", "MaxSurfaceTilt", 45f, "Maximum target surface inclination in degrees; does not constrain collision rotation.", v => IceFloeClimb.MaxSurfaceTilt = v, 0f, 80f);
                BindRuntime("Ice floes - Tilt", "YawDamping", 0.15f, "Yaw water-drag rate in inverse seconds.", v => IceFloeClimb.YawDamping = v, 0f, 10f);
                BindRuntime("Ice floes - Motion", "KinematicResponseSeconds", 0.15f, "Response time for ownerless kinematic surface tracking.", v => IceFloeClimb.KinematicResponseSeconds = v, 0.02f, 1f);
                BindRuntime("Ice floes - Authority", "KinematicPublishIntervalSeconds", 0.2f,
                    "Seconds between ownerless kinematic ZDO pose publications, plus up to 0.02 seconds of per-floe staggering. Does not slow local motion or native-owned physics. 0.1 restores the previous rate. The upper bound preserves the existing 0.5-second replica velocity freshness window.",
                    v => IceFloeClimb.KinematicPublishIntervalSeconds = v, 0.1f, 0.25f);

                // Quality and participation are peer-local. Physical coefficients above are
                // server-controlled so a normal ownership transfer does not change the model.
                BindRuntime("Ice floes - Background", "EnableBackgroundWaveForecast", true,
                    "Build rolling forecasts for locally simulated ownerless kinematic floes on one background thread. Disable for the synchronous reference path. Native dynamic physics and foreign replicas are not sent to the worker.",
                    v => IceFloeClimb.EnableBackgroundWaveForecast = v, local: true);
                BindRuntime("Ice floes - Background", "BackgroundForecastSeconds", 10f,
                    "Desired stable-wind reserve in server-world seconds. Filled gradually in one-second blocks; urgent coverage wins. Wind changes use short replacements instead of waiting for this entire reserve.",
                    v => IceFloeClimb.BackgroundForecastSeconds = v, 2f, 10f, local: true);
                BindRuntime("Ice floes - Prediction", "EnableWavePrediction", true, "Reuse distant surface forecasts instead of recalculating every update.", v => IceFloeClimb.EnableWavePrediction = v, local: true);
                BindRuntime("Ice floes - Prediction", "MinimumPredictionSeconds", 0.1f, "Minimum synchronous forecast horizon. Also controls distance-based background wind refreshes, with a 0.5-second refresh floor.", v => IceFloeClimb.MinimumPredictionSeconds = v, 0.05f, 2f, local: true);
                BindRuntime("Ice floes - Prediction", "MaximumPredictionSeconds", 2f, "Maximum synchronous horizon and distance-based background wind refresh interval. Background reserve is configured separately.", v => IceFloeClimb.MaximumPredictionSeconds = v, 0.1f, 2f, local: true);
                BindRuntime("Ice floes - Prediction", "PredictionKnotSeconds", 0.25f, "Maximum spacing between forecast knots in seconds.", v => IceFloeClimb.PredictionKnotSeconds = v, 0.1f, 0.25f, local: true);
                BindRuntime("Ice floes - Prediction", "PredictionPositionTolerance", 0.5f, "Maximum unpredicted horizontal displacement before rebuilding a forecast, in meters.", v => IceFloeClimb.PredictionPositionTolerance = v, 0.1f, 2f, local: true);
                BindRuntime("Ice floes - Distant visual", "DistantBobAmplitude", 0.08f, "Local bob amplitude beyond visible waves, in meters. Never published to ZDO.", v => IceFloeClimb.DistantBobAmplitude = v, 0f, 0.25f, local: true);
                BindRuntime("Ice floes - Distant visual", "DistantBobPeriod", 6f, "Local distant-bob period in seconds.", v => IceFloeClimb.DistantBobPeriod = v, 2f, 30f, local: true);
                BindRuntime("Ice floes - Authority", "EnableFallbackSimulation", true, "Allow this peer to participate in the existing ownerless election. Does not claim native ownership.", v => IceFloeClimb.EnableFallbackSimulation = v, local: true);

                Configuration.Save();
                RemoveObsoleteMainEntries(main);
                main.Save();
            }
            finally
            {
                main.SaveOnConfigSet = mainSave;
                if (Configuration != null)
                    Configuration.SaveOnConfigSet = true;
            }
            try
            {
                watcher = new FileSystemWatcher(Paths.ConfigPath, FileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    IncludeSubdirectories = false
                };
                watcher.Changed += RequestReload;
                watcher.Created += RequestReload;
                watcher.Renamed += RequestReload;
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception exception)
            {
                watcher?.Dispose();
                watcher = null;
                LogWarning($"Ice-floe configuration file watching is unavailable. Use SeasonalIceFloeSettings.Reload(): {exception.Message}");
            }
        }

        private static ConfigEntry<T> World<T>(string name, T value, string description, AcceptableValueBase range = null) =>
            Bind(WorldSection, name, value, description, range, ConfigSyncMode.AlwaysServerControlled);

        private static ConfigEntry<T> Bind<T>(string section, string name, T value,
            string description, AcceptableValueBase range, ConfigSyncMode mode) =>
            configSync.AddConfigEntry(Configuration, section, name, value, new ConfigDescription(description, range),
                syncMode: mode, serverControlledByDefault: mode != ConfigSyncMode.AlwaysClientControlled).SourceConfig;

        private static void RemoveObsoleteMainEntries(ConfigFile main)
        {
            ConfigDefinition[] definitions =
            {
                new ConfigDefinition(WorldSection, "Enable ice floes in winter"),
                new ConfigDefinition(WorldSection, "Fill the water with ice floes at given days from to"),
                new ConfigDefinition(WorldSection, "Amount of ice floes in one zone"),
                new ConfigDefinition(WorldSection, "Scale of ice floes"),
                new ConfigDefinition(WorldSection, "Health of ice floes"),
                new ConfigDefinition("Test", "Log ice floes")
            };
            foreach (ConfigDefinition definition in definitions)
            {
                // BepInEx retains unbound entries on Save. Consume this obsolete key as
                // opaque text, then discard it through the public API. Never read its Value,
                // register it with config sync, or copy it into the independent floe file.
                main.Remove(definition);
                main.Bind<string>(definition, string.Empty);
                main.Remove(definition);
            }
        }

        private static void BindRuntime(string section, string name, float value, string description, Action<float> apply,
            float minimum, float maximum, bool local = false) =>
            BindRuntime(section, name, value, description, apply, new FiniteRange(minimum, maximum, value), local);

        private static void BindRuntime<T>(string section, string name, T value, string description, Action<T> apply,
            AcceptableValueBase range = null, bool local = false)
        {
            ConfigEntry<T> entry = configSync.AddConfigEntry(Configuration, section, name, value,
                new ConfigDescription(description, range),
                syncMode: local ? ConfigSyncMode.AlwaysClientControlled : ConfigSyncMode.AlwaysServerControlled,
                serverControlledByDefault: !local).SourceConfig;
            Action action = () =>
            {
                apply(entry.Value);
                IceFloeClimb.InvalidateSharedMotionSettings();
            };
            action();
            entry.SettingChanged += (_, __) => action();
            applyRuntime.Add(action);
        }

        /// <summary>Reapply effective configured values after temporary inspector experiments. No file writes.</summary>
        public static void ApplyConfiguredRuntimeValues()
        {
            foreach (Action apply in applyRuntime)
                apply();
        }

        private static void RequestReload(object sender, FileSystemEventArgs args) => Interlocked.Exchange(ref reloadRequested, 1);

        // File watcher threads only enqueue; ConfigEntry callbacks can touch game objects.
        internal static void Update()
        {
            if (Interlocked.Exchange(ref reloadRequested, 0) != 0)
            {
                reloadAt = Time.realtimeSinceStartup + 0.25f;
                reloadRetries = 0;
            }
            if (reloadAt < 0f || Time.realtimeSinceStartup < reloadAt)
                return;
            reloadAt = -1f;
            if (!TryReload() && reloadRetries++ < 3)
                reloadAt = Time.realtimeSinceStartup + 0.5f;
        }

        /// <summary>Reload on the main thread. Connected clients remain subject to config-sync policy.</summary>
        public static void Reload() => TryReload();

        private static bool TryReload()
        {
            if (Configuration == null || reloading || !File.Exists(Configuration.ConfigFilePath))
                return false;
            reloading = true;
            bool save = Configuration.SaveOnConfigSet;
            Configuration.SaveOnConfigSet = false;
            try
            {
                Configuration.Reload();
                ApplyConfiguredRuntimeValues();
                return true;
            }
            catch (Exception exception)
            {
                LogWarning($"Unable to reload ice-floe configuration: {exception.Message}");
                return false;
            }
            finally
            {
                Configuration.SaveOnConfigSet = save;
                reloading = false;
            }
        }

        internal static void Dispose()
        {
            FloeForecastWorker.Shutdown();
            watcher?.Dispose();
            watcher = null;
            Interlocked.Exchange(ref reloadRequested, 0);
            reloadAt = -1f;
        }

        private sealed class FiniteRange : AcceptableValueBase
        {
            private readonly float minimum, maximum, fallback;
            internal FiniteRange(float minimum, float maximum, float fallback) : base(typeof(float))
            { this.minimum = minimum; this.maximum = maximum; this.fallback = fallback; }
            public override object Clamp(object value) => value is float number && !float.IsNaN(number) && !float.IsInfinity(number)
                ? Mathf.Clamp(number, minimum, maximum) : fallback;
            public override bool IsValid(object value) => value is float number && number >= minimum && number <= maximum;
            public override string ToDescriptionString() => FormattableString.Invariant($"# Acceptable value range: From {minimum} to {maximum}");
        }

        private sealed class OrderedRange : AcceptableValueBase
        {
            private readonly float minimum, maximum;
            private readonly Vector2 fallback;
            private readonly bool integers;
            internal OrderedRange(float minimum, float maximum, Vector2 fallback, bool integers = false) : base(typeof(Vector2))
            { this.minimum = minimum; this.maximum = maximum; this.fallback = fallback; this.integers = integers; }
            public override object Clamp(object value)
            {
                if (!(value is Vector2 range) || float.IsNaN(range.x) || float.IsNaN(range.y) || float.IsInfinity(range.x) || float.IsInfinity(range.y))
                    return fallback;
                float low = Mathf.Clamp(Mathf.Min(range.x, range.y), minimum, maximum);
                float high = Mathf.Clamp(Mathf.Max(range.x, range.y), minimum, maximum);
                return integers ? new Vector2(Mathf.Floor(low), Mathf.Floor(high)) : new Vector2(low, high);
            }
            public override bool IsValid(object value) => value is Vector2 range && range.Equals(Clamp(range));
            public override string ToDescriptionString() => FormattableString.Invariant($"# Ordered minimum/maximum range: {minimum} to {maximum}");
        }
    }
}
