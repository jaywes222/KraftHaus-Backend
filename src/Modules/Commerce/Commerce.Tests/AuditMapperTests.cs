using Commerce.Domain.Common;
using Commerce.Domain.Krafts;
using Commerce.Domain.Menus;
using Commerce.Domain.Orders;
using Commerce.Infrastructure.Auditing;
using Common.Domain.Events;
using Xunit;

namespace Commerce.Tests;

public sealed class AuditMapperTests
{
    /// <summary>Every concrete Commerce domain event must have a stable audit action (the mapper throws otherwise).</summary>
    [Fact]
    public void EveryCommerceDomainEventHasAnAuditAction()
    {
        var eventTypes = typeof(Kraft).Assembly.GetTypes()
            .Where(x => x is { IsAbstract: false, IsClass: true } && typeof(DomainEvent).IsAssignableFrom(x))
            .ToList();

        Assert.NotEmpty(eventTypes);

        var kraft = Kraft.Create(new KraftId(Guid.CreateVersion7()), "Mama Pima", KraftDiscipline.Food, null, "mama-pima").Value!;

        foreach (var type in eventTypes)
        {
            var instance = Sample(type);
            var (action, target, _) = CommerceAuditMapper.Map(instance, kraft);

            Assert.StartsWith("commerce.", action, StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(target.Type));
        }
    }

    [Fact]
    public void OrderEventsCarryNoPhoneOrName()
    {
        var kraft = Kraft.Create(new KraftId(Guid.CreateVersion7()), "Mama Pima", KraftDiscipline.Food, null, "mama-pima").Value!;
        var placed = new KraftOrderPlaced(new KraftOrderId(Guid.CreateVersion7()), Guid.CreateVersion7(), Guid.CreateVersion7(), KraftOrderKind.Live, 1100m, 3);

        var (action, _, metadata) = CommerceAuditMapper.Map(placed, kraft);

        Assert.Equal("commerce.order.placed", action);
        Assert.Equal("1100", metadata["total"]);
        Assert.DoesNotContain(metadata.Values, x => x.Contains("+254", StringComparison.Ordinal));
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
