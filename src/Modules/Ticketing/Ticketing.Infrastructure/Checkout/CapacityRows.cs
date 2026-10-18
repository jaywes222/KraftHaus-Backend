namespace Ticketing.Infrastructure.Checkout;

/// <summary>Persistence-only row: one capacity counter. Written with atomic SQL by <see cref="CapacityLedger" />, never through EF change tracking.</summary>
internal sealed class CapacityBucketRow
{
    public const string TableName = "capacity_bucket";

    public Guid Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public Guid RefId { get; set; }

    public Guid EventId { get; set; }

    public int Total { get; set; }

    public int Held { get; set; }

    public int Sold { get; set; }
}

/// <summary>Persistence-only row: what one order holds in one bucket (Held → Converted | Released).</summary>
internal sealed class CapacityHoldRow
{
    public const string TableName = "capacity_hold";

    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public Guid RefId { get; set; }

    public int Quantity { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public string Status { get; set; } = string.Empty;
}
