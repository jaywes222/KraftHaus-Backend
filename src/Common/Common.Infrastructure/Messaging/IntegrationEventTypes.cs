using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Common.IntegrationEvents;

namespace Common.Infrastructure.Messaging;

/// <summary>
///     Name ↔ type map of every integration event declared in <c>Common.IntegrationEvents</c>. The short class name is
///     the stable wire name stored in <c>outbox_message.Type</c>, so renaming an event class is a breaking change.
/// </summary>
public static class IntegrationEventTypes
{
    private static readonly FrozenDictionary<string, Type> ByName = Build();

    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string NameOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return ByName.TryGetValue(type.Name, out var known) && known == type
            ? type.Name
            : throw new ArgumentException($"{type.FullName} is not an integration event in Common.IntegrationEvents.", nameof(type));
    }

    public static Type? Find(string name) => ByName.GetValueOrDefault(name);

    private static FrozenDictionary<string, Type> Build()
    {
        var types = typeof(IntegrationEvent).Assembly
            .GetTypes()
            .Where(x => x is { IsAbstract: false, IsClass: true } && typeof(IntegrationEvent).IsAssignableFrom(x))
            .ToList();

        var duplicate = types.GroupBy(x => x.Name, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Two integration events share the name '{duplicate.Key}'.");
        }

        return types.ToFrozenDictionary(x => x.Name, StringComparer.Ordinal);
    }
}
