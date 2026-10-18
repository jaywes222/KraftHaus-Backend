using System.Text.Json;
using Common.Application.Auth;
using Common.Application.EndpointFilters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Payments.Application.Destinations;
using Payments.Application.Payments;
using Payments.Domain.Common;
using Payments.Domain.Destinations;
using Payments.Domain.Payments;

namespace Payments.Infrastructure.Endpoints;

internal static class PaymentEndpoints
{
    private const int MaxCallbackBytes = 64 * 1024;

    public static void MapPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // PalPluss callback: per-payment token in the path, body only used to learn the transaction id.
        endpoints.MapPost("/webhooks/palpluss/{token}", HandleCallbackAsync)
            .WithTags("Webhooks")
            .AllowAnonymous()
            .ExcludeFromDescription();

        var admin = endpoints.MapGroup("/api/admin/payments").WithTags("Payments (admin)");

        admin.MapGet("/", (PaymentStatus? status, Guid? referenceId, DateTimeOffset? before, int? limit,
                    PaymentAdminService service, CancellationToken ct) =>
                service.ListAsync(status, referenceId, before, limit, ct))
            .RequirePermission(PaymentsPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<PaymentAdminView>>>();

        admin.MapGet("/summary", (DateTimeOffset? from, DateTimeOffset? to, PaymentAdminService service, CancellationToken ct) =>
                service.SummaryAsync(from, to, ct))
            .RequirePermission(PaymentsPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<PaymentSummaryView>>();

        admin.MapPost("/{paymentId:guid}/review", (Guid paymentId, ReviewPaymentRequest request, ICurrentUser user,
                    PaymentAdminService service, CancellationToken ct) =>
                service.ReviewAsync(new PaymentId(paymentId), request, user.Id, ct))
            .RequirePermission(PaymentsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<PaymentAdminView>>();

        admin.MapPut("/{paymentId:guid}/settlement", (Guid paymentId, RecordSettlementRequest request, ICurrentUser user,
                    PaymentAdminService service, CancellationToken ct) =>
                service.RecordSettlementAsync(new PaymentId(paymentId), request, user.Id, ct))
            .RequirePermission(PaymentsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<PaymentAdminView>>();

        admin.MapGet("/{paymentId:guid}", (Guid paymentId, PaymentAdminService service, CancellationToken ct) =>
                service.GetAsync(new PaymentId(paymentId), ct))
            .RequirePermission(PaymentsPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<PaymentAdminView>>();

        admin.MapPost("/{paymentId:guid}/check", (Guid paymentId, PaymentAdminService service, CancellationToken ct) =>
                service.CheckNowAsync(new PaymentId(paymentId), ct))
            .RequirePermission(PaymentsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<PaymentAdminView>>();

        MapTillEndpoints(endpoints);
    }

    /// <summary>
    ///     Kraft tills (UF-017). Kraft-scoped routes (<c>{kraftId}</c>) let a Kraft Owner submit and see its own tills;
    ///     tenant-scoped routes are Finance's queue and decisions, which Kraft roles never reach (BR-026).
    /// </summary>
    private static void MapTillEndpoints(IEndpointRouteBuilder endpoints)
    {
        var kraft = endpoints.MapGroup("/api/admin/krafts/{kraftId:guid}/tills").WithTags("Tills (admin)");

        kraft.MapPost("/", (Guid kraftId, SubmitTillRequest request, ICurrentUser user, TillService service, CancellationToken ct) =>
                service.SubmitAsync(kraftId, request, user.Id, ct))
            .RequirePermission(PaymentsPermission.TillApprove)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<TillView>>();

        kraft.MapGet("/", (Guid kraftId, TillService service, CancellationToken ct) =>
                service.ListForKraftAsync(kraftId, ct))
            .RequirePermission(PaymentsPermission.TillView)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<TillView>>>();

        // The KRAFTHAUS till (tenant roles only: a Kraft role never reaches a route without its kraftId).
        var platform = endpoints.MapGroup("/api/admin/platform-till").WithTags("Tills (admin)");

        platform.MapGet("/", (TillService service, CancellationToken ct) => service.ListPlatformAsync(ct))
            .RequirePermission(PaymentsPermission.TillApprove)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<TillView>>>();

        platform.MapPost("/", (SubmitPlatformTillRequest request, ICurrentUser user, TillService service, CancellationToken ct) =>
                service.SubmitPlatformAsync(request, user.Id, ct))
            .RequirePermission(PaymentsPermission.TillApprove)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<TillView>>();

        var tills = endpoints.MapGroup("/api/admin/tills").WithTags("Tills (admin)");

        tills.MapGet("/", (DestinationStatus? status, Guid? eventId, int? limit, TillService service, CancellationToken ct) =>
                service.ListAsync(status, eventId, limit, ct))
            .RequirePermission(PaymentsPermission.TillApprove)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<TillView>>>();

        tills.MapGet("/{tillId:guid}", (Guid tillId, TillService service, CancellationToken ct) =>
                service.GetAsync(new PaymentDestinationId(tillId), ct))
            .RequirePermission(PaymentsPermission.TillApprove)
            .AddEndpointFilter<ResultToResponseTransformer<TillView>>();

        tills.MapPost("/{tillId:guid}/test", (Guid tillId, TestTillRequest request, HttpContext http, TillService service, CancellationToken ct) =>
                service.TestAsync(new PaymentDestinationId(tillId), request, http.Request.Host.Host, ct))
            .RequirePermission(PaymentsPermission.TillApprove)
            .AddEndpointFilter<ResultToResponseTransformer<TillView>>();

        tills.MapPost("/{tillId:guid}/approve", (Guid tillId, ICurrentUser user, TillService service, CancellationToken ct) =>
                service.ApproveAsync(new PaymentDestinationId(tillId), user.Id, ct))
            .RequirePermission(PaymentsPermission.TillApprove)
            .AddEndpointFilter<ResultToResponseTransformer<TillView>>();

        tills.MapPost("/{tillId:guid}/reject", (Guid tillId, TillDecisionRequest request, ICurrentUser user, TillService service, CancellationToken ct) =>
                service.RejectAsync(new PaymentDestinationId(tillId), request, user.Id, ct))
            .RequirePermission(PaymentsPermission.TillApprove)
            .AddEndpointFilter<ResultToResponseTransformer<TillView>>();

        tills.MapPost("/{tillId:guid}/disable", (Guid tillId, TillDecisionRequest request, ICurrentUser user, TillService service, CancellationToken ct) =>
                service.DisableAsync(new PaymentDestinationId(tillId), request, user.Id, ct))
            .RequirePermission(PaymentsPermission.TillApprove)
            .AddEndpointFilter<ResultToResponseTransformer<TillView>>();
    }

    private static async Task<IResult> HandleCallbackAsync(
        string token,
        HttpRequest request,
        PaymentCallbackService service,
        CancellationToken cancellationToken)
    {
        var hints = await ReadHintsAsync(request, cancellationToken);
        var result = await service.HandleAsync(token, hints, cancellationToken);

        return result.IsFailure
            ? Results.StatusCode((int)result.Error!.StatusCode)
            : Results.Ok();
    }

    /// <summary>
    ///     Reads at most <see cref="MaxCallbackBytes" /> of the (unsigned) body and pulls out the transaction id and the
    ///     M-Pesa receipt. Anything larger, or malformed, is ignored: the token alone identifies the payment.
    /// </summary>
    private static async Task<CallbackHints> ReadHintsAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxCallbackBytes)
        {
            return CallbackHints.None;
        }

        var buffer = new byte[MaxCallbackBytes + 1];
        var read = 0;
        int chunk;
        while (read < buffer.Length &&
               (chunk = await request.Body.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken)) > 0)
        {
            read += chunk;
        }

        if (read is 0 or > MaxCallbackBytes)
        {
            return CallbackHints.None;
        }

        try
        {
            using var document = JsonDocument.Parse(buffer.AsMemory(0, read));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("transaction", out var transaction) ||
                transaction.ValueKind != JsonValueKind.Object)
            {
                return CallbackHints.None;
            }

            return new CallbackHints(Text(transaction, "id"), Text(transaction, "mpesa_receipt") ?? Text(transaction, "mpesaReceipt"));
        }
        catch (JsonException)
        {
            return CallbackHints.None;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
