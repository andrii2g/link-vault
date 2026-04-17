using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LinkVault.Api.Serialization;

internal static class UtcDateTimeOffsetJsonConverters
{
    public static void Apply(JsonSerializerOptions options)
    {
        options.Converters.Add(new UtcDateTimeOffsetJsonConverter());
        options.Converters.Add(new NullableUtcDateTimeOffsetJsonConverter());
    }

    private sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
    {
        private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new JsonException("Timestamp value is required.");
            }

            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture));
        }
    }

    private sealed class NullableUtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset?>
    {
        private readonly UtcDateTimeOffsetJsonConverter _innerConverter = new();

        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            return _innerConverter.Read(ref reader, typeof(DateTimeOffset), options);
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            _innerConverter.Write(writer, value.Value, options);
        }
    }
}
