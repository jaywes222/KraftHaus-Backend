using System.Text.Json;
using System.Text.Json.Serialization;
using Common.Domain.StronglyTypedIds;

namespace Common.Application.JsonConverters;

public sealed class StronglyTypedIdReadOnlyJsonConverter
    : JsonConverter<IStronglyTypedId>
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeof(IStronglyTypedId).IsAssignableFrom(typeToConvert);
    }

    public override IStronglyTypedId? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Expected a string value for strongly typed ID '{typeToConvert.Name}'.");
        }

        var stringValue = reader.GetString();

        if (string.IsNullOrWhiteSpace(stringValue))
        {
            throw new JsonException(
                $"The ID value for '{typeToConvert.Name}' cannot be null or empty.");
        }

        DefaultIdType parsedValue;

        try
        {
            parsedValue = DefaultIdType.Parse(stringValue);
        }
        catch (Exception ex)
        {
            throw new JsonException(
                $"Error parsing the value '{stringValue}' for type '{typeToConvert.Name}'.",
                ex);
        }

        var effectiveType =
            Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;

        var instance = Activator.CreateInstance(
            effectiveType,
            parsedValue);

        if (instance is not IStronglyTypedId stronglyTypedId)
        {
            throw new JsonException(
                $"Could not create strongly typed ID '{effectiveType.Name}'.");
        }

        return stronglyTypedId;
    }

    public override void Write(
        Utf8JsonWriter writer,
        IStronglyTypedId value,
        JsonSerializerOptions options)
    {
        throw new NotSupportedException(
            "Direct serialization is not supported. " +
            "Use StronglyTypedIdWriteOnlyJsonConverter for response serialization.");
    }
}
