using Common.Domain.Events;
using Displays.Domain.Common;
using Displays.Domain.Devices;
using Displays.Infrastructure.Auditing;
using Xunit;

namespace Displays.Tests;

public sealed class AuditMapperTests
{
    /// <summary>Every concrete Displays domain event must have a stable audit action (the mapper throws otherwise).</summary>
    [Fact]
    public void EveryDisplaysDomainEventHasAnAuditAction()
    {
        var eventTypes = typeof(DisplayDevice).Assembly.GetTypes()
            .Where(x => x is { IsAbstract: false, IsClass: true } && typeof(DomainEvent).IsAssignableFrom(x))
            .ToList();

        Assert.NotEmpty(eventTypes);

        var device = DisplayDevice.Pair(
            new DisplayDeviceId(Guid.CreateVersion7()),
            new DisplayDeviceSpec("Main bar TV", null, Guid.CreateVersion7(), null),
            "hash",
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow).Value!;

        foreach (var type in eventTypes)
        {
            var (action, target, _) = DisplaysAuditMapper.Map(Sample(type), device);

            Assert.StartsWith("displays.", action, StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(target.Type));
        }
    }

    private static DomainEvent Sample(Type type)
    {
        var constructor = type.GetConstructors().OrderByDescending(x => x.GetParameters().Length).First();
        var arguments = constructor.GetParameters().Select(x => Value(x.ParameterType)).ToArray();

        return (DomainEvent)constructor.Invoke(arguments);
    }

    private static object? Value(Type type)
    {
        if (type == typeof(string))
        {
            return "sample";
        }

        if (type == typeof(Guid))
        {
            return Guid.CreateVersion7();
        }

        if (type == typeof(DateTimeOffset))
        {
            return DateTimeOffset.UtcNow;
        }

        if (type.IsEnum)
        {
            return Enum.GetValues(type).GetValue(0);
        }

        if (type.GetConstructor([typeof(Guid)]) is { } idConstructor)
        {
            return idConstructor.Invoke([Guid.CreateVersion7()]);
        }

        // decimal, int and bool: their defaults are valid samples.
        return Activator.CreateInstance(type);
    }
}
