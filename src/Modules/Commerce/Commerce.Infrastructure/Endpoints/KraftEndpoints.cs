using Commerce.Application.Images;
using Commerce.Application.Krafts;
using Commerce.Domain.Common;
using Commerce.Domain.Krafts;
using Common.Application.Auth;
using Common.Application.EndpointFilters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Commerce.Infrastructure.Endpoints;

/// <summary>
///     Kraft onboarding (UF-017). Routes carrying <c>{kraftId}</c> are Kraft-scoped: a Kraft Owner reaches only its own
///     Kraft, tenant roles reach every Kraft of the tenant (BR-026).
/// </summary>
internal static class KraftEndpoints
{
    public static void MapAdminKraftEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/krafts").WithTags("Krafts (admin)");

        admin.MapPost("/", (CreateKraftRequest request, KraftService service, CancellationToken ct) =>
                service.CreateAsync(request, ct))
            .RequirePermission(CommercePermission.ManageKrafts)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<KraftView>>();

        admin.MapGet("/", (KraftStatus? status, Guid? eventId, KraftService service, CancellationToken ct) =>
                service.ListAsync(status, eventId, ct))
            .RequirePermission(CommercePermission.ViewKrafts)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<KraftView>>>();

        var kraft = admin.MapGroup("/{kraftId:guid}");

        kraft.MapPost("/logo", async (Guid kraftId, IFormFile file, CommerceImageService service, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return await service.UploadLogoAsync(new KraftId(kraftId), stream, file.Length, ct);
            })
            .RequirePermission(CommercePermission.ManageKraft)
            .DisableAntiforgery()
            .AddEndpointFilter<ResultToResponseTransformer<string>>();

        kraft.MapGet("/", (Guid kraftId, KraftService service, CancellationToken ct) =>
                service.GetAsync(new KraftId(kraftId), ct))
            .RequirePermission(CommercePermission.ManageKraft)
            .AddEndpointFilter<ResultToResponseTransformer<KraftView>>();

        kraft.MapPut("/", (Guid kraftId, UpdateKraftRequest request, KraftService service, CancellationToken ct) =>
                service.UpdateProfileAsync(new KraftId(kraftId), request, ct))
            .RequirePermission(CommercePermission.ManageKraft)
            .AddEndpointFilter<ResultToResponseTransformer<KraftView>>();

        kraft.MapPost("/archive", (Guid kraftId, KraftService service, CancellationToken ct) =>
                service.ArchiveAsync(new KraftId(kraftId), ct))
            .RequirePermission(CommercePermission.ManageKrafts)
            .AddEndpointFilter<ResultToResponseTransformer<KraftView>>();

        kraft.MapPost("/brands", (Guid kraftId, BrandRequest request, KraftService service, CancellationToken ct) =>
                service.AddBrandAsync(new KraftId(kraftId), request, ct))
            .RequirePermission(CommercePermission.ManageKraft)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<KraftView>>();

        kraft.MapPut("/brands/{brandId:guid}", (Guid kraftId, Guid brandId, BrandRequest request, KraftService service, CancellationToken ct) =>
                service.UpdateBrandAsync(new KraftId(kraftId), new KraftBrandId(brandId), request, ct))
            .RequirePermission(CommercePermission.ManageKraft)
            .AddEndpointFilter<ResultToResponseTransformer<KraftView>>();

        kraft.MapDelete("/brands/{brandId:guid}", (Guid kraftId, Guid brandId, KraftService service, CancellationToken ct) =>
                service.RemoveBrandAsync(new KraftId(kraftId), new KraftBrandId(brandId), ct))
            .RequirePermission(CommercePermission.ManageKraft)
            .AddEndpointFilter<ResultToResponseTransformer<KraftView>>();

        kraft.MapPost("/krafters", (Guid kraftId, KrafterRequest request, KraftService service, CancellationToken ct) =>
                service.AddKrafterAsync(new KraftId(kraftId), request, ct))
            .RequirePermission(CommercePermission.ManageKraft)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<KraftView>>();

        kraft.MapPut("/krafters/{krafterId:guid}", (Guid kraftId, Guid krafterId, KrafterRequest request, KraftService service, CancellationToken ct) =>
                service.UpdateKrafterAsync(new KraftId(kraftId), new KrafterId(krafterId), request, ct))
            .RequirePermission(CommercePermission.ManageKraft)
            .AddEndpointFilter<ResultToResponseTransformer<KraftView>>();

        kraft.MapDelete("/krafters/{krafterId:guid}", (Guid kraftId, Guid krafterId, KraftService service, CancellationToken ct) =>
                service.RemoveKrafterAsync(new KraftId(kraftId), new KrafterId(krafterId), ct))
            .RequirePermission(CommercePermission.ManageKraft)
            .AddEndpointFilter<ResultToResponseTransformer<KraftView>>();

        // Event terms carry the commission: money roles only.
        kraft.MapGet("/events", (Guid kraftId, KraftService service, CancellationToken ct) =>
                service.TermsAsync(new KraftId(kraftId), ct))
            .RequirePermission(CommercePermission.ViewMoney)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<KraftTermsView>>>();

        kraft.MapPut("/events/{eventId:guid}", (Guid kraftId, Guid eventId, KraftEventTermsRequest request, KraftService service, CancellationToken ct) =>
                service.SetEventTermsAsync(new KraftId(kraftId), eventId, request, ct))
            .RequirePermission(CommercePermission.ManageKrafts)
            .AddEndpointFilter<ResultToResponseTransformer<KraftTermsView>>();

        kraft.MapDelete("/events/{eventId:guid}", (Guid kraftId, Guid eventId, KraftService service, CancellationToken ct) =>
                service.WithdrawFromEventAsync(new KraftId(kraftId), eventId, ct))
            .RequirePermission(CommercePermission.ManageKrafts)
            .AddEndpointFilter<ResultToResponseTransformer<KraftView>>();
    }

    public static void MapClientKraftEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/client/events/{slug}/krafts", (string slug, KraftCatalogService service, CancellationToken ct) =>
                service.ListAtEventAsync(slug, ct))
            .WithTags("Krafts (client)")
            .AllowAnonymous()
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<PublicKraftView>>>();

        // The members wall: every active Kraft with its logo.
        endpoints.MapGet("/api/client/krafts", (KraftCatalogService service, CancellationToken ct) =>
                service.MembersAsync(ct))
            .WithTags("Krafts (client)")
            .AllowAnonymous()
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<KraftMemberView>>>();

        // Logos and menu photos (public, like the menu). Names are checked against the stored pattern.
        endpoints.MapGet(CommerceImageUrls.Route + "{fileName}", (string fileName, HttpContext http, CommerceImageService service) =>
            {
                if (service.Open(fileName) is not { } image)
                {
                    return Results.NotFound();
                }

                http.Response.Headers.CacheControl = "public, max-age=604800, immutable";
                return Results.Stream(image.Content, image.ContentType);
            })
            .WithTags("Krafts (client)")
            .AllowAnonymous();
    }
}
