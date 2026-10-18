using Commerce.Application.Orders;
using Commerce.Application.Settlements;
using Commerce.Domain.Common;
using Commerce.Domain.Orders;
using Commerce.Infrastructure.RateLimiting;
using Commerce.Infrastructure.Realtime;
using Common.Application.Auth;
using Common.Application.EndpointFilters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Commerce.Infrastructure.Endpoints;

/// <summary>Food and drink orders: customer checkout and payment, the kitchen board and pickup, Finance's view.</summary>
internal static class OrderEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";

    public static void MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapClient(endpoints);
        MapKitchen(endpoints);
        MapAdmin(endpoints);

        endpoints.MapHub<KitchenHub>(KitchenHub.Pattern).RequirePermission(CommercePermission.KitchenView);
        endpoints.MapHub<CustomerOrdersHub>(CustomerOrdersHub.Pattern).RequireAuthorization();
    }

    /// <summary>The customer is always the token's subject; no endpoint accepts a customer id.</summary>
    private static void MapClient(IEndpointRouteBuilder endpoints)
    {
        var checkouts = endpoints.MapGroup("/api/client/kraft-checkouts").WithTags("Food orders (client)").RequireAuthorization();

        checkouts.MapPost("/", (
                    [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
                    CheckoutRequest request,
                    ICurrentUser user,
                    KraftCheckoutService service,
                    CancellationToken ct) =>
                service.CheckoutAsync(user.Id, idempotencyKey, request, ct))
            .RequireRateLimiting(CommerceRateLimitingPolicies.FoodCheckout)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<CheckoutView>>();

        checkouts.MapGet("/{checkoutGroupId:guid}", (Guid checkoutGroupId, ICurrentUser user, KraftCheckoutService service, CancellationToken ct) =>
                service.GetCheckoutAsync(user.Id, checkoutGroupId, ct))
            .AddEndpointFilter<ResultToResponseTransformer<CheckoutView>>();

        var orders = endpoints.MapGroup("/api/client/kraft-orders").WithTags("Food orders (client)").RequireAuthorization();

        orders.MapGet("/", (ICurrentUser user, KraftCheckoutService service, CancellationToken ct) =>
                service.ListMineAsync(user.Id, ct))
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<KraftOrderView>>>();

        orders.MapGet("/{orderId:guid}", (Guid orderId, ICurrentUser user, KraftCheckoutService service, CancellationToken ct) =>
                service.GetAsync(user.Id, new KraftOrderId(orderId), ct))
            .AddEndpointFilter<ResultToResponseTransformer<KraftOrderView>>();

        orders.MapPost("/{orderId:guid}/pay", (Guid orderId, PayOrderRequest request, HttpContext http, ICurrentUser user,
                    KraftCheckoutService service, CancellationToken ct) =>
                service.PayAsync(user.Id, new KraftOrderId(orderId), request, http.Request.Host.Host, ct))
            .RequireRateLimiting(CommerceRateLimitingPolicies.FoodCheckout)
            .AddEndpointFilter<ResultToResponseTransformer<KraftOrderView>>();
    }

    /// <summary>Event Ops: the Kraft's own board (UF-015) and pickup (UF-014). Kraft-scoped routes (BR-026).</summary>
    private static void MapKitchen(IEndpointRouteBuilder endpoints)
    {
        var kitchen = endpoints.MapGroup("/api/ops/krafts/{kraftId:guid}/orders").WithTags("Kitchen (ops)");

        kitchen.MapGet("/", (Guid kraftId, Guid? eventId, KitchenService service, CancellationToken ct) =>
                service.BoardAsync(new KraftId(kraftId), eventId, ct))
            .RequirePermission(CommercePermission.KitchenView)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<KitchenOrderView>>>();

        kitchen.MapPost("/{orderId:guid}/accept", (Guid kraftId, Guid orderId, KitchenService service, CancellationToken ct) =>
                service.AcceptAsync(new KraftId(kraftId), new KraftOrderId(orderId), ct))
            .RequirePermission(CommercePermission.KitchenHandle)
            .AddEndpointFilter<ResultToResponseTransformer<KitchenOrderView>>();

        kitchen.MapPost("/{orderId:guid}/ready", (Guid kraftId, Guid orderId, KitchenService service, CancellationToken ct) =>
                service.ReadyAsync(new KraftId(kraftId), new KraftOrderId(orderId), ct))
            .RequirePermission(CommercePermission.KitchenHandle)
            .AddEndpointFilter<ResultToResponseTransformer<KitchenOrderView>>();

        kitchen.MapPost("/{orderId:guid}/collect", (Guid kraftId, Guid orderId, ICurrentUser user, KitchenService service, CancellationToken ct) =>
                service.CollectAsync(new KraftId(kraftId), new KraftOrderId(orderId), user.Id, ct))
            .RequirePermission(CommercePermission.KitchenHandle)
            .AddEndpointFilter<ResultToResponseTransformer<KitchenOrderView>>();

        kitchen.MapPost("/collect", (Guid kraftId, CollectByCodeRequest request, ICurrentUser user, KitchenService service, CancellationToken ct) =>
                service.CollectByCodeAsync(new KraftId(kraftId), request, user.Id, ct))
            .RequirePermission(CommercePermission.KitchenHandle)
            .RequireRateLimiting(CommerceRateLimitingPolicies.PickupScan)
            .AddEndpointFilter<ResultToResponseTransformer<KitchenOrderView>>();

        kitchen.MapPost("/{orderId:guid}/lines/{lineId:guid}/unavailable", (Guid kraftId, Guid orderId, Guid lineId,
                    LineUnavailableRequest request, KitchenService service, CancellationToken ct) =>
                service.LineUnavailableAsync(new KraftId(kraftId), new KraftOrderId(orderId), new KraftOrderLineId(lineId), request, ct))
            .RequirePermission(CommercePermission.KitchenHandle)
            .AddEndpointFilter<ResultToResponseTransformer<KitchenOrderView>>();
    }

    /// <summary>Money view (fee split): Tenant Admin and Finance for all Krafts, a Kraft Owner for its own.</summary>
    private static void MapAdmin(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin").WithTags("Food orders (admin)");

        admin.MapGet("/kraft-orders", (Guid? eventId, Guid? kraftId, KraftOrderStatus? status, bool? needsAction, DateTimeOffset? before,
                    int? limit, KraftOrderAdminService service, CancellationToken ct) =>
                service.ListAsync(eventId, kraftId, status, needsAction, before, limit, ct))
            .RequirePermission(CommercePermission.ViewMoney)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<KraftOrderAdminView>>>();

        admin.MapGet("/kraft-orders/{orderId:guid}", (Guid orderId, KraftOrderAdminService service, CancellationToken ct) =>
                service.GetAsync(new KraftOrderId(orderId), ct))
            .RequirePermission(CommercePermission.ViewMoney)
            .AddEndpointFilter<ResultToResponseTransformer<KraftOrderAdminView>>();

        admin.MapPost("/kraft-orders/{orderId:guid}/resolve", (Guid orderId, ResolveKraftOrderRequest request, ICurrentUser user,
                    KraftOrderAdminService service, CancellationToken ct) =>
                service.ResolveAsync(new KraftOrderId(orderId), request, user.Id, ct))
            .RequirePermission(CommercePermission.ResolveOrders)
            .AddEndpointFilter<ResultToResponseTransformer<KraftOrderAdminView>>();

        admin.MapGet("/events/{eventId:guid}/kraft-sales", (Guid eventId, KraftOrderAdminService service, CancellationToken ct) =>
                service.SalesAsync(eventId, null, ct))
            .RequirePermission(CommercePermission.ViewMoney)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<KraftSalesView>>>();

        // Kraft-scoped: a Kraft Owner sees its own orders and sales only.
        admin.MapGet("/krafts/{kraftId:guid}/orders", (Guid kraftId, Guid? eventId, KraftOrderStatus? status, DateTimeOffset? before, int? limit,
                    KraftOrderAdminService service, CancellationToken ct) =>
                service.ListAsync(eventId, kraftId, status, null, before, limit, ct))
            .RequirePermission(CommercePermission.ViewMoney)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<KraftOrderAdminView>>>();

        admin.MapGet("/events/{eventId:guid}/statements", (Guid eventId, SettlementService service, CancellationToken ct) =>
                service.StatementsAsync(eventId, null, ct))
            .RequirePermission(CommercePermission.ViewMoney)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<KraftStatementView>>>();

        // Kraft-scoped: a Kraft Owner sees its own statement.
        admin.MapGet("/krafts/{kraftId:guid}/statement", (Guid kraftId, Guid eventId, SettlementService service, CancellationToken ct) =>
                service.StatementsAsync(eventId, kraftId, ct))
            .RequirePermission(CommercePermission.ViewMoney)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<KraftStatementView>>>();

        admin.MapPost("/krafts/{kraftId:guid}/settlements", (Guid kraftId, RecordSettlementRequest request, ICurrentUser user,
                    SettlementService service, CancellationToken ct) =>
                service.RecordAsync(new KraftId(kraftId), request, user.Id, ct))
            .RequirePermission(CommercePermission.ResolveOrders)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<KraftStatementView>>();

        admin.MapGet("/krafts/{kraftId:guid}/sales", (Guid kraftId, Guid eventId, KraftOrderAdminService service, CancellationToken ct) =>
                service.SalesAsync(eventId, kraftId, ct))
            .RequirePermission(CommercePermission.ViewMoney)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<KraftSalesView>>>();
    }
}
