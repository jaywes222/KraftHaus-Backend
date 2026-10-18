using Commerce.Application.Abstractions;
using Commerce.Domain.Common;
using Commerce.Domain.Krafts;
using Common.Application.EventCatalog;
using Common.Domain.ResultMonad;

namespace Commerce.Application.Krafts;

/// <summary>
///     Kraft onboarding (UF-017): the Tenant Admin creates Krafts and signs them up for events with a commission; the
///     Kraft Owner keeps its profile, brands and Krafters up to date.
/// </summary>
public sealed class KraftService(
    IKraftRepository krafts,
    ICommerceUnitOfWork unitOfWork,
    IEventCatalog events)
{
    public async Task<Result<KraftView>> CreateAsync(CreateKraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var slug = await UniqueSlugAsync(request.Name ?? string.Empty, cancellationToken);
        var created = Kraft.Create(new KraftId(Guid.CreateVersion7()), request.Name, request.Discipline, request.Description, slug);

        if (created.IsFailure)
        {
            return created.Error!;
        }

        krafts.Add(created.Value!);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);

        return saved.IsFailure ? saved.Error! : KraftView.From(created.Value!);
    }

    public async Task<Result<IReadOnlyList<KraftView>>> ListAsync(KraftStatus? status, Guid? eventId, CancellationToken cancellationToken)
    {
        var list = await krafts.ListForReadAsync(status, eventId, cancellationToken);

        return Result<IReadOnlyList<KraftView>>.Success([.. list.Select(KraftView.From)]);
    }

    public async Task<Result<KraftView>> GetAsync(KraftId id, CancellationToken cancellationToken)
    {
        var kraft = await krafts.FindForReadAsync(id, cancellationToken);

        return kraft is null ? CommerceErrors.KraftNotFound : KraftView.From(kraft);
    }

    public Task<Result<KraftView>> UpdateProfileAsync(KraftId id, UpdateKraftRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, kraft => kraft.UpdateProfile(request?.Name, request?.Discipline ?? default, request?.Description), cancellationToken);

    public Task<Result<KraftView>> ArchiveAsync(KraftId id, CancellationToken cancellationToken) =>
        ChangeAsync(id, kraft => kraft.Archive(), cancellationToken);

    public Task<Result<KraftView>> AddBrandAsync(KraftId id, BrandRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, kraft => kraft.AddBrand(new KraftBrandId(Guid.CreateVersion7()), request?.Name, request?.Description), cancellationToken);

    public Task<Result<KraftView>> UpdateBrandAsync(KraftId id, KraftBrandId brandId, BrandRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, kraft => kraft.UpdateBrand(brandId, request?.Name, request?.Description), cancellationToken);

    public Task<Result<KraftView>> RemoveBrandAsync(KraftId id, KraftBrandId brandId, CancellationToken cancellationToken) =>
        ChangeAsync(id, kraft => kraft.RemoveBrand(brandId), cancellationToken);

    public Task<Result<KraftView>> AddKrafterAsync(KraftId id, KrafterRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, kraft => kraft.AddKrafter(new KrafterId(Guid.CreateVersion7()), Spec(request)), cancellationToken);

    public Task<Result<KraftView>> UpdateKrafterAsync(KraftId id, KrafterId krafterId, KrafterRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, kraft => kraft.UpdateKrafter(krafterId, Spec(request)), cancellationToken);

    public Task<Result<KraftView>> RemoveKrafterAsync(KraftId id, KrafterId krafterId, CancellationToken cancellationToken) =>
        ChangeAsync(id, kraft => kraft.RemoveKrafter(krafterId), cancellationToken);

    /// <summary>Signs the Kraft up for an event (or changes its commission and stall there).</summary>
    public async Task<Result<KraftTermsView>> SetEventTermsAsync(
        KraftId id,
        Guid eventId,
        KraftEventTermsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sale = await events.GetSaleSnapshotAsync(eventId, cancellationToken);
        if (sale is null || sale.Status is EventLifecycle.Cancelled or EventLifecycle.Completed)
        {
            return CommerceErrors.EventNotFound;
        }

        var kraft = await krafts.FindAsync(id, cancellationToken);
        if (kraft is null)
        {
            return CommerceErrors.KraftNotFound;
        }

        var terms = kraft.SetEventTerms(
            new KraftParticipationId(Guid.CreateVersion7()),
            eventId,
            request.CommissionPercent,
            request.Stall,
            request.PaymentRouting ?? kraft.AtEvent(eventId)?.PaymentRouting ?? PaymentRouting.OwnTill);
        if (terms.IsFailure)
        {
            return terms.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);

        return saved.IsFailure
            ? saved.Error!
            : KraftTermsView.From(kraft.Id.Value, terms.Value!);
    }

    public Task<Result<KraftView>> WithdrawFromEventAsync(KraftId id, Guid eventId, CancellationToken cancellationToken) =>
        ChangeAsync(id, kraft => kraft.WithdrawFromEvent(eventId), cancellationToken);

    public async Task<Result<IReadOnlyList<KraftTermsView>>> TermsAsync(KraftId id, CancellationToken cancellationToken)
    {
        var kraft = await krafts.FindForReadAsync(id, cancellationToken);
        if (kraft is null)
        {
            return CommerceErrors.KraftNotFound;
        }

        return Result<IReadOnlyList<KraftTermsView>>.Success(
            [.. kraft.Participations.Select(x => KraftTermsView.From(kraft.Id.Value, x))]);
    }

    private async Task<Result<KraftView>> ChangeAsync(KraftId id, Func<Kraft, IResult> change, CancellationToken cancellationToken)
    {
        var kraft = await krafts.FindAsync(id, cancellationToken);
        if (kraft is null)
        {
            return CommerceErrors.KraftNotFound;
        }

        var changed = change(kraft);
        if (changed.Error is { } error)
        {
            return error;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);

        return saved.IsFailure ? saved.Error! : KraftView.From(kraft);
    }

    private async Task<string> UniqueSlugAsync(string name, CancellationToken cancellationToken)
    {
        var baseSlug = Slug.From(name);
        var candidate = baseSlug;
        var n = 1;

        while (await krafts.SlugExistsAsync(candidate, cancellationToken))
        {
            n++;
            candidate = $"{baseSlug}-{n}";
        }

        return candidate;
    }

    private static KrafterSpec Spec(KrafterRequest? request) =>
        new(request?.Name, request?.Role, request?.Bio, request?.Instagram);
}
