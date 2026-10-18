namespace Commerce.Infrastructure.Stock;

/// <summary>Persistence-only row: one stock counter. Written with atomic SQL by <see cref="StockLedger" />, never through EF change tracking.</summary>
internal sealed class StockBucketRow
{
    public const string TableName = "stock_bucket";

    public Guid Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public Guid MenuItemId { get; set; }

    public Guid EventId { get; set; }

    public Guid KraftId { get; set; }

    /// <summary>Null: no limit.</summary>
    public int? Total { get; set; }

    public int Held { get; set; }

    public int Sold { get; set; }
}

/// <summary>Persistence-only row: what one order holds in one bucket (Held → Converted | Released).</summary>
internal sealed class StockHoldRow
{
    public const string TableName = "stock_hold";

    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public Guid MenuItemId { get; set; }

    public int Quantity { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public string Status { get; set; } = string.Empty;
}
