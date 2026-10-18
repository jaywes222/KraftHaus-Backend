using System.Text.Json;
using Common.Application.Auth;
using Common.Application.EndpointFilters;
using Content.Application;
using Content.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Content.Infrastructure.Endpoints;

internal static class ContentEndpoints
{
    public static void MapContentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var client = endpoints.MapGroup("/api/client").WithTags("Content (client)").AllowAnonymous();

        client.MapGet("/articles", (string? tag, DateTimeOffset? before, int? limit, ArticleService service, CancellationToken ct) =>
                service.ListPublishedAsync(tag, before, limit, ct))
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<ArticleSummaryView>>>();

        client.MapGet("/articles/{slug}", (string slug, ArticleService service, CancellationToken ct) =>
                service.GetPublishedAsync(slug, ct))
            .AddEndpointFilter<ResultToResponseTransformer<ArticleView>>();

        client.MapGet("/site", (SiteStudioService service, CancellationToken ct) => service.GetPublishedAsync(ct))
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyDictionary<string, JsonElement>>>();

        endpoints.MapGet(ContentImageUrls.Route + "{fileName}", (string fileName, HttpContext http, ArticleService service) =>
            {
                if (service.OpenImage(fileName) is not { } image)
                {
                    return Results.NotFound();
                }

                http.Response.Headers.CacheControl = "public, max-age=604800, immutable";
                return Results.Stream(image.Content, image.ContentType);
            })
            .WithTags("Content (client)")
            .AllowAnonymous();

        var admin = endpoints.MapGroup("/api/admin").WithTags("Content (admin)");
        var articles = admin.MapGroup("/articles");

        articles.MapGet("/", (ArticleStatus? status, ArticleService service, CancellationToken ct) => service.ListAsync(status, ct))
            .RequirePermission(ContentPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<ArticleSummaryView>>>();

        articles.MapPost("/", (ArticleRequest request, ArticleService service, CancellationToken ct) => service.CreateAsync(request.ToInput(), ct))
            .RequirePermission(ContentPermission.Manage)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<ArticleView>>();

        articles.MapGet("/{articleId:guid}", (Guid articleId, ArticleService service, CancellationToken ct) => service.GetAsync(articleId, ct))
            .RequirePermission(ContentPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<ArticleView>>();

        articles.MapPut("/{articleId:guid}", (Guid articleId, ArticleRequest request, ArticleService service, CancellationToken ct) =>
                service.UpdateAsync(articleId, request.ToInput(), ct))
            .RequirePermission(ContentPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<ArticleView>>();

        articles.MapPost("/{articleId:guid}/publish", (Guid articleId, ArticleService service, CancellationToken ct) => service.PublishAsync(articleId, ct))
            .RequirePermission(ContentPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<ArticleView>>();

        articles.MapPost("/{articleId:guid}/unpublish", (Guid articleId, ArticleService service, CancellationToken ct) => service.UnpublishAsync(articleId, ct))
            .RequirePermission(ContentPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<ArticleView>>();

        articles.MapPost("/{articleId:guid}/archive", (Guid articleId, ArticleService service, CancellationToken ct) => service.ArchiveAsync(articleId, ct))
            .RequirePermission(ContentPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<ArticleView>>();

        articles.MapPost("/{articleId:guid}/cover", async (Guid articleId, IFormFile file, ArticleService service, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return await service.UploadCoverAsync(articleId, stream, file.Length, ct);
            })
            .RequirePermission(ContentPermission.Manage)
            .DisableAntiforgery()
            .AddEndpointFilter<ResultToResponseTransformer<string>>();

        var site = admin.MapGroup("/site");

        site.MapGet("/blocks", (SiteStudioService service, CancellationToken ct) => service.ListAsync(ct))
            .RequirePermission(ContentPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<SiteBlockView>>>();

        site.MapPut("/blocks/{key}", (SiteBlockKey key, SiteBlockRequest request, SiteStudioService service, CancellationToken ct) =>
                service.SaveDraftAsync(key, request.Content, ct))
            .RequirePermission(ContentPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<SiteBlockView>>();

        site.MapPost("/blocks/{key}/publish", (SiteBlockKey key, SiteStudioService service, CancellationToken ct) => service.PublishAsync(key, ct))
            .RequirePermission(ContentPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<SiteBlockView>>();

        site.MapPost("/publish", (SiteStudioService service, CancellationToken ct) => service.PublishAllAsync(ct))
            .RequirePermission(ContentPermission.Manage)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<SiteBlockView>>>();
    }
}
