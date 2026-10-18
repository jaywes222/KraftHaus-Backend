using Commerce.Domain.Common;
using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;

namespace Commerce.Domain.Krafts;

/// <summary>The creative discipline a Kraft works in (PRODUCT.md: music, fashion, food, tech, film, photography).</summary>
public enum KraftDiscipline
{
    Food = 1,
    Drinks = 2,
    Fashion = 3,
    Music = 4,
    Art = 5,
    Tech = 6,
    Film = 7,
    Photography = 8,
    Other = 9
}

/// <summary>Where a Kraft's customers pay at an event.</summary>
public enum PaymentRouting
{
    /// <summary>Into the Kraft's own approved till (a PalPluss channel); the Kraft owes the platform its share.</summary>
    OwnTill = 1,

    /// <summary>Into the KRAFTHAUS till; the platform pays the Kraft its share at settlement.</summary>
    PlatformTill = 2
}

public enum KraftStatus
{
    Active = 1,

    /// <summary>No longer shown or signed up for new events; history stays.</summary>
    Archived = 2
}

/// <summary>
///     A Kraft (Spec 08 §4): a vendor or creative collective with its brands and the Krafters behind it, signed up for
///     events. Owned by the tenant; a Kraft Owner and Kraft Staff are Identity role assignments scoped to its id
///     (BR-026). Its till for an event lives in Payments; its menu items are their own aggregates.
/// </summary>
public sealed class Kraft : AggregateRoot<KraftId>
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 600;
    public const int MaxBrands = 10;
    public const int MaxKrafters = 30;

    private readonly List<KraftBrand> _brands = [];
    private readonly List<Krafter> _krafters = [];
    private readonly List<KraftParticipation> _participations = [];

    private Kraft(KraftId id)
        : base(id)
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public KraftDiscipline Discipline { get; private set; }

    public string? Description { get; private set; }

    public KraftStatus Status { get; private set; }

    /// <summary>Logo shown on the members wall and the menu (storage path from ids only).</summary>
    public string? LogoPath { get; private set; }

    public int LogoVersion { get; private set; }

    public IReadOnlyList<KraftBrand> Brands => _brands.AsReadOnly();

    public IReadOnlyList<Krafter> Krafters => _krafters.AsReadOnly();

    /// <summary>Events this Kraft takes part in, with the terms for each.</summary>
    public IReadOnlyList<KraftParticipation> Participations => _participations.AsReadOnly();

    public static Result<Kraft> Create(KraftId id, string? name, KraftDiscipline discipline, string? description, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var checkedProfile = CheckProfile(name, discipline, description);
        if (checkedProfile.IsFailure)
        {
            return checkedProfile.Error!;
        }

        var kraft = new Kraft(id)
        {
            Name = checkedProfile.Value!.Name,
            Description = checkedProfile.Value.Description,
            Discipline = discipline,
            Slug = slug,
            Status = KraftStatus.Active
        };

        kraft.RaiseEvent(new KraftCreated(id, discipline));

        return kraft;
    }

    public Result UpdateProfile(string? name, KraftDiscipline discipline, string? description)
    {
        var active = EnsureActive();
        if (active.IsFailure)
        {
            return active;
        }

        var checkedProfile = CheckProfile(name, discipline, description);
        if (checkedProfile.IsFailure)
        {
            return checkedProfile.Error!;
        }

        Name = checkedProfile.Value!.Name;
        Description = checkedProfile.Value.Description;
        Discipline = discipline;

        RaiseEvent(new KraftProfileUpdated(Id));

        return Result.Success;
    }

    public Result<KraftBrand> AddBrand(KraftBrandId brandId, string? name, string? description)
    {
        var active = EnsureActive();
        if (active.IsFailure)
        {
            return active.Error!;
        }

        if (_brands.Count >= MaxBrands)
        {
            return CommerceErrors.Conflict("TooManyBrands", $"A Kraft can have at most {MaxBrands} brands.");
        }

        var brand = KraftBrand.Create(brandId, name, description);
        if (brand.IsFailure)
        {
            return brand.Error!;
        }

        if (_brands.Exists(x => string.Equals(x.Name, brand.Value!.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return CommerceErrors.Conflict("BrandExists", "This Kraft already has a brand with that name.");
        }

        _brands.Add(brand.Value!);
        RaiseEvent(new KraftBrandAdded(Id, brandId));

        return brand.Value!;
    }

    public Result<KraftBrand> UpdateBrand(KraftBrandId brandId, string? name, string? description)
    {
        var brand = _brands.Find(x => x.Id == brandId);
        if (brand is null)
        {
            return CommerceErrors.NotFound("BrandNotFound", "The brand was not found.");
        }

        var updated = brand.Update(name, description);
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        if (_brands.Exists(x => x.Id != brandId && string.Equals(x.Name, brand.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return CommerceErrors.Conflict("BrandExists", "This Kraft already has a brand with that name.");
        }

        RaiseEvent(new KraftBrandUpdated(Id, brandId));

        return brand;
    }

    public Result RemoveBrand(KraftBrandId brandId)
    {
        var brand = _brands.Find(x => x.Id == brandId);
        if (brand is null)
        {
            return CommerceErrors.NotFound("BrandNotFound", "The brand was not found.");
        }

        _brands.Remove(brand);
        RaiseEvent(new KraftBrandRemoved(Id, brandId));

        return Result.Success;
    }

    public Result<Krafter> AddKrafter(KrafterId krafterId, KrafterSpec spec)
    {
        var active = EnsureActive();
        if (active.IsFailure)
        {
            return active.Error!;
        }

        if (_krafters.Count >= MaxKrafters)
        {
            return CommerceErrors.Conflict("TooManyKrafters", $"A Kraft can list at most {MaxKrafters} Krafters.");
        }

        var krafter = Krafter.Create(krafterId, spec);
        if (krafter.IsFailure)
        {
            return krafter.Error!;
        }

        _krafters.Add(krafter.Value!);
        RaiseEvent(new KrafterAdded(Id, krafterId));

        return krafter.Value!;
    }

    public Result<Krafter> UpdateKrafter(KrafterId krafterId, KrafterSpec spec)
    {
        var krafter = _krafters.Find(x => x.Id == krafterId);
        if (krafter is null)
        {
            return CommerceErrors.NotFound("KrafterNotFound", "The Krafter was not found.");
        }

        var updated = krafter.Update(spec);
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        RaiseEvent(new KrafterUpdated(Id, krafterId));

        return krafter;
    }

    public Result RemoveKrafter(KrafterId krafterId)
    {
        var krafter = _krafters.Find(x => x.Id == krafterId);
        if (krafter is null)
        {
            return CommerceErrors.NotFound("KrafterNotFound", "The Krafter was not found.");
        }

        _krafters.Remove(krafter);
        RaiseEvent(new KrafterRemoved(Id, krafterId));

        return Result.Success;
    }

    /// <summary>Signs the Kraft up for an event, or changes its terms there (commission, stall).</summary>
    public Result<KraftParticipation> SetEventTerms(
        KraftParticipationId participationId,
        Guid eventId,
        decimal commissionPercent,
        string? stall,
        PaymentRouting routing = PaymentRouting.OwnTill)
    {
        if (!Enum.IsDefined(routing))
        {
            return CommerceErrors.Invalid("KraftEventInvalid", ["PaymentRouting: Choose OwnTill or PlatformTill."]);
        }

        var active = EnsureActive();
        if (active.IsFailure)
        {
            return active.Error!;
        }

        var terms = KraftParticipation.CheckTerms(commissionPercent, stall);
        if (terms.IsFailure)
        {
            return terms.Error!;
        }

        var existing = _participations.Find(x => x.EventId == eventId);

        if (existing is null)
        {
            var joined = KraftParticipation.Create(participationId, eventId, commissionPercent, terms.Value, routing);
            _participations.Add(joined);
            RaiseEvent(new KraftJoinedEvent(Id, eventId, commissionPercent));
            return joined;
        }

        var changedCommission = existing.CommissionPercent != commissionPercent;
        existing.Change(commissionPercent, terms.Value, routing);

        RaiseEvent(new KraftEventTermsChanged(Id, eventId, commissionPercent, changedCommission));

        return existing;
    }

    public Result WithdrawFromEvent(Guid eventId)
    {
        var participation = _participations.Find(x => x.EventId == eventId);
        if (participation is null)
        {
            return CommerceErrors.NotFound("KraftNotAtEvent", "This Kraft does not take part in that event.");
        }

        _participations.Remove(participation);
        RaiseEvent(new KraftWithdrawnFromEvent(Id, eventId));

        return Result.Success;
    }

    /// <summary>Records a new logo and returns the previous path so the caller can delete the old file.</summary>
    public Result<string?> ReplaceLogo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var active = EnsureActive();
        if (active.IsFailure)
        {
            return active.Error!;
        }

        var previous = LogoPath;
        LogoPath = path;
        LogoVersion++;
        RaiseEvent(new KraftProfileUpdated(Id));

        return previous;
    }

    public KraftParticipation? AtEvent(Guid eventId) =>
        Status == KraftStatus.Active ? _participations.Find(x => x.EventId == eventId) : null;

    public Result Archive()
    {
        if (Status == KraftStatus.Archived)
        {
            return CommerceErrors.Conflict("KraftArchived", "This Kraft is archived.");
        }

        Status = KraftStatus.Archived;
        RaiseEvent(new KraftArchived(Id));

        return Result.Success;
    }

    private Result EnsureActive() =>
        Status == KraftStatus.Active
            ? Result.Success
            : CommerceErrors.Conflict("KraftArchived", "This Kraft is archived.");

    private static Result<(string Name, string? Description)> CheckProfile(string? name, KraftDiscipline discipline, string? description)
    {
        var errors = new List<string>();

        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length is < 2 or > NameMaxLength)
        {
            errors.Add($"Name: Use 2-{NameMaxLength} characters.");
        }

        if (!Enum.IsDefined(discipline))
        {
            errors.Add("Discipline: Choose a discipline.");
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is { Length: > DescriptionMaxLength })
        {
            errors.Add($"Description: Keep it under {DescriptionMaxLength} characters.");
        }

        return errors.Count > 0
            ? CommerceErrors.Invalid("KraftInvalid", errors)
            : (trimmedName, trimmedDescription);
    }
}
