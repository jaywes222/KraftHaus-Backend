using Common.Application.Customers;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Ticketing.Application.Abstractions;
using Ticketing.Domain.Common;
using Ticketing.Domain.Tickets;
using Common.Domain.Phones;

namespace Ticketing.Application.Tickets;

/// <summary>The signed-in customer's tickets: everything held by their account's phone number (BR-010: no QR while awaiting balance; none before the event day).</summary>
public sealed class MyTicketsService(ICustomerDirectory customers, ITicketRepository tickets, TicketQrCodec qr, TimeProvider time)
{
    public async Task<Result<IReadOnlyList<MyTicketView>>> ListAsync(ApplicationUserId userId, CancellationToken cancellationToken)
    {
        var profile = await customers.FindAsync(userId, cancellationToken);

        if (profile?.Phone is null || !KenyanMobile.TryCreate(profile.Phone, out var phone))
        {
            return Result<IReadOnlyList<MyTicketView>>.Success([]);
        }

        var list = await tickets.ListForHolderAsync(phone!.Value, cancellationToken);
        var now = time.GetUtcNow();

        return Result<IReadOnlyList<MyTicketView>>.Success([.. list.Select(x => MyTicketView.For(x, qr, now))]);
    }
}
