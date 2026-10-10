namespace Common.Application.Notifications;

public interface ISmsGateway
{
    Task SendAsync(
        SmsMessage message,
        CancellationToken cancellationToken);
}
