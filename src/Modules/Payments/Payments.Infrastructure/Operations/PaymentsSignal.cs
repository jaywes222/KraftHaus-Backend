using System.Globalization;
using Common.Application.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payments.Domain.Payments;
using Payments.Infrastructure.Persistence;

namespace Payments.Infrastructure.Operations;

/// <summary>
///     Operator alerts for money (Spec 08 §14): payments still waiting on M-Pesa long after the prompt (callbacks not
///     arriving, the status job not running) and payments Finance must review (amount mismatch).
/// </summary>
internal sealed class PaymentsSignal(PaymentsDbContext db, IOptions<OperationalAlertOptions> options, TimeProvider time) : IOperationalSignal
{
    public async Task<IReadOnlyList<OperationalIssue>> CheckAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow().AddMinutes(-options.Value.StuckPaymentMinutes);

        var stuck = await db.Payments.AsNoTracking()
            .CountAsync(x => (x.Status == PaymentStatus.Pending || x.Status == PaymentStatus.Initiating) && x.CreatedAt < cutoff, cancellationToken);
        var review = await db.Payments.AsNoTracking()
            .CountAsync(x => x.Status == PaymentStatus.NeedsReview, cancellationToken);

        var issues = new List<OperationalIssue>();
        if (stuck > 0)
        {
            issues.Add(new OperationalIssue("payments.stuck", string.Create(CultureInfo.InvariantCulture,
                $"{stuck} M-Pesa payment(s) pending for over {options.Value.StuckPaymentMinutes} min.")));
        }

        if (review > 0)
        {
            issues.Add(new OperationalIssue("payments.review", string.Create(CultureInfo.InvariantCulture,
                $"{review} payment(s) need Finance review (amount mismatch).")));
        }

        return issues;
    }
}
