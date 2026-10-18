using Common.Application.Auth;
using Common.Application.EndpointFilters;
using Communications.Application;
using Communications.Application.Broadcasts;
using Communications.Application.Messages;
using Communications.Application.OptOuts;
using Communications.Application.Templates;
using Communications.Domain.Common;
using Communications.Domain.Messages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Communications.Infrastructure.Endpoints;

/// <summary>Message log, broadcasts, templates, opt-outs, the customer's marketing switch and the SMS delivery-report webhook.</summary>
internal static class CommunicationsEndpoints
{
    public static void MapCommunicationsAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin").WithTags("Communications (admin)");

        admin.MapGet("/messages", (MessageStatus? status, MessageChannel? channel, MessageKind? kind, Guid? broadcastId, string? recipient,
                    DateTimeOffset? before, int? limit, MessageLogService service, CancellationToken ct) =>
                service.ListAsync(status, channel, kind, broadcastId, recipient, before, limit, ct))
            .RequirePermission(CommunicationsPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<MessageView>>>();

        admin.MapGet("/messages/summary", (int? hours, MessageLogService service, CancellationToken ct) =>
                service.SummaryAsync(hours, ct))
            .RequirePermission(CommunicationsPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<MessageSummaryView>>();

        var broadcasts = admin.MapGroup("/broadcasts");

        broadcasts.MapGet("/", (Guid? eventId, BroadcastService service, CancellationToken ct) => service.ListAsync(eventId, ct))
            .RequirePermission(CommunicationsPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<BroadcastView>>>();

        broadcasts.MapGet("/{broadcastId:guid}", (Guid broadcastId, BroadcastService service, CancellationToken ct) => service.GetAsync(broadcastId, ct))
            .RequirePermission(CommunicationsPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<BroadcastView>>();

        broadcasts.MapPost("/", (SaveBroadcastRequest request, BroadcastService service, CancellationToken ct) => service.CreateAsync(request, ct))
            .RequirePermission(CommunicationsPermission.Manage)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<BroadcastView>>();

        broadcasts.MapPut("/{broadcastId:guid}", (Guid broadcastId, SaveBroadcastRequest request, BroadcastService service, CancellationToken ct) =>
                service.UpdateAsync(broadcastId, request, ct))
            .RequirePermission(CommunicationsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<BroadcastView>>();

        broadcasts.MapGet("/{broadcastId:guid}/preview", (Guid broadcastId, BroadcastService service, CancellationToken ct) =>
                service.PreviewAsync(broadcastId, ct))
            .RequirePermission(CommunicationsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<AudiencePreviewView>>();

        broadcasts.MapPost("/{broadcastId:guid}/schedule", (Guid broadcastId, ScheduleBroadcastRequest request, BroadcastService service, CancellationToken ct) =>
                service.ScheduleAsync(broadcastId, request, ct))
            .RequirePermission(CommunicationsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<BroadcastView>>();

        broadcasts.MapPost("/{broadcastId:guid}/cancel", (Guid broadcastId, BroadcastService service, CancellationToken ct) =>
                service.CancelAsync(broadcastId, ct))
            .RequirePermission(CommunicationsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<BroadcastView>>();

        var templates = admin.MapGroup("/message-templates");

        templates.MapGet("/", (MessageTemplateService service, CancellationToken ct) => service.ListAsync(ct))
            .RequirePermission(CommunicationsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<MessageTemplateView>>>();

        templates.MapPost("/", (SaveTemplateRequest request, MessageTemplateService service, CancellationToken ct) => service.CreateAsync(request, ct))
            .RequirePermission(CommunicationsPermission.Manage)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<MessageTemplateView>>();

        templates.MapPut("/{templateId:guid}", (Guid templateId, SaveTemplateRequest request, MessageTemplateService service, CancellationToken ct) =>
                service.UpdateAsync(templateId, request, ct))
            .RequirePermission(CommunicationsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<MessageTemplateView>>();

        templates.MapDelete("/{templateId:guid}", (Guid templateId, MessageTemplateService service, CancellationToken ct) =>
                service.ArchiveAsync(templateId, ct))
            .RequirePermission(CommunicationsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer>();

        var optOuts = admin.MapGroup("/opt-outs");

        optOuts.MapGet("/", (OptOutService service, CancellationToken ct) => service.ListAsync(ct))
            .RequirePermission(CommunicationsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<OptOutView>>>();

        optOuts.MapPost("/", (AddOptOutRequest request, OptOutService service, CancellationToken ct) => service.AddAsync(request, ct))
            .RequirePermission(CommunicationsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<OptOutView>>();

        optOuts.MapDelete("/{phone}", (string phone, OptOutService service, CancellationToken ct) => service.RemoveAsync(phone, ct))
            .RequirePermission(CommunicationsPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer>();
    }

    public static void MapCommunicationsClientEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var client = endpoints.MapGroup("/api/client/communications").WithTags("Communications (client)");

        client.MapGet("/preferences", (ICurrentUser user, OptOutService service, CancellationToken ct) =>
                service.GetPreferencesAsync(user.Id, ct))
            .RequireAuthorization()
            .AddEndpointFilter<ResultToResponseTransformer<MessagePreferencesView>>();

        client.MapPut("/preferences", (MessagePreferencesRequest request, ICurrentUser user, OptOutService service, CancellationToken ct) =>
                service.SetPreferencesAsync(user.Id, request, ct))
            .RequireAuthorization()
            .AddEndpointFilter<ResultToResponseTransformer<MessagePreferencesView>>();
    }

    public static void MapCommunicationsWebhooks(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/webhooks/sms/{token}", DeliveryReportWebhook.HandleAsync)
            .WithTags("Webhooks")
            .AllowAnonymous()
            .ExcludeFromDescription();
}
