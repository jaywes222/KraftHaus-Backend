namespace Common.Application.Notifications;

/// <summary>What the SMS provider said when it accepted a message.</summary>
/// <param name="ProviderMessageId">The provider's id for the message (delivery reports quote it), when it returns one.</param>
public sealed record SmsReceipt(string? ProviderMessageId)
{
    public static SmsReceipt Accepted { get; } = new((string?)null);
}
