using Commerce.Application.Images;
using Commerce.Domain.Krafts;

namespace Commerce.Application.Krafts;

public sealed record KraftBrandView(Guid Id, string Name, string? Description)
{
    public static KraftBrandView From(KraftBrand brand) => new(brand.Id.Value, brand.Name, brand.Description);
}

public sealed record KrafterView(Guid Id, string Name, string? Role, string? Bio, string? Instagram)
{
    public static KrafterView From(Krafter krafter) =>
        new(krafter.Id.Value, krafter.Name, krafter.Role, krafter.Bio, krafter.Instagram);
}

/// <summary>A Kraft as admins and its owner see it. No commission (that is <see cref="KraftTermsView" />).</summary>
public sealed record KraftView(
    Guid Id,
    string Name,
    string Slug,
    string Discipline,
    string? Description,
    string Status,
    IReadOnlyList<KraftBrandView> Brands,
    IReadOnlyList<KrafterView> Krafters,
    IReadOnlyList<KraftEventView> Events,
    string? LogoHref)
{
    public static KraftView From(Kraft kraft)
    {
        ArgumentNullException.ThrowIfNull(kraft);

        return new KraftView(
            kraft.Id.Value,
            kraft.Name,
            kraft.Slug,
            kraft.Discipline.ToString(),
            kraft.Description,
            kraft.Status.ToString(),
            [.. kraft.Brands.Select(KraftBrandView.From)],
            [.. kraft.Krafters.Select(KrafterView.From)],
            [.. kraft.Participations.Select(x => new KraftEventView(x.EventId, x.Stall))],
            CommerceImageUrls.For(kraft.LogoPath));
    }
}

public sealed record KraftEventView(Guid EventId, string? Stall);

/// <summary>A Kraft's terms at an event, with the commission: Tenant Admin, Finance and the Kraft Owner only.</summary>
public sealed record KraftTermsView(Guid KraftId, Guid EventId, decimal CommissionPercent, string? Stall, string PaymentRouting)
{
    public static KraftTermsView From(Guid kraftId, KraftParticipation terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        return new KraftTermsView(kraftId, terms.EventId, terms.CommissionPercent, terms.Stall, terms.PaymentRouting.ToString());
    }
}

/// <summary>A Kraft on an event's public page.</summary>
public sealed record PublicKraftView(
    Guid Id,
    string Name,
    string Slug,
    string Discipline,
    string? Description,
    string? Stall,
    bool TakesOrders,
    IReadOnlyList<KraftBrandView> Brands,
    IReadOnlyList<PublicKrafterView> Krafters,
    string? LogoHref);

public sealed record PublicKrafterView(Guid Id, string Name, string? Role, string? Bio, string? Instagram);

public sealed record CreateKraftRequest(string? Name, KraftDiscipline Discipline, string? Description);

public sealed record UpdateKraftRequest(string? Name, KraftDiscipline Discipline, string? Description);

public sealed record BrandRequest(string? Name, string? Description);

public sealed record KrafterRequest(string? Name, string? Role, string? Bio, string? Instagram);

/// <param name="PaymentRouting">OwnTill (default) or PlatformTill.</param>
public sealed record KraftEventTermsRequest(decimal CommissionPercent, string? Stall, PaymentRouting? PaymentRouting = null);
