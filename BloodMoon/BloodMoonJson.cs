using Newtonsoft.Json;
using System.Globalization;

namespace Seasons.BloodMoon
{
    internal static partial class BloodMoonJson
    {
        private static readonly JsonSerializerSettings persistenceSettings = CreateSettings(Formatting.Indented);
        private static readonly JsonSerializerSettings networkSettings = CreateSettings(Formatting.None);

        internal static string SerializePersistence<T>(T value)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(value, persistenceSettings);
        }

        internal static T DeserializePersistence<T>(string json)
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json, persistenceSettings);
        }

        internal static string SerializeNetwork<T>(T value)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(value, networkSettings);
        }

        internal static string SerializeNetwork(object value)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(value, networkSettings);
        }

        internal static T DeserializeNetwork<T>(string json)
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json, networkSettings);
        }

        private static JsonSerializerSettings CreateSettings(Formatting formatting)
        {
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                Formatting = formatting,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                ReferenceLoopHandling = ReferenceLoopHandling.Error,
                TypeNameHandling = TypeNameHandling.None,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                MaxDepth = 64,
                ContractResolver = new BloodMoonContractResolver()
            };
            settings.Converters.Add(new FiniteSingleJsonConverter());
            settings.Converters.Add(new FiniteDoubleJsonConverter());
            settings.Converters.Add(new Vector3JsonConverter());
            settings.Converters.Add(new QuaternionJsonConverter());
            settings.Converters.Add(new ResyncEnvelopeJsonConverter());
            return settings;
        }
    }

    // Existing Blood Moon JSON call sites used the Newtonsoft type directly. This namespace-local
    // facade routes them through the controlled settings without changing unrelated Seasons JSON.
    internal static class JsonConvert
    {
        public static string SerializeObject(object value)
        {
            return BloodMoonJson.SerializeNetwork(value);
        }

        public static string SerializeObject(object value, JsonSerializerSettings settings)
        {
            _ = settings;
            return BloodMoonJson.SerializePersistence(value);
        }

        public static T DeserializeObject<T>(string json)
        {
            return BloodMoonJson.DeserializeNetwork<T>(json);
        }

        public static T DeserializeObject<T>(string json, JsonSerializerSettings settings)
        {
            _ = settings;
            return BloodMoonJson.DeserializePersistence<T>(json);
        }
    }
}
