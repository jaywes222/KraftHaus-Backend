using Common.Application.Commerce;
using Common.Application.EventCatalog;
using Common.Domain.ResultMonad;
using Displays.Application.Abstractions;
using Displays.Domain.Common;
using Displays.Domain.Configurations;
using Displays.Domain.Devices;

namespace Displays.Application.Configurations;

public sealed record SlideRequest(
    SlideKind Kind,
    int DurationSeconds,
    string? Title,
    string? Body,
    Guid? AssetId,
    IReadOnlyList<Guid>? KraftIds);

public sealed record CreateConfigurationRequest(Guid EventId, string? Name, IReadOnlyList<SlideRequest>? Slides);

public sealed record UpdateConfigurationRequest(string? Name, IReadOnlyList<SlideRequest>? Slides);

public sealed record SlideView(
    Guid Id,
    string Kind,
    int DurationSeconds,
    string? Title,
    string? Body,
    Guid? AssetId,
    IReadOnlyList<Guid> KraftIds);

public sealed record DisplayConfigurationView(Guid Id, Guid EventId, string Name, string Status, IReadOnlyList<SlideView> Slides)
{
    public static DisplayConfigurationView From(DisplayConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new DisplayConfigurationView(
            configuration.Id.Value,
            configuration.EventId,
            configuration.Name,
            configuration.Status.ToString(),
            [.. configuration.Slides.Select(x => new SlideView(x.Id, x.Kind.ToString(), x.DurationSeconds, x.Title, x.Body, x.AssetId, x.KraftIds))]);
    }
}

/// <summary>Playlists for screens: which slides, in which order, for how long.</summary>
public sealed class ConfigurationService(
    IDisplayConfigurationRepository configurations,
    IDisplayDeviceRepository devices,
    IDisplayAssetRepository assets,
    IDisplaysUnitOfWork unitOfWork,
    IDisplayRealtime realtime,
    IEventCatalog events,
    IKraftDirectory krafts,
    TimeProvider time)
{
    public async Task<Result<DisplayConfigurationView>> CreateAsync(CreateConfigurationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.EventId == Guid.Empty || await events.GetSaleSnapshotAsync(request.EventId, cancellationToken) is null)
        {
            return DisplaysErrors.EventNotFound;
        }

        var slides = Specs(request.Slides);
        var references = await CheckReferencesAsync(slides, cancellationToken);
        if (references.IsFailure)
        {
            return references.Error!;
        }

        var now = time.GetUtcNow();
        var created = DisplayConfiguration.Create(
            new DisplayConfigurationId(Guid.CreateVersion7(now)),
            request.EventId,
            request.Name,
            slides,
            () => Guid.CreateVersion7(now));
        if (created.IsFailure)
        {
            return created.Error!;
        }

        configurations.Add(created.Value!);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);

        return saved.IsFailure ? saved.Error! : DisplayConfigurationView.From(created.Value!);
    }

    public async Task<Result<IReadOnlyList<DisplayConfigurationView>>> ListAsync(Guid? eventId, bool includeArchived, CancellationToken cancellationToken)
    {
        var list = await configurations.ListAsync(eventId, includeArchived, cancellationToken);

        return Result<IReadOnlyList<DisplayConfigurationView>>.Success([.. list.Select(DisplayConfigurationView.From)]);
    }

    public async Task<Result<DisplayConfigurationView>> GetAsync(Guid configurationId, CancellationToken cancellationToken)
    {
        var configuration = await configurations.FindForReadAsync(new DisplayConfigurationId(configurationId), cancellationToken);

        return configuration is null ? DisplaysErrors.ConfigurationNotFound : DisplayConfigurationView.From(configuration);
    }

    public async Task<Result<DisplayConfigurationView>> UpdateAsync(Guid configurationId, UpdateConfigurationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configuration = await configurations.FindAsync(new DisplayConfigurationId(configurationId), cancellationToken);
        if (configuration is null)
        {
            return DisplaysErrors.ConfigurationNotFound;
        }

        var slides = Specs(request.Slides);
        var references = await CheckReferencesAsync(slides, cancellationToken);
        if (references.IsFailure)
        {
            return references.Error!;
        }

        var now = time.GetUtcNow();
        var updated = configuration.Update(request.Name, slides, () => Guid.CreateVersion7(now));
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        await realtime.EventChangedAsync(configuration.EventId, "configuration", cancellationToken);

        return DisplayConfigurationView.From(configuration);
    }

    /// <summary>Refused while a screen shows it by default or through its schedule: it would go blank.</summary>
    public async Task<Result<DisplayConfigurationView>> ArchiveAsync(Guid configurationId, CancellationToken cancellationToken)
    {
        var configuration = await configurations.FindAsync(new DisplayConfigurationId(configurationId), cancellationToken);
        if (configuration is null)
        {
            return DisplaysErrors.ConfigurationNotFound;
        }

        var now = time.GetUtcNow();
        var users = (await devices.ListAsync(configuration.EventId, DisplayDeviceStatus.Active, cancellationToken))
            .Where(x => x.DefaultConfigurationId == configurationId ||
                        x.Schedule.Any(e => e.ConfigurationId == configurationId && e.EndsAt > now))
            .Select(x => x.Name)
            .ToList();
        if (users.Count > 0)
        {
            return DisplaysErrors.Conflict(
                "ConfigurationInUse",
                $"Screens still use this configuration: {string.Join(", ", users)}. Point them at another one first.");
        }

        var archived = configuration.Archive();
        if (archived.IsFailure)
        {
            return archived.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);

        return saved.IsFailure ? saved.Error! : DisplayConfigurationView.From(configuration);
    }

    private static List<DisplaySlideSpec> Specs(IReadOnlyList<SlideRequest>? slides) =>
        [.. (slides ?? []).Select(x => new DisplaySlideSpec(x.Kind, x.DurationSeconds, x.Title, x.Body, x.AssetId, x.KraftIds))];

    /// <summary>Images and Krafts named on slides must exist (an id typo would leave a slide blank on the day).</summary>
    private async Task<Result> CheckReferencesAsync(IReadOnlyList<DisplaySlideSpec> slides, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        var assetIds = slides.Where(x => x.AssetId is { } id && id != Guid.Empty).Select(x => x.AssetId!.Value).Distinct().ToList();
        if (assetIds.Count > 0)
        {
            var found = (await assets.ListByIdsAsync(assetIds, cancellationToken)).Select(x => x.Id.Value).ToHashSet();
            errors.AddRange(assetIds.Where(x => !found.Contains(x)).Select(x => $"AssetId: Image {x} was not found."));
        }

        var kraftIds = slides.SelectMany(x => x.KraftIds ?? []).Where(x => x != Guid.Empty).Distinct().ToList();
        if (kraftIds.Count > 0)
        {
            var names = await krafts.NamesAsync(kraftIds, cancellationToken);
            errors.AddRange(kraftIds.Where(x => !names.ContainsKey(x)).Select(x => $"KraftIds: Kraft {x} was not found."));
        }

        return errors.Count == 0 ? Result.Success : DisplaysErrors.Invalid("ConfigurationInvalid", errors);
    }
}
