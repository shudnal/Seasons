using Newtonsoft.Json;
using System;
using System.Globalization;

namespace Seasons.BloodMoon
{
    internal static partial class BloodMoonJson
    {
        private sealed class FiniteSingleJsonConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(float);
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                float number = (float)value;
                EnsureFinite(number, writer.Path);
                writer.WriteValue(number);
            }

            public override object ReadJson(
                JsonReader reader,
                Type objectType,
                object existingValue,
                JsonSerializer serializer)
            {
                if (reader.TokenType != JsonToken.Integer && reader.TokenType != JsonToken.Float)
                    throw new JsonSerializationException(
                        $"Single value at '{reader.Path}' must be numeric.");

                float number;
                try
                {
                    number = Convert.ToSingle(reader.Value, CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    throw new JsonSerializationException(
                        $"Single value at '{reader.Path}' is invalid.",
                        ex);
                }

                EnsureFinite(number, reader.Path);
                return number;
            }
        }

        private sealed class FiniteDoubleJsonConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(double);
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                double number = (double)value;
                EnsureFinite(number, writer.Path);
                writer.WriteValue(number);
            }

            public override object ReadJson(
                JsonReader reader,
                Type objectType,
                object existingValue,
                JsonSerializer serializer)
            {
                if (reader.TokenType != JsonToken.Integer && reader.TokenType != JsonToken.Float)
                    throw new JsonSerializationException(
                        $"Double value at '{reader.Path}' must be numeric.");

                double number;
                try
                {
                    number = Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    throw new JsonSerializationException(
                        $"Double value at '{reader.Path}' is invalid.",
                        ex);
                }

                EnsureFinite(number, reader.Path);
                return number;
            }
        }

        private static void EnsureFinite(float value, string path)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new JsonSerializationException(
                    $"Single value at '{path}' must be finite.");
        }

        private static void EnsureFinite(double value, string path)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new JsonSerializationException(
                    $"Double value at '{path}' must be finite.");
        }
    }
}
