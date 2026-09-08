using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Seasons.BloodMoon
{
    internal static partial class BloodMoonJson
    {
        private sealed class BloodMoonContractResolver : DefaultContractResolver
        {
            private static readonly Dictionary<string, string[]> FieldContracts =
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["Seasons.BloodMoon.BloodMoonNetwork+ResyncEnvelope"] =
                        new[] { "Protocol", "Global", "Participants", "OwnDetail" },
                    ["Seasons.BloodMoon.BloodMoonOutcomeQueue+Store"] =
                        new[] { "Schema", "WorldUid", "Revision", "UpdatedAtUtcTicks", "Pending" },
                    ["Seasons.BloodMoon.BloodMoonOutcomeQueue+PendingOutcome"] =
                        new[] { "EventId", "PlayerId", "RewardPayload", "Chronicle" },
                    ["Seasons.BloodMoon.BloodMoonRecovery+PersistedRecovery"] =
                        new[] { "WorldUid", "EventId", "StageOne", "Remaining", "SavedUtcTicks" },
                    ["Seasons.BloodMoon.BloodMoonRecovery+PersistedDefeat"] =
                        new[] { "WorldUid", "EventId" },
                    ["Seasons.BloodMoon.BloodMoonSkills+RewardEnvelope"] =
                        new[] { "WorldUid", "EventId", "BonusBySkill" },
                    ["Seasons.BloodMoon.BloodMoonSkills+RewardApplicationRecord"] =
                        new[] { "Completed", "TargetProgress" },
                    ["Seasons.BloodMoon.BloodMoonSkillReportReliability+PendingSkillGainReport"] =
                        new[] { "Sequence", "Skill", "BaseEquivalent", "LiveBonusEquivalent" },
                    ["Seasons.BloodMoon.BloodMoonSkillReportReliability+LiveSkillReportRecord"] =
                        new[] { "WorldUid", "EventId", "PlayerId", "LastAckSequence", "LastSequence", "LiveBonusUsed", "Pending" },
                    ["Seasons.BloodMoon.BloodMoonSpawnPoolRegistry+Store"] =
                        new[] { "Schema", "WorldUid", "Revision", "UpdatedAtUtcTicks", "PrefabsByEvent" }
                };

            protected override List<MemberInfo> GetSerializableMembers(Type objectType)
            {
                if (!string.Equals(objectType.Namespace, "Seasons.BloodMoon", StringComparison.Ordinal) ||
                    objectType.GetCustomAttributes(typeof(JsonObjectAttribute), inherit: true).Length != 0)
                {
                    return base.GetSerializableMembers(objectType);
                }

                if (!FieldContracts.TryGetValue(objectType.FullName ?? string.Empty, out string[] fieldNames))
                {
                    throw new JsonSerializationException(
                        $"Blood Moon JSON type '{objectType.FullName}' has no explicit contract.");
                }

                List<MemberInfo> members = new List<MemberInfo>(fieldNames.Length);
                foreach (string fieldName in fieldNames)
                {
                    FieldInfo field = objectType.GetField(
                        fieldName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field == null)
                    {
                        throw new JsonSerializationException(
                            $"Required JSON field '{objectType.FullName}.{fieldName}' is missing.");
                    }
                    members.Add(field);
                }
                return members;
            }
        }

        // The envelope is a private nested transport type and cannot carry attributes from this file.
        private sealed class ResyncEnvelopeJsonConverter : JsonConverter
        {
            private static readonly string[] FieldNames = { "Protocol", "Global", "Participants", "OwnDetail" };

            public override bool CanConvert(Type objectType)
            {
                return string.Equals(
                    objectType.FullName,
                    "Seasons.BloodMoon.BloodMoonNetwork+ResyncEnvelope",
                    StringComparison.Ordinal);
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                foreach (string fieldName in FieldNames)
                {
                    FieldInfo field = GetField(value.GetType(), fieldName);
                    writer.WritePropertyName(fieldName);
                    serializer.Serialize(writer, field.GetValue(value));
                }
                writer.WriteEndObject();
            }

            public override object ReadJson(
                JsonReader reader,
                Type objectType,
                object existingValue,
                JsonSerializer serializer)
            {
                JObject source = JObject.Load(reader);
                object result = Activator.CreateInstance(objectType, nonPublic: true);
                foreach (string fieldName in FieldNames)
                {
                    JToken token = source.GetValue(fieldName, StringComparison.Ordinal);
                    if (token == null)
                        continue;

                    FieldInfo field = GetField(objectType, fieldName);
                    object fieldValue = token.Type == JTokenType.Null
                        ? null
                        : token.ToObject(field.FieldType, serializer);
                    field.SetValue(result, fieldValue);
                }
                return result;
            }

            private static FieldInfo GetField(Type type, string fieldName)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null)
                    throw new JsonSerializationException(
                        $"Required resync envelope field '{fieldName}' is missing.");
                return field;
            }
        }
    }
}
