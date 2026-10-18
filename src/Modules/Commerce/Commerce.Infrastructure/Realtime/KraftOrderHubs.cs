using System.Globalization;
using Commerce.Application.Boards;
using Commerce.Application.Orders;
using Commerce.Domain.Orders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Commerce.Infrastructure.Realtime;

/// <summary>
///     Kitchen board live updates (Spec 08 §10, UF-015): <c>/hubs/krafts/{kraftId}/kitchen</c>. The route carries the Kraft,
///     so the endpoint's <c>kitchen:view</c> check is Kraft-scoped like every other Kraft route (BR-026); a connection joins
///     its Kraft's group only. Server → client message: <c>orderChanged</c> with a kitchen card.
/// </summary>
public sealed class KitchenHub : Hub
{
    public const string Pattern = "/hubs/krafts/{kraftId:guid}/kitchen";

    public static string Group(Guid kraftId) => $"kraft:{kraftId:D}";

    public override async Task OnConnectedAsync()
    {
        var raw = Context.GetHttpContext()?.Request.RouteValues["kraftId"]?.ToString();

        if (!Guid.TryParse(raw, out var kraftId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(kraftId));
        await base.OnConnectedAsync();
    }
}

/// <summary>
///     The signed-in customer's own order updates: <c>/hubs/orders</c>. Each connection joins only its own user group.
///     Server → client message: <c>orderChanged</c> with the order id, reference and status.
/// </summary>
public sealed class CustomerOrdersHub : Hub
{
    public const string Pattern = "/hubs/orders";

    public static string Group(Guid userId) => $"user:{userId:D}";

    public override async Task OnConnectedAsync()
    {
        if (!Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var userId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(userId));
        await base.OnConnectedAsync();
    }
}

internal sealed partial class SignalRKraftOrderRealtime(
    IHubContext<KitchenHub> kitchen,
    IHubContext<CustomerOrdersHub> customers,
    DisplaySignals displays,
    TimeProvider time,
    ILogger<SignalRKraftOrderRealtime> logger)
    : IKraftOrderRealtime
{
    public const string OrderChanged = "orderChanged";

    public async Task OrderChangedAsync(KraftOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        try
        {
            if (order.IsPaid)
            {
                await kitchen.Clients.Group(KitchenHub.Group(order.KraftId))
                    .SendAsync(OrderChanged, KitchenOrderView.From(order, time.GetUtcNow()), cancellationToken);
            }

            await customers.Clients.Group(CustomerOrdersHub.Group(order.CustomerUserId))
                .SendAsync(
                    OrderChanged,
                    new
                    {
                        id = order.Id.Value,
                        checkoutGroupId = order.CheckoutGroupId,
                        reference = order.Reference,
                        kraftName = order.KraftName,
                        status = order.Status.ToString(),
                        readyAt = order.ReadyAt?.ToString("O", CultureInfo.InvariantCulture)
                    },
                    cancellationToken);
        }
#pragma warning disable CA1031 // Best effort: clients poll and re-fetch on reconnect.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPushFailed(logger, order.Id.Value, ex);
        }

        if (order.IsPaid)
        {
            // Order-ready screens and sold-out flags on menu screens.
            await displays.OrdersChangedAsync(order.EventId, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Real-time push for Kraft order {OrderId} failed.")]
    private static partial void LogPushFailed(ILogger logger, Guid orderId, Exception exception);
}
