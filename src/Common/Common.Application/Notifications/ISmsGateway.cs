namespace Common.Application.Notifications;

public interface ISmsGateway
{
    Task SendAsync(
        SmsMessage message,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Sends and returns the provider's message id when it gives one (Communications stores it so delivery reports
    ///     can be matched, Spec 08 §9.5). Gateways that cannot report an id fall back to <see cref="SendAsync" />.
    ///     Throws like <see cref="SendAsync" /> when the provider does not accept the message.
    /// </summary>
    async Task<SmsReceipt> SendWithReceiptAsync(
        SmsMessage message,
        CancellationToken cancellationToken)
    {
        await SendAsync(message, cancellationToken);
        return SmsReceipt.Accepted;
    }
}
