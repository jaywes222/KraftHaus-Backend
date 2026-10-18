using Common.Application.Customers;
using Common.Application.EventCatalog;
using Common.Application.Fees;
using Common.Domain.Phones;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions;
using Ticketing.Domain.Common;

namespace Ticketing.Application.Orders;

/// <param name="TicketFeePerTicket">KES per ticket the signed-in caller would pay (0 when waived).</param>
/// <param name="FoodOrderFee">KES per Kraft order the caller would pay (0 when waived).</param>
/// <param name="Waived">The caller's phone is on this event's waitlist, so no platform fee.</param>
/// <param name="StandardTicketFee">The fee before any waiver, to show what was saved.</param>
/// <param name="StandardFoodFee">The food fee before any waiver.</param>
public sealed record FeeQuoteView(
    decimal TicketFeePerTicket,
    decimal FoodOrderFee,
    bool Waived,
    decimal StandardTicketFee,
    decimal StandardFoodFee,
    string Currency);

/// <summary>What platform fee the caller pays on an event, so checkout and the menu can show it before paying.</summary>
public sealed class FeeQuoteService(
    IEventCatalog events,
    ICustomerDirectory customers,
    IWaitlistRepository waitlist,
    IOptions<PlatformFeeOptions> options)
{
    public async Task<Result<FeeQuoteView>> QuoteAsync(string slug, ApplicationUserId? caller, CancellationToken cancellationToken)
    {
        var found = string.IsNullOrWhiteSpace(slug) ? null : await events.FindBySlugAsync(slug.Trim(), cancellationToken);
        if (found is null || found.Status == EventLifecycle.Draft)
        {
            return TicketingErrors.EventNotFound;
        }

        var fees = options.Value;
        var waived = false;

        if (fees.WaiveForWaitlist && caller is { } userId &&
            await customers.FindAsync(userId, cancellationToken) is { Phone: { } raw } &&
            KenyanMobile.TryCreate(raw, out var phone))
        {
            waived = await waitlist.ExistsAsync(found.EventId, phone!.Value, cancellationToken);
        }

        return new FeeQuoteView(
            waived ? 0m : fees.TicketFeePerTicket,
            waived ? 0m : fees.FoodOrderFee,
            waived,
            fees.TicketFeePerTicket,
            fees.FoodOrderFee,
            "KES");
    }
}
