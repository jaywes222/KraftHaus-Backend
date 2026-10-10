using Common.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace Common.Infrastructure.Notifications.Sms;

internal sealed partial class DummySmsGateway(
    ILogger<DummySmsGateway> logger)
    : ISmsGateway
{
    public Task SendAsync(
        SmsMessage message,
        CancellationToken cancellationToken)
    {
        LogDummySms(
            logger,
            message.PhoneNumber);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Dummy SMS gateway accepted message for {PhoneNumber}.")]
    private static partial void LogDummySms(
        ILogger logger,
        string phoneNumber);
}
