using Common.Application.Auth;
using Common.Application.EndpointFilters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ticketing.Application.Orders;
using Ticketing.Domain.Common;
using Ticketing.Domain.Orders;
using Ticketing.Domain.Tickets;

namespace Ticketing.Infrastructure.Endpoints;

/// <summary>Admin / Finance surface: orders, sales counters and the waitlist of an event.</summary>
internal static class AdminTicketingEndpoints
{
    public static void MapAdminTicketingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin").WithTags("Ticketing (admin)");

        admin.MapGet("/events/{eventId:guid}/orders", (Guid eventId, OrderStatus? status, bool? needsAction, DateTimeOffset? before,
                    int? limit, OrderAdminService service, CancellationToken ct) =>
                service.ListAsync(eventId, status, needsAction, before, limit, ct))
            .RequirePermission(TicketingPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<OrderAdminView>>>();

        admin.MapGet("/orders/{orderId:guid}", (Guid orderId, OrderAdminService service, CancellationToken ct) =>
                service.GetAsync(new OrderId(orderId), ct))
            .RequirePermission(TicketingPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<OrderAdminView>>();

        admin.MapGet("/orders/{orderId:guid}/tickets", (Guid orderId, OrderAdminService service, CancellationToken ct) =>
                service.TicketsAsync(new OrderId(orderId), ct))
            .RequirePermission(TicketingPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<AdminTicketView>>>();

        admin.MapPost("/orders/{orderId:guid}/resolve", (Guid orderId, ResolveOrderRequest request, ICurrentUser user,
                    OrderAdminService service, CancellationToken ct) =>
                service.ResolveAsync(new OrderId(orderId), request, user.Id, ct))
            .RequirePermission(TicketingPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<OrderAdminView>>();

        admin.MapPost("/tickets/{ticketId:guid}/void", (Guid ticketId, VoidTicketRequest request,
                    OrderAdminService service, CancellationToken ct) =>
                service.VoidTicketAsync(new TicketId(ticketId), request, ct))
            .RequirePermission(TicketingPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<AdminTicketView>>();

        admin.MapGet("/events/{eventId:guid}/tickets", (Guid eventId, Guid? dateId, TicketStatus? status, string? q,
                    DateTimeOffset? before, int? limit, OrderAdminService service, CancellationToken ct) =>
                service.TicketsForEventAsync(eventId, dateId, status, q, before, limit, ct))
            .RequirePermission(TicketingPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<AdminTicketView>>>();

        admin.MapGet("/events/{eventId:guid}/sales", (Guid eventId, OrderAdminService service, CancellationToken ct) =>
                service.SalesAsync(eventId, ct))
            .RequirePermission(TicketingPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<SalesSummaryView>>();

        // Personal data (phones, names, ages): Tenant Admin only (events:manage), not Support.
        admin.MapGet("/events/{eventId:guid}/waitlist", (Guid eventId, DateTimeOffset? joinedAfter, int? limit,
                    OrderAdminService service, CancellationToken ct) =>
                service.WaitlistAsync(eventId, joinedAfter, limit, ct))
            .RequirePermission(TicketingPermission.ExportWaitlist)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<WaitlistAdminView>>>();
    }
}
