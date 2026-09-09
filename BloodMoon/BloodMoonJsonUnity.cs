using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static partial class BloodMoonJson
    {
        private abstract class UnityValueJsonConverter : JsonConverter
        {
            protected static JObject ReadObject(JsonReader reader, string typeName)
            {
                if (reader.TokenType == JsonToken.Null)
                    throw new JsonSerializationException(
                        $"{typeName} cannot be null at '{reader.Path}'.");
                if (reader.TokenType != JsonToken.StartObject)
                    throw new JsonSerializationException(
                        $"{typeName} must be a JSON object at '{reader.Path}'.");
                return JObject.Load(reader);
            }

            protected static float ReadFiniteComponent(
                JObject value,
                string component,
                string path)
            {
                JToken token = value.GetValue(component, StringComparison.Ordinal);
                if (token == null)
                    throw new JsonSerializationException(
                        $"Missing required component '{component}' at '{path}'.");
                if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                    throw new JsonSerializationException(
                        $"Component '{component}' at '{path}' must be numeric.");

                double number;
                try
                {
                    number = token.Value<double>();
                }
                catch (Exception ex)
                {
                    throw new JsonSerializationException(
                        $"Component '{component}' at '{path}' is invalid.",
                        ex);
                }

                if (double.IsNaN(number) || double.IsInfinity(number) ||
                    number < -float.MaxValue || number > float.MaxValue)
                {
                    throw new JsonSerializationException(
                        $"Component '{component}' at '{path}' must be finite and fit in a Single.");
                }

                float result = (float)number;
                EnsureFinite(result, path + "." + component);
                return result;
            }

            protected static void WriteFiniteComponent(
                JsonWriter writer,
                string component,
                float value)
            {
                EnsureFinite(value, writer.Path + "." + component);
                writer.WritePropertyName(component);
                writer.WriteValue(value);
            }
        }

        private sealed class Vector3JsonConverter : UnityValueJsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Vector3);
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                Vector3 vector = (Vector3)value;
                writer.WriteStartObject();
                WriteFiniteComponent(writer, "x", vector.x);
                WriteFiniteComponent(writer, "y", vector.y);
                WriteFiniteComponent(writer, "z", vector.z);
                writer.WriteEndObject();
            }

            public override object ReadJson(
                JsonReader reader,
                Type objectType,
                object existingValue,
                JsonSerializer serializer)
            {
                string path = reader.Path;
                JObject value = ReadObject(reader, nameof(Vector3));
                return new Vector3(
                    ReadFiniteComponent(value, "x", path),
                    ReadFiniteComponent(value, "y", path),
                    ReadFiniteComponent(value, "z", path));
            }
        }

        private sealed class QuaternionJsonConverter : UnityValueJsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Quaternion);
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                Quaternion rotation = (Quaternion)value;
                writer.WriteStartObject();
                WriteFiniteComponent(writer, "x", rotation.x);
                WriteFiniteComponent(writer, "y", rotation.y);
                WriteFiniteComponent(writer, "z", rotation.z);
                WriteFiniteComponent(writer, "w", rotation.w);
                writer.WriteEndObject();
            }

            public override object ReadJson(
                JsonReader reader,
                Type objectType,
                object existingValue,
                JsonSerializer serializer)
            {
                string path = reader.Path;
                JObject value = ReadObject(reader, nameof(Quaternion));
                return new Quaternion(
                    ReadFiniteComponent(value, "x", path),
                    ReadFiniteComponent(value, "y", path),
                    ReadFiniteComponent(value, "z", path),
                    ReadFiniteComponent(value, "w", path));
            }
        }
    }
}
