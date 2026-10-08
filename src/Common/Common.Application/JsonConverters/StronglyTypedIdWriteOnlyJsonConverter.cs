using System.Text.Json;
using System.Text.Json.Serialization;
using Common.Domain.StronglyTypedIds;

namespace Common.Application.JsonConverters;

public sealed class StronglyTypedIdWriteOnlyJsonConverter
    : JsonConverter<IStronglyTypedId>
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeof(IStronglyTypedId).IsAssignableFrom(typeToConvert);
    }

    public override IStronglyTypedId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        throw new NotSupportedException(
            "Direct deserialization is not supported. " +
            "Use StronglyTypedIdReadOnlyJsonConverter for request deserialization.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        IStronglyTypedId value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value.ToString());
    }
}
