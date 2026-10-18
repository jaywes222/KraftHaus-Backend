using Commerce.Application.Abstractions;
using Commerce.Domain.Common;
using Commerce.Domain.Krafts;
using Common.Application.EventCatalog;
using Commerce.Application.Images;
using Common.Domain.ResultMonad;

namespace Commerce.Application.Krafts;

/// <summary>Public list of the Krafts at an event (event page, UF-007): who they are and whether they take orders.</summary>
public sealed class KraftCatalogService(
    IKraftRepository krafts,
    IEventCatalog events,
    KraftTills tills)
{
    public async Task<Result<IReadOnlyList<PublicKraftView>>> ListAtEventAsync(string slug, CancellationToken cancellationToken)
    {
        var snapshot = await events.FindBySlugAsync(slug, cancellationToken);
        if (snapshot is null || snapshot.Status == EventLifecycle.Draft)
        {
            return CommerceErrors.EventNotFound;
        }

        var list = await krafts.ListForReadAsync(KraftStatus.Active, snapshot.EventId, cancellationToken);
        var selling = await tills.SellingAsync(snapshot.EventId, list, cancellationToken);

        return Result<IReadOnlyList<PublicKraftView>>.Success(
        [
            .. list.Select(kraft => new PublicKraftView(
                kraft.Id.Value,
                kraft.Name,
                kraft.Slug,
                kraft.Discipline.ToString(),
                kraft.Description,
                kraft.AtEvent(snapshot.EventId)?.Stall,
                selling.Contains(kraft.Id.Value),
                [.. kraft.Brands.Select(KraftBrandView.From)],
                [.. kraft.Krafters.Select(x => new PublicKrafterView(x.Id.Value, x.Name, x.Role, x.Bio, x.Instagram))],
                CommerceImageUrls.For(kraft.LogoPath)))
        ]);
    }

    /// <summary>The members wall: every active Kraft with its logo, whatever event it is at.</summary>
    public async Task<Result<IReadOnlyList<KraftMemberView>>> MembersAsync(CancellationToken cancellationToken)
    {
        var list = await krafts.ListForReadAsync(KraftStatus.Active, null, cancellationToken);

        return Result<IReadOnlyList<KraftMemberView>>.Success(
        [
            .. list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => new KraftMemberView(x.Id.Value, x.Name, x.Slug, x.Discipline.ToString(), x.Description, CommerceImageUrls.For(x.LogoPath)))
        ]);
    }
}

/// <summary>A Kraft on the public members wall.</summary>
public sealed record KraftMemberView(Guid Id, string Name, string Slug, string Discipline, string? Description, string? LogoHref);
