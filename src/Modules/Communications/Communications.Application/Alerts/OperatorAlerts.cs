using System.Globalization;
using Common.Application.Messaging;
using Common.Application.Notifications;
using Common.Application.Operations;
using Common.Application.Tenancy;
using Communications.Application.Abstractions;
using Communications.Application.Messages;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Communications.Application.Alerts;

/// <summary>Operator alert when SMS (OTPs, confirmations) are failing (Spec 08 §14 "SMS failure rate").</summary>
public sealed class SmsFailureSignal(IMessageRepository messages, IOptions<OperationalAlertOptions> options, TimeProvider time) : IOperationalSignal
{
    public async Task<IReadOnlyList<OperationalIssue>> CheckAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var summary = MessageLogService.Summary(time.GetUtcNow().AddHours(-1), await messages.CountSinceAsync(time.GetUtcNow().AddHours(-1), cancellationToken));
        var attempted = summary.Sent + summary.Delivered + summary.Failed;

        return attempted >= settings.SmsMinimumSample && summary.FailureRate >= settings.SmsFailureRate
            ? [new OperationalIssue("messages.failing", string.Create(CultureInfo.InvariantCulture,
                $"{summary.Failed} of {attempted} messages failed in the last hour ({summary.FailureRate:P0}). Check the SMS balance and sender ID."))]
            : [];
    }
}

/// <summary>
///     Runs every operational check for the tenant and texts / e-mails the operators about new problems (Spec 08 §14).
///     A condition is re-sent only after <see cref="OperationalAlertOptions.CooldownMinutes" />. A check that throws is
///     itself reported. With no recipients configured, problems are logged only.
/// </summary>
public sealed partial class OperatorAlertsJob(
    IEnumerable<IOperationalSignal> signals,
    ICurrentTenant tenant,
    IMemoryCache sent,
    ISmsGateway sms,
    IEmailGateway email,
    IOptions<OperationalAlertOptions> options,
    ILogger<OperatorAlertsJob> logger)
    : ITenantJob
{
    public const string Name = "communications.operator-alerts";
    public const string Prefix = "KRAFTHAUS alert";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var issues = new List<OperationalIssue>();

        foreach (var signal in signals)
        {
            try
            {
                issues.AddRange(await signal.CheckAsync(cancellationToken));
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                issues.Add(new OperationalIssue($"check.{signal.GetType().Name}", $"A health check ({signal.GetType().Name}) could not run: {ex.GetType().Name}."));
            }
        }

        var tenantKey = tenant.IsResolved ? tenant.TenantId.Value.ToString("N") : "none";
        var fresh = issues
            .Where(x => !sent.TryGetValue($"alerts:{tenantKey}:{x.Key}", out _))
            .ToList();

        foreach (var issue in issues)
        {
            LogIssue(logger, issue.Key, issue.Summary);
        }

        if (fresh.Count == 0)
        {
            return;
        }

        var text = $"{Prefix}: {string.Join(" | ", fresh.Select(x => x.Summary))}";
        if (text.Length > 459)
        {
            text = text[..456] + "...";
        }

        var delivered = false;
        foreach (var phone in settings.Phones)
        {
            delivered |= await TryAsync(() => sms.SendAsync(new SmsMessage(phone, text), cancellationToken));
        }

        foreach (var address in settings.Emails)
        {
            delivered |= await TryAsync(() => email.SendAsync(
                new EmailMessage(address, $"{Prefix}: {fresh.Count} issue(s)", string.Join(Environment.NewLine, fresh.Select(x => $"- {x.Summary}"))),
                cancellationToken));
        }

        if (settings.Phones.Count + settings.Emails.Count == 0)
        {
            LogNoRecipients(logger);
            delivered = true;
        }

        if (!delivered)
        {
            // Nobody could be reached; try again next run rather than waiting for the cooldown.
            return;
        }

        foreach (var issue in fresh)
        {
            sent.Set($"alerts:{tenantKey}:{issue.Key}", true, TimeSpan.FromMinutes(settings.CooldownMinutes));
        }
    }

    private async Task<bool> TryAsync(Func<Task> send)
    {
        try
        {
            await send();
            return true;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            LogSendFailed(logger, ex.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Operational issue {IssueKey}: {Summary}")]
    private static partial void LogIssue(ILogger logger, string issueKey, string summary);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Operator alerts have no recipients (set Alerts:Phones / Alerts:Emails).")]
    private static partial void LogNoRecipients(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "An operator alert could not be sent ({ErrorType}).")]
    private static partial void LogSendFailed(ILogger logger, string errorType);
}
