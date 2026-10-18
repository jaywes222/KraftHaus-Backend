using Common.Application.Auth;
using Common.Application.EndpointFilters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Ticketing.Application.Orders;
using Ticketing.Application.Tickets;
using Ticketing.Domain.Common;
using Ticketing.Infrastructure.RateLimiting;

namespace Ticketing.Infrastructure.Endpoints;

/// <summary>
///     Signed-in customer surface (UF-003/005/006): place an order, (re)send the M-Pesa prompt (also for a Lipa Mdogo
///     Mdogo balance), follow the order, list, transfer and name my tickets. The customer is always the token's subject; no endpoint accepts a customer id.
/// </summary>
internal static class ClientOrderEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";

    public static void MapClientOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var orders = endpoints.MapGroup("/api/client/orders").WithTags("Orders (client)").RequireAuthorization();

        orders.MapPost("/", (
                    [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
                    PlaceOrderRequest request,
                    HttpContext http,
                    ICurrentUser user,
                    CheckoutService service,
                    CancellationToken ct) =>
                service.PlaceOrderAsync(user.Id, idempotencyKey, request, http.Request.Host.Host, ct))
            .RequireRateLimiting(TicketingRateLimitingPolicies.Checkout)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<OrderView>>();

        orders.MapGet("/", (ICurrentUser user, CheckoutService service, CancellationToken ct) =>
                service.ListMineAsync(user.Id, ct))
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<OrderView>>>();

        orders.MapGet("/{orderId:guid}", (Guid orderId, ICurrentUser user, CheckoutService service, CancellationToken ct) =>
                service.GetAsync(user.Id, new OrderId(orderId), ct))
            .AddEndpointFilter<ResultToResponseTransformer<OrderView>>();

        orders.MapPost("/{orderId:guid}/payments", (
                    Guid orderId,
                    RetryPaymentRequest request,
                    HttpContext http,
                    ICurrentUser user,
                    CheckoutService service,
                    CancellationToken ct) =>
                service.RetryPaymentAsync(user.Id, new OrderId(orderId), request, http.Request.Host.Host, ct))
            .RequireRateLimiting(TicketingRateLimitingPolicies.Checkout)
            .AddEndpointFilter<ResultToResponseTransformer<OrderView>>();

        var tickets = endpoints.MapGroup("/api/client/tickets").WithTags("Tickets (client)").RequireAuthorization();

        tickets.MapGet("/", (ICurrentUser user, MyTicketsService service, CancellationToken ct) =>
                service.ListAsync(user.Id, ct))
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<MyTicketView>>>();

        tickets.MapPost("/{ticketId:guid}/transfer", (Guid ticketId, TransferTicketRequest request, ICurrentUser user,
                    TicketHolderService service, CancellationToken ct) =>
                service.TransferAsync(user.Id, new TicketId(ticketId), request, ct))
            .RequireRateLimiting(TicketingRateLimitingPolicies.Checkout)
            .AddEndpointFilter<ResultToResponseTransformer<TransferResultView>>();

        tickets.MapPut("/{ticketId:guid}/attendee", (Guid ticketId, AssignAttendeeRequest request, ICurrentUser user,
                    TicketHolderService service, CancellationToken ct) =>
                service.AssignAttendeeAsync(user.Id, new TicketId(ticketId), request, ct))
            .AddEndpointFilter<ResultToResponseTransformer<MyTicketView>>();
    }
}
