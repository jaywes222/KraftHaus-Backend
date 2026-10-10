using Common.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace Common.Infrastructure.Notifications.Email;

internal sealed partial class DummyEmailGateway(
    ILogger<DummyEmailGateway> logger)
    : IEmailGateway
{
    public Task SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken)
    {
        LogDummyEmail(
            logger,
            message.To,
            message.Subject);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Dummy email gateway accepted message for {Email}. Subject: {Subject}")]
    private static partial void LogDummyEmail(
        ILogger logger,
        string email,
        string subject);
}
