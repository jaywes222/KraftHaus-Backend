using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Displays.Domain.Common;

namespace Displays.Domain.Configurations;

/// <summary>What a slide shows (Spec 01 / SURFACES.md, display surfaces).</summary>
public enum SlideKind
{
    /// <summary>What's on now and next, from the programme.</summary>
    Schedule = 1,

    /// <summary>Items by Kraft with prices and sold-out flags.</summary>
    Menu = 2,

    /// <summary>Order numbers ready for collection, with the Kraft to collect from.</summary>
    OrdersReady = 3,

    /// <summary>A short message, optionally with an image.</summary>
    Announcement = 4,

    /// <summary>A sponsor image.</summary>
    Sponsor = 5,

    /// <summary>The DJ booth: up next, the most requested songs, just played (song requests).</summary>
    DjBooth = 6
}

public enum DisplayConfigurationStatus
{
    Active = 1,
    Archived = 2
}

/// <param name="KraftIds">Menu and order-ready slides: only these Krafts (empty: all Krafts at the event).</param>
public sealed record DisplaySlide(
    Guid Id,
    SlideKind Kind,
    int DurationSeconds,
    string? Title,
    string? Body,
    Guid? AssetId,
    IReadOnlyList<Guid> KraftIds);

public sealed record DisplaySlideSpec(
    SlideKind Kind,
    int DurationSeconds,
    string? Title,
    string? Body,
    Guid? AssetId,
    IReadOnlyList<Guid>? KraftIds);

/// <summary>
///     A playlist for screens at one event: slides shown in order, each for its duration, then round again. Screens point
///     at a configuration as their default or through their time schedule.
/// </summary>
public sealed class DisplayConfiguration : AggregateRoot<DisplayConfigurationId>
{
    public const int NameMaxLength = 60;
    public const int TitleMaxLength = 80;
    public const int BodyMaxLength = 400;
    public const int MaxSlides = 20;
    public const int MinDurationSeconds = 5;
    public const int MaxDurationSeconds = 300;
    public const int MaxKraftsPerSlide = 50;

    private List<DisplaySlide> _slides = [];

    private DisplayConfiguration(DisplayConfigurationId id)
        : base(id)
    {
    }

    public Guid EventId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public DisplayConfigurationStatus Status { get; private set; }

    public IReadOnlyList<DisplaySlide> Slides => _slides;

    public static Result<DisplayConfiguration> Create(
        DisplayConfigurationId id,
        Guid eventId,
        string? name,
        IReadOnlyList<DisplaySlideSpec> slides,
        Func<Guid> newId)
    {
        if (eventId == Guid.Empty)
        {
            return DisplaysErrors.Invalid("ConfigurationInvalid", ["EventId: Choose the event."]);
        }

        var configuration = new DisplayConfiguration(id) { EventId = eventId, Status = DisplayConfigurationStatus.Active };
        var applied = configuration.Apply(name, slides, newId);
        if (applied.IsFailure)
        {
            return applied.Error!;
        }

        configuration.RaiseEvent(new DisplayConfigurationCreated(id, eventId, configuration._slides.Count));

        return configuration;
    }

    public Result Update(string? name, IReadOnlyList<DisplaySlideSpec> slides, Func<Guid> newId)
    {
        if (Status == DisplayConfigurationStatus.Archived)
        {
            return ArchivedError;
        }

        var applied = Apply(name, slides, newId);
        if (applied.IsFailure)
        {
            return applied;
        }

        RaiseEvent(new DisplayConfigurationUpdated(Id, _slides.Count));

        return Result.Success;
    }

    public Result Archive()
    {
        if (Status == DisplayConfigurationStatus.Archived)
        {
            return ArchivedError;
        }

        Status = DisplayConfigurationStatus.Archived;
        RaiseEvent(new DisplayConfigurationArchived(Id));

        return Result.Success;
    }

    public bool UsesAsset(Guid assetId) => _slides.Exists(x => x.AssetId == assetId);

    private static Error ArchivedError =>
        DisplaysErrors.Conflict("ConfigurationArchived", "This configuration has been archived.");

    private Result Apply(string? name, IReadOnlyList<DisplaySlideSpec>? slides, Func<Guid> newId)
    {
        ArgumentNullException.ThrowIfNull(newId);

        var errors = new List<string>();
        var trimmed = name?.Trim() ?? string.Empty;
        var specs = slides ?? [];

        if (trimmed.Length is < 2 or > NameMaxLength)
        {
            errors.Add($"Name: Use 2-{NameMaxLength} characters.");
        }

        if (specs.Count == 0)
        {
            errors.Add("Slides: Add at least one slide.");
        }
        else if (specs.Count > MaxSlides)
        {
            errors.Add($"Slides: At most {MaxSlides} slides.");
        }

        var built = new List<DisplaySlide>();
        for (var i = 0; i < specs.Count; i++)
        {
            var slide = Validate(specs[i], $"Slides[{i}]", errors);
            if (slide is not null)
            {
                built.Add(slide with { Id = newId() });
            }
        }

        if (errors.Count > 0)
        {
            return DisplaysErrors.Invalid("ConfigurationInvalid", errors);
        }

        Name = trimmed;
        _slides = built;

        return Result.Success;
    }

    private static DisplaySlide? Validate(DisplaySlideSpec? spec, string field, List<string> errors)
    {
        if (spec is null)
        {
            errors.Add($"{field}: Describe the slide.");
            return null;
        }

        var before = errors.Count;
        var title = string.IsNullOrWhiteSpace(spec.Title) ? null : spec.Title.Trim();
        var body = string.IsNullOrWhiteSpace(spec.Body) ? null : spec.Body.Trim();
        var krafts = (spec.KraftIds ?? []).Distinct().ToList();

        if (!Enum.IsDefined(spec.Kind))
        {
            errors.Add($"{field}.Kind: Choose Schedule, Menu, OrdersReady, Announcement or Sponsor.");
        }

        if (spec.DurationSeconds is < MinDurationSeconds or > MaxDurationSeconds)
        {
            errors.Add($"{field}.DurationSeconds: Use {MinDurationSeconds}-{MaxDurationSeconds} seconds.");
        }

        if (title is { Length: > TitleMaxLength })
        {
            errors.Add($"{field}.Title: Keep it under {TitleMaxLength} characters.");
        }

        if (body is { Length: > BodyMaxLength })
        {
            errors.Add($"{field}.Body: Keep it under {BodyMaxLength} characters.");
        }

        if (krafts.Contains(Guid.Empty) || krafts.Count > MaxKraftsPerSlide)
        {
            errors.Add($"{field}.KraftIds: Use up to {MaxKraftsPerSlide} Kraft ids.");
        }

        if (spec.AssetId == Guid.Empty)
        {
            errors.Add($"{field}.AssetId: Choose an image or leave it empty.");
        }

        switch (spec.Kind)
        {
            case SlideKind.Announcement when title is null && body is null:
                errors.Add($"{field}.Title: An announcement needs a title or a message.");
                break;
            case SlideKind.Sponsor when spec.AssetId is null:
                errors.Add($"{field}.AssetId: A sponsor slide needs an image.");
                break;
            case SlideKind.Schedule or SlideKind.Sponsor or SlideKind.DjBooth when krafts.Count > 0:
                errors.Add($"{field}.KraftIds: Only menu and order-ready slides filter by Kraft.");
                break;
        }

        return errors.Count > before
            ? null
            : new DisplaySlide(Guid.Empty, spec.Kind, spec.DurationSeconds, title, body, spec.AssetId, krafts);
    }
}
