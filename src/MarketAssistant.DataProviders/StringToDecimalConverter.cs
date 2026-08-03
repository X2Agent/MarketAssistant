using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketAssistant.DataProviders;

/// <summary>
/// 将字符串格式的数字转换为 decimal（币安等加密货币 API 返回的价格字段通常是字符串）。
/// 使用 JsonConverterFactory 同时支持 decimal 和 decimal? 两种字段类型。
/// </summary>
public sealed class StringToDecimalConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert == typeof(decimal) || typeToConvert == typeof(decimal?);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => typeToConvert == typeof(decimal?)
            ? new NullableDecimalConverter()
            : new NonNullableDecimalConverter();

    private sealed class NonNullableDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var stringValue = reader.GetString();
                if (decimal.TryParse(stringValue, out var value))
                    return value;
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetDecimal();
            }

            return 0m;
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }

    private sealed class NullableDecimalConverter : JsonConverter<decimal?>
    {
        public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.String)
            {
                var stringValue = reader.GetString();
                if (string.IsNullOrEmpty(stringValue))
                    return null;
                if (decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                    return value;
                return null;
            }

            if (reader.TokenType == JsonTokenType.Number)
                return reader.GetDecimal();

            return null;
        }

        public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
                writer.WriteStringValue(value.Value.ToString());
            else
                writer.WriteNullValue();
        }
    }
}
