using Commerce.Application.Images;
using Commerce.Application.Menus;
using Commerce.Domain.Common;
using Common.Application.EndpointFilters;
using Common.Application.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Commerce.Infrastructure.Endpoints;

/// <summary>Menus (UF-017), the kitchen's on/off switch (UF-015) and the public menu (UF-007).</summary>
internal static class MenuEndpoints
{
    public static void MapMenuEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/krafts/{kraftId:guid}/menu").WithTags("Menus (admin)");

        admin.MapPost("/", (Guid kraftId, AddMenuItemRequest request, MenuService service, CancellationToken ct) =>
                service.AddAsync(new KraftId(kraftId), request, ct))
            .RequirePermission(CommercePermission.ManageKraft)
            .AddEndpointFilter<ResultToCreatedResponseTransformer<MenuItemAdminView>>();

        admin.MapGet("/", (Guid kraftId, Guid? eventId, bool? includeRetired, MenuService service, CancellationToken ct) =>
                service.ListAsync(new KraftId(kraftId), eventId, includeRetired ?? false, ct))
            .RequirePermission(CommercePermission.KitchenView)
            .AddEndpointFilter<ResultToResponseTransformer<IReadOnlyList<MenuItemAdminView>>>();

        admin.MapPut("/{itemId:guid}", (Guid kraftId, Guid itemId, MenuItemRequest request, MenuService service, CancellationToken ct) =>
                service.UpdateAsync(new KraftId(kraftId), new MenuItemId(itemId), request, ct))
            .RequirePermission(CommercePermission.ManageKraft)
            .AddEndpointFilter<ResultToResponseTransformer<MenuItemAdminView>>();

        admin.MapDelete("/{itemId:guid}", (Guid kraftId, Guid itemId, MenuService service, CancellationToken ct) =>
                service.RetireAsync(new KraftId(kraftId), new MenuItemId(itemId), ct))
            .RequirePermission(CommercePermission.ManageKraft)
            .AddEndpointFilter<ResultToResponseTransformer<MenuItemAdminView>>();

        admin.MapPost("/{itemId:guid}/image", async (Guid kraftId, Guid itemId, IFormFile file, CommerceImageService service, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return await service.UploadMenuImageAsync(new KraftId(kraftId), new MenuItemId(itemId), stream, file.Length, ct);
            })
            .RequirePermission(CommercePermission.ManageKraft)
            .DisableAntiforgery()
            .AddEndpointFilter<ResultToResponseTransformer<string>>();

        endpoints.MapPut("/api/ops/krafts/{kraftId:guid}/menu/{itemId:guid}/availability",
                (Guid kraftId, Guid itemId, SetAvailabilityRequest request, MenuService service, CancellationToken ct) =>
                    service.SetAvailableAsync(new KraftId(kraftId), new MenuItemId(itemId), request, ct))
            .WithTags("Kitchen (ops)")
            .RequirePermission(CommercePermission.KitchenHandle)
            .AddEndpointFilter<ResultToResponseTransformer<MenuItemAdminView>>();

        endpoints.MapGet("/api/client/events/{slug}/menu", (string slug, MenuService service, CancellationToken ct) =>
                service.PublicMenuAsync(slug, ct))
            .WithTags("Menus (client)")
            .AllowAnonymous()
            .AddEndpointFilter<ResultToResponseTransformer<PublicMenuView>>();
    }
}
