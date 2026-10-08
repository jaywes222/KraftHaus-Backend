using System.Text.Json;
using System.Text.Json.Serialization;
using Common.Domain.StronglyTypedIds;

namespace Common.Application.JsonConverters;

internal sealed class StronglyTypedIdJsonConverter<T> : JsonConverter<T>
    where T : struct, IStronglyTypedId
{
    public override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Expected a string value for '{typeof(T).Name}'.");
        }

        var value = reader.GetString();

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException(
                $"The ID value for '{typeof(T).Name}' cannot be empty.");
        }

        DefaultIdType parsedValue;

        try
        {
            parsedValue = DefaultIdType.Parse(value);
        }
        catch (Exception ex)
        {
            throw new JsonException(
                $"Invalid value '{value}' for '{typeof(T).Name}'.",
                ex);
        }

        var instance = Activator.CreateInstance(
            typeof(T),
            parsedValue);

        if (instance is not T result)
        {
            throw new JsonException(
                $"Could not create '{typeof(T).Name}'.");
        }

        return result;
    }

    public override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value.ToString());
    }
}
