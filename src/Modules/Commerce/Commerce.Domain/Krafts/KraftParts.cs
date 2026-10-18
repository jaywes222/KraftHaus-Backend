using System.Text.RegularExpressions;
using Commerce.Domain.Common;
using Common.Domain.Entities;
using Common.Domain.ResultMonad;

namespace Commerce.Domain.Krafts;

/// <summary>A label a Kraft sells under (a Kraft can run several).</summary>
public sealed class KraftBrand : AuditableEntity<KraftBrandId>
{
    public const int NameMaxLength = 60;
    public const int DescriptionMaxLength = 300;

    private KraftBrand(KraftBrandId id)
        : base(id)
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    internal static Result<KraftBrand> Create(KraftBrandId id, string? name, string? description)
    {
        var brand = new KraftBrand(id);
        var set = brand.Update(name, description);

        return set.IsFailure ? set.Error! : brand;
    }

    internal Result Update(string? name, string? description)
    {
        var errors = new List<string>();
        var trimmedName = name?.Trim() ?? string.Empty;
        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        if (trimmedName.Length is < 1 or > NameMaxLength)
        {
            errors.Add($"Name: Use 1-{NameMaxLength} characters.");
        }

        if (trimmedDescription is { Length: > DescriptionMaxLength })
        {
            errors.Add($"Description: Keep it under {DescriptionMaxLength} characters.");
        }

        if (errors.Count > 0)
        {
            return CommerceErrors.Invalid("BrandInvalid", errors);
        }

        Name = trimmedName;
        Description = trimmedDescription;

        return Result.Success;
    }
}

/// <param name="Role">What they do in the Kraft ("Head chef", "Designer").</param>
/// <param name="Instagram">Handle without the @.</param>
public sealed record KrafterSpec(string? Name, string? Role, string? Bio, string? Instagram);

/// <summary>A person behind a Kraft, shown on its public profile (and later on gifts and the leaderboard).</summary>
public sealed partial class Krafter : AuditableEntity<KrafterId>
{
    public const int NameMaxLength = 80;
    public const int RoleMaxLength = 60;
    public const int BioMaxLength = 300;
    public const int InstagramMaxLength = 30;

    private Krafter(KrafterId id)
        : base(id)
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string? Role { get; private set; }

    public string? Bio { get; private set; }

    public string? Instagram { get; private set; }

    internal static Result<Krafter> Create(KrafterId id, KrafterSpec spec)
    {
        var krafter = new Krafter(id);
        var set = krafter.Update(spec);

        return set.IsFailure ? set.Error! : krafter;
    }

    internal Result Update(KrafterSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var errors = new List<string>();
        var name = spec.Name?.Trim() ?? string.Empty;
        var role = Clean(spec.Role);
        var bio = Clean(spec.Bio);
        var instagram = Clean(spec.Instagram)?.TrimStart('@');

        if (name.Length is < 2 or > NameMaxLength)
        {
            errors.Add($"Name: Use 2-{NameMaxLength} characters.");
        }

        if (role is { Length: > RoleMaxLength })
        {
            errors.Add($"Role: Keep it under {RoleMaxLength} characters.");
        }

        if (bio is { Length: > BioMaxLength })
        {
            errors.Add($"Bio: Keep it under {BioMaxLength} characters.");
        }

        if (instagram is not null && !InstagramHandle().IsMatch(instagram))
        {
            errors.Add("Instagram: Enter the handle, e.g. krafthaus.ke.");
        }

        if (errors.Count > 0)
        {
            return CommerceErrors.Invalid("KrafterInvalid", errors);
        }

        Name = name;
        Role = role;
        Bio = bio;
        Instagram = instagram;

        return Result.Success;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Za-z0-9._]{1,30}$")]
    private static partial Regex InstagramHandle();
}

/// <summary>A Kraft's place at one event: the platform's commission on its sales there and where its stall is.</summary>
public sealed class KraftParticipation : AuditableEntity<KraftParticipationId>
{
    public const int StallMaxLength = 60;
    public const decimal MaxCommissionPercent = 100m;

    private KraftParticipation(KraftParticipationId id)
        : base(id)
    {
    }

    public Guid EventId { get; private set; }

    /// <summary>Platform commission on the Kraft's food and drink sales at this event, 0-100 with two decimals.</summary>
    public decimal CommissionPercent { get; private set; }

    /// <summary>Where to find the stall ("Courtyard, stall 4").</summary>
    public string? Stall { get; private set; }

    /// <summary>Own till or the platform's till for this event.</summary>
    public PaymentRouting PaymentRouting { get; private set; } = PaymentRouting.OwnTill;

    public bool PaysIntoPlatformTill => PaymentRouting == PaymentRouting.PlatformTill;

    internal static Result<string?> CheckTerms(decimal commissionPercent, string? stall)
    {
        var errors = new List<string>();

        if (commissionPercent is < 0m or > MaxCommissionPercent || decimal.Round(commissionPercent, 2) != commissionPercent)
        {
            errors.Add("CommissionPercent: Use a percentage between 0 and 100 with at most two decimals.");
        }

        var trimmed = string.IsNullOrWhiteSpace(stall) ? null : stall.Trim();
        if (trimmed is { Length: > StallMaxLength })
        {
            errors.Add($"Stall: Keep it under {StallMaxLength} characters.");
        }

        return errors.Count > 0 ? CommerceErrors.Invalid("KraftEventInvalid", errors) : trimmed;
    }

    internal static KraftParticipation Create(KraftParticipationId id, Guid eventId, decimal commissionPercent, string? stall, PaymentRouting routing) =>
        new(id) { EventId = eventId, CommissionPercent = commissionPercent, Stall = stall, PaymentRouting = routing };

    internal void Change(decimal commissionPercent, string? stall, PaymentRouting routing)
    {
        CommissionPercent = commissionPercent;
        Stall = stall;
        PaymentRouting = routing;
    }
}
