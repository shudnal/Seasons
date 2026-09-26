using BepInEx;
using BepInEx.Configuration;
using ConditionalConfigSync;
using Newtonsoft.Json;
using System;
using System.IO;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    /// <summary>JSON-backed tuning with the three seasonal controls in the main config.</summary>
    public static class SeasonalIceFloeSettings
    {
        private const string WorldSection = "Season - Winter ocean";
        private static IceFloeConfiguration current = new IceFloeConfiguration();
        private static bool initialized;
        private static readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Auto,
            TypeNameHandling = TypeNameHandling.None,
            MaxDepth = 16
        };

        public static Vector2 AmountPerZone { get; private set; } = new Vector2(10f, 15f);
        public static Vector2 Scale { get; private set; } = new Vector2(1.25f, 2.5f);

        internal static void Initialize(ConfigFile main)
        {
            if (initialized)
                return;
            enableIceFloes = configSync.AddConfigEntry(main, WorldSection, "Enable ice floes in winter", true,
                new ConfigDescription("Enable seasonal ocean floes. Disabling removes marked floes and resets their placement markers on the server. Advanced settings are in Seasonal ice floes.json."),
                syncMode: ConfigSyncMode.AlwaysServerControlled, serverControlledByDefault: true).SourceConfig;
            iceFloesInWinterDays = configSync.AddConfigEntry(main, WorldSection, "Fill the water with ice floes at given days from to", new Vector2(4f, 10f),
                new ConfigDescription("Inclusive winter-day range for seasonal floes. Outside this interval the server removes them.", new WinterDayRange()),
                syncMode: ConfigSyncMode.AlwaysServerControlled, serverControlledByDefault: true).SourceConfig;
            iceFloesHealth = configSync.AddConfigEntry(main, WorldSection, "Health of ice floes", 20f,
                new ConfigDescription("Base health scaled by floe volume and world level during spawning. Existing floes require respawning to change health.", new HealthRange()),
                syncMode: ConfigSyncMode.AlwaysServerControlled, serverControlledByDefault: true).SourceConfig;
            logFloes = configSync.AddConfigEntry(main, "Test", "Log ice floes", false,
                new ConfigDescription("Log ice-floe placement and removal."),
                syncMode: ConfigSyncMode.AlwaysClientControlled).SourceConfig;
            RemoveObsoleteConfiguration(main);
            ApplyConfiguredRuntimeValues();
            initialized = true;
        }

        private sealed class WinterDayRange : AcceptableValueBase
        {
            internal WinterDayRange() : base(typeof(Vector2)) { }
            public override object Clamp(object value)
            {
                if (!(value is Vector2 range) || float.IsNaN(range.x) || float.IsNaN(range.y) ||
                    float.IsInfinity(range.x) || float.IsInfinity(range.y))
                    return new Vector2(4f, 10f);
                return new Vector2(Mathf.Clamp(Mathf.Min(range.x, range.y), 1f, 10000f),
                    Mathf.Clamp(Mathf.Max(range.x, range.y), 1f, 10000f));
            }
            public override bool IsValid(object value) => value is Vector2 range && range.Equals(Clamp(range));
            public override string ToDescriptionString() => "# Ordered minimum/maximum range: 1 to 10000";
        }

        private sealed class HealthRange : AcceptableValueBase
        {
            internal HealthRange() : base(typeof(float)) { }
            public override object Clamp(object value) => value is float number && !float.IsNaN(number) && !float.IsInfinity(number)
                ? Mathf.Clamp(number, 1f, 100000f) : 20f;
            public override bool IsValid(object value) => value is float number && number >= 1f && number <= 100000f;
            public override string ToDescriptionString() => "# Acceptable value range: From 1 to 100000";
        }

        private static void RemoveObsoleteConfiguration(ConfigFile main)
        {
            bool save = main.SaveOnConfigSet;
            main.SaveOnConfigSet = false;
            try
            {
                foreach (string name in new[] { "Amount of ice floes in one zone", "Scale of ice floes" })
                {
                    ConfigDefinition definition = new ConfigDefinition(WorldSection, name);
                    // Consume and discard obsolete orphaned entries without reading their values.
                    main.Remove(definition);
                    main.Bind<string>(definition, string.Empty);
                    main.Remove(definition);
                }
                main.Save();
            }
            finally
            {
                main.SaveOnConfigSet = save;
            }

            // The separate cfg is retired, not imported into either the main cfg or JSON.
            try
            {
                File.Delete(Path.Combine(Paths.ConfigPath, pluginID + ".IceFloes.cfg"));
            }
            catch (Exception exception)
            {
                LogWarning($"Unable to remove the obsolete ice-floe cfg; it will not be read: {exception.Message}");
            }
        }

        internal static void SaveDefaultSettings(string folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, SeasonSettings.seasonalIceFloesFileName),
                JsonConvert.SerializeObject(new IceFloeConfiguration(), Formatting.Indented));
        }

        private static IceFloeConfiguration ReadSettings(string json)
        {
            IceFloeConfiguration settings = new IceFloeConfiguration();
            if (!string.IsNullOrWhiteSpace(json))
                JsonConvert.PopulateObject(json, settings, serializerSettings);
            settings.Normalize();
            return settings;
        }

        public static void ApplySynchronizedSettings()
        {
            IceFloeConfiguration settings;
            try
            {
                settings = ReadSettings(seasonalIceFloesJSON.Value);
            }
            catch (Exception exception)
            {
                LogWarning($"Error parsing synchronized seasonal ice-floe settings; previous values retained: {exception.Message}");
                return;
            }
            Vector2 previousAmount = AmountPerZone;
            current = settings;
            ApplyConfiguredRuntimeValues();
            if (SeasonState.IsActive && !previousAmount.Equals(AmountPerZone))
                ZoneSystemVariantController.UpdateWaterState();
            LogInfo(string.IsNullOrWhiteSpace(seasonalIceFloesJSON.Value)
                ? "Seasonal ice-floe settings loaded defaults" : "Seasonal ice-floe settings updated");
        }

        /// <summary>Reapply effective JSON values after temporary inspector edits. No file or pose writes.</summary>
        public static void ApplyConfiguredRuntimeValues()
        {
            AmountPerZone = new Vector2(current.amountPerZone.min, current.amountPerZone.max);
            Scale = new Vector2(current.scale.min, current.scale.max);
            IceFloeClimb.ProbeDistance = current.surface.probeDistance;
            IceFloeClimb.ScaleProbeDistance = current.surface.scaleProbeDistance;
            IceFloeClimb.SecondarySwellWeight = current.surface.secondarySwellWeight;
            IceFloeClimb.UseFullWaterHeight = current.surface.useFullWaterHeight;
            IceFloeClimb.ApplyBuoyancy = current.buoyancy.applyBuoyancy;
            IceFloeClimb.ApplyVerticalWaterDamping = current.buoyancy.applyVerticalWaterDamping;
            IceFloeClimb.ApplyHorizontalWaterDamping = current.buoyancy.applyHorizontalWaterDamping;
            IceFloeClimb.MassMultiplier = current.buoyancy.massMultiplier;
            IceFloeClimb.HullThickness = current.buoyancy.hullThickness;
            IceFloeClimb.RelativeDensity = current.buoyancy.relativeDensity;
            IceFloeClimb.RestingSubmergence = current.buoyancy.restingSubmergence;
            IceFloeClimb.HeightOffset = current.buoyancy.heightOffset;
            IceFloeClimb.VerticalDampingRatio = current.buoyancy.verticalDampingRatio;
            IceFloeClimb.MaxWaterDragAcceleration = current.buoyancy.maxWaterDragAcceleration;
            IceFloeClimb.HorizontalDamping = current.buoyancy.horizontalDamping;
            IceFloeClimb.ApplySurfaceAlignment = current.tilt.applySurfaceAlignment;
            IceFloeClimb.ApplyTiltDamping = current.tilt.applyTiltDamping;
            IceFloeClimb.ApplyYawDamping = current.tilt.applyYawDamping;
            IceFloeClimb.TiltFrequency = current.tilt.tiltFrequency;
            IceFloeClimb.TiltDampingRatio = current.tilt.tiltDampingRatio;
            IceFloeClimb.MaxTiltAcceleration = current.tilt.maxTiltAcceleration;
            IceFloeClimb.MaxSurfaceTilt = current.tilt.maxSurfaceTilt;
            IceFloeClimb.YawDamping = current.tilt.yawDamping;
            IceFloeClimb.KinematicResponseSeconds = current.motion.kinematicResponseSeconds;
            IceFloeClimb.KinematicPublishIntervalSeconds = current.authority.kinematicPublishIntervalSeconds;
            // All fields come from the same effective CustomSyncedValue payload.
            // The client group describes runtime tuning, not a separate local source.
            IceFloeClimb.EnableWavePrediction = current.client.enableWavePrediction;
            IceFloeClimb.MinimumPredictionSeconds = current.client.minimumPredictionSeconds;
            IceFloeClimb.MaximumPredictionSeconds = current.client.maximumPredictionSeconds;
            IceFloeClimb.PredictionKnotSeconds = current.client.predictionKnotSeconds;
            IceFloeClimb.PredictionPositionTolerance = current.client.predictionPositionTolerance;
            IceFloeClimb.EnableBackgroundWaveForecast = current.client.enableBackgroundWaveForecast;
            IceFloeClimb.BackgroundForecastSeconds = current.client.backgroundForecastSeconds;
            IceFloeClimb.DistantBobAmplitude = current.client.distantBobAmplitude;
            IceFloeClimb.DistantBobPeriod = current.client.distantBobPeriod;
            IceFloeClimb.EnableFallbackSimulation = current.client.enableFallbackSimulation;
            IceFloeClimb.InvalidateSharedMotionSettings();
        }
    }

    /// <summary>Optional JSON overrides. Omitted fields retain the shipped defaults.</summary>
    [Serializable]
    public sealed class IceFloeConfiguration
    {
        public NumericRange amountPerZone = new NumericRange(10f, 15f);
        public NumericRange scale = new NumericRange(1.25f, 2.5f);
        public SurfaceSettings surface = new SurfaceSettings();
        public BuoyancySettings buoyancy = new BuoyancySettings();
        public TiltSettings tilt = new TiltSettings();
        public MotionSettings motion = new MotionSettings();
        public AuthoritySettings authority = new AuthoritySettings();
        public ClientSettings client = new ClientSettings();

        [Serializable]
        public sealed class NumericRange
        {
            public float min;
            public float max;
            public NumericRange(float min, float max) { this.min = min; this.max = max; }
        }

        [Serializable]
        public sealed class SurfaceSettings
        {
            public float probeDistance = 2f;
            public bool scaleProbeDistance;
            public float secondarySwellWeight = 1f;
            public bool useFullWaterHeight = true;
        }

        [Serializable]
        public sealed class BuoyancySettings
        {
            public bool applyBuoyancy = true;
            public bool applyVerticalWaterDamping = true;
            public bool applyHorizontalWaterDamping = true;
            public float massMultiplier = 4f;
            public float hullThickness = 1f;
            public float relativeDensity = 0.9f;
            public float restingSubmergence = 0.7f;
            public float heightOffset;
            public float verticalDampingRatio = 1f;
            public float maxWaterDragAcceleration = 6f;
            public float horizontalDamping = 0.15f;
        }

        [Serializable]
        public sealed class TiltSettings
        {
            public bool applySurfaceAlignment = true;
            public bool applyTiltDamping = true;
            public bool applyYawDamping = true;
            public float tiltFrequency = 0.65f;
            public float tiltDampingRatio = 1f;
            public float maxTiltAcceleration = 1.5f;
            public float maxSurfaceTilt = 45f;
            public float yawDamping = 0.15f;
        }

        [Serializable]
        public sealed class MotionSettings
        {
            public float kinematicResponseSeconds = 0.15f;
        }

        [Serializable]
        public sealed class AuthoritySettings
        {
            public float kinematicPublishIntervalSeconds = 0.2f;
        }

        [Serializable]
        public sealed class ClientSettings
        {
            public bool enableWavePrediction = true;
            public float minimumPredictionSeconds = 0.1f;
            public float maximumPredictionSeconds = 2f;
            public float predictionKnotSeconds = 0.25f;
            public float predictionPositionTolerance = 0.5f;
            public bool enableBackgroundWaveForecast = true;
            public float backgroundForecastSeconds = 10f;
            public float distantBobAmplitude = 0.08f;
            public float distantBobPeriod = 6f;
            public bool enableFallbackSimulation = true;
        }

        private static float Number(float value, float fallback, float minimum, float maximum) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, maximum);

        private static NumericRange Range(NumericRange value, float low, float high, float minimum, float maximum, bool integers = false)
        {
            float a = Number(value.min, low, minimum, maximum);
            float b = Number(value.max, high, minimum, maximum);
            value.min = Mathf.Min(a, b);
            value.max = Mathf.Max(a, b);
            if (integers)
            {
                value.min = Mathf.Floor(value.min);
                value.max = Mathf.Floor(value.max);
            }
            return value;
        }

        internal void Normalize()
        {
            amountPerZone = Range(amountPerZone ?? new NumericRange(10f, 15f), 10f, 15f, 0f, 10000f, integers: true);
            scale = Range(scale ?? new NumericRange(1.25f, 2.5f), 1.25f, 2.5f, 0.1f, 10f);
            surface ??= new SurfaceSettings();
            buoyancy ??= new BuoyancySettings();
            tilt ??= new TiltSettings();
            motion ??= new MotionSettings();
            authority ??= new AuthoritySettings();
            client ??= new ClientSettings();
            surface.probeDistance = Number(surface.probeDistance, 2f, 0.25f, 20f);
            surface.secondarySwellWeight = Number(surface.secondarySwellWeight, 1f, 0f, 1f);
            buoyancy.massMultiplier = Number(buoyancy.massMultiplier, 4f, 0.25f, 20f);
            buoyancy.hullThickness = Number(buoyancy.hullThickness, 1f, 0.1f, 10f);
            buoyancy.relativeDensity = Number(buoyancy.relativeDensity, 0.9f, 0.5f, 0.99f);
            buoyancy.restingSubmergence = Number(buoyancy.restingSubmergence, 0.7f, 0.1f, 0.95f);
            buoyancy.heightOffset = Number(buoyancy.heightOffset, 0f, -10f, 10f);
            buoyancy.verticalDampingRatio = Number(buoyancy.verticalDampingRatio, 1f, 0f, 5f);
            buoyancy.maxWaterDragAcceleration = Number(buoyancy.maxWaterDragAcceleration, 6f, 0f, 50f);
            buoyancy.horizontalDamping = Number(buoyancy.horizontalDamping, 0.15f, 0f, 10f);
            tilt.tiltFrequency = Number(tilt.tiltFrequency, 0.65f, 0f, 3f);
            tilt.tiltDampingRatio = Number(tilt.tiltDampingRatio, 1f, 0f, 5f);
            tilt.maxTiltAcceleration = Number(tilt.maxTiltAcceleration, 1.5f, 0f, 20f);
            tilt.maxSurfaceTilt = Number(tilt.maxSurfaceTilt, 45f, 0f, 80f);
            tilt.yawDamping = Number(tilt.yawDamping, 0.15f, 0f, 10f);
            motion.kinematicResponseSeconds = Number(motion.kinematicResponseSeconds, 0.15f, 0.02f, 1f);
            authority.kinematicPublishIntervalSeconds = Number(authority.kinematicPublishIntervalSeconds, 0.2f, 0.1f, 0.25f);
            client.maximumPredictionSeconds = Number(client.maximumPredictionSeconds, 2f, 0.1f, 2f);
            client.minimumPredictionSeconds = Mathf.Min(Number(client.minimumPredictionSeconds, 0.1f, 0.05f, 2f), client.maximumPredictionSeconds);
            client.predictionKnotSeconds = Number(client.predictionKnotSeconds, 0.25f, 0.1f, 0.25f);
            client.predictionPositionTolerance = Number(client.predictionPositionTolerance, 0.5f, 0.1f, 2f);
            client.backgroundForecastSeconds = Number(client.backgroundForecastSeconds, 10f, 2f, 10f);
            client.distantBobAmplitude = Number(client.distantBobAmplitude, 0.08f, 0f, 0.25f);
            client.distantBobPeriod = Number(client.distantBobPeriod, 6f, 2f, 30f);
        }
    }
}
