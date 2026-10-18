using Common.Application.Auth;
using Common.Application.EndpointFilters;
using Feedback.Application;
using Feedback.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Feedback.Infrastructure.Endpoints;

internal static class FeedbackEndpoints
{
    public static void MapFeedbackEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var client = endpoints.MapGroup("/api/client/events/{slug}/feedback").WithTags("Feedback (client)");

        client.MapGet("/", (string slug, ICurrentUser user, FeedbackService service, CancellationToken ct) =>
                service.GetMineAsync(slug, user.Id, ct))
            .RequireAuthorization()
            .AddEndpointFilter<ResultToResponseTransformer<MyFeedbackView>>();

        client.MapPut("/{eventDateId:guid}", (string slug, Guid eventDateId, RatingRequest request, ICurrentUser user, FeedbackService service, CancellationToken ct) =>
                service.SubmitAsync(slug, eventDateId, user.Id, request.ToInput(), ct))
            .RequireAuthorization()
            .AddEndpointFilter<ResultToResponseTransformer<RatingInputView>>();

        var admin = endpoints.MapGroup("/api/admin").WithTags("Feedback (admin)");

        admin.MapGet("/events/{eventId:guid}/feedback", (Guid eventId, FeedbackService service, CancellationToken ct) =>
                service.SummaryAsync(eventId, ct))
            .RequirePermission(FeedbackPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<EventFeedbackView>>();

        admin.MapGet("/events/{eventId:guid}/feedback/comments", (Guid eventId, Guid? eventDateId, int? limit, FeedbackService service, CancellationToken ct) =>
                service.CommentsAsync(eventId, eventDateId, limit, ct))
            .RequirePermission(FeedbackPermission.View)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<FeedbackCommentView>>>();

        admin.MapPost("/feedback/{ratingId:guid}/hide", (Guid ratingId, FeedbackService service, CancellationToken ct) =>
                service.SetCommentHiddenAsync(ratingId, true, ct))
            .RequirePermission(FeedbackPermission.Moderate)
            .AddEndpointFilter<ResultToResponseTransformer<FeedbackCommentView>>();

        admin.MapPost("/feedback/{ratingId:guid}/show", (Guid ratingId, FeedbackService service, CancellationToken ct) =>
                service.SetCommentHiddenAsync(ratingId, false, ct))
            .RequirePermission(FeedbackPermission.Moderate)
            .AddEndpointFilter<ResultToResponseTransformer<FeedbackCommentView>>();
    }
}
