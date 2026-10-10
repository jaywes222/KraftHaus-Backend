using System.Net.Mail;

namespace Common.Application.Notifications;

public interface IEmailGateway
{
    Task SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken);
}
