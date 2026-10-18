using Common.Application.Music;
using Common.Application.Commerce;
using Common.Application.Displays;
using Common.Application.EventCatalog;
using Common.Domain.ResultMonad;
using Displays.Application.Abstractions;
using Displays.Application.Assets;
using Displays.Domain.Common;
using Displays.Domain.Configurations;
using Displays.Domain.Devices;
using Microsoft.Extensions.Options;

namespace Displays.Application.Feed;

public sealed record FeedDeviceView(Guid Id, string Name, string? Location);

public sealed record FeedEventView(Guid Id, string Title, string Slug, string? VenueName);

public sealed record FeedProgrammeItem(string Title, string? Who, string? Stage, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public sealed record FeedSchedule(IReadOnlyList<FeedProgrammeItem> Now, IReadOnlyList<FeedProgrammeItem> Next);

/// <param name="ImageUrl">Relative URL of the slide's image on this host, when it has one.</param>
public sealed record FeedSlide(
    Guid Id,
    string Kind,
    int DurationSeconds,
    string? Title,
    string? Body,
    Uri? ImageUrl,
    FeedSchedule? Schedule,
    IReadOnlyList<MenuBoardKraft>? Menu,
    IReadOnlyList<ReadyOrder>? ReadyOrders,
    MusicBoard? Music = null);

/// <summary>
///     Everything a screen renders, resolved for <see cref="ServerTime" />. <see cref="Configuration" /> null means
///     nothing is scheduled: show the "no content" screen. Re-fetch after <see cref="RefreshAfterSeconds" />, at
///     <see cref="NextChangeAt" />, or when the hub says <c>refresh</c>.
/// </summary>
public sealed record DisplayFeedView(
    FeedDeviceView Device,
    FeedEventView? Event,
    DateTimeOffset ServerTime,
    int RefreshAfterSeconds,
    DateTimeOffset? NextChangeAt,
    Guid? ConfigurationId,
    string? Configuration,
    IReadOnlyList<FeedSlide> Slides);

/// <summary>A paired screen's feed (UF-019): its current configuration with every slide's content filled in.</summary>
public sealed class FeedService(
    IDisplayDeviceRepository devices,
    IDisplayConfigurationRepository configurations,
    IProgrammeRepository programme,
    ICommerceBoards boards,
    IEnumerable<IMusicBoard> music,
    IEventCatalog events,
    IOptions<DisplaysOptions> options,
    TimeProvider time)
{
    public async Task<Result<DisplayFeedView>> GetAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var id = new DisplayDeviceId(deviceId);
        var device = await devices.FindForReadAsync(id, cancellationToken);
        if (device is null || device.Status != DisplayDeviceStatus.Active)
        {
            return DisplaysErrors.DeviceNotFound;
        }

        var now = time.GetUtcNow();
        await devices.TouchAsync(id, now, TimeSpan.FromSeconds(settings.SeenWriteIntervalSeconds), cancellationToken);

        var sale = await events.GetSaleSnapshotAsync(device.EventId, cancellationToken);
        var eventView = sale is null ? null : new FeedEventView(sale.EventId, sale.Title, sale.Slug, sale.VenueName);

        var configuration = device.ConfigurationAt(now) is { } configurationId
            ? await configurations.FindForReadAsync(new DisplayConfigurationId(configurationId), cancellationToken)
            : null;
        if (configuration is { Status: not DisplayConfigurationStatus.Active })
        {
            configuration = null;
        }

        var slides = configuration is null ? [] : await ResolveAsync(configuration, device.EventId, now, settings, cancellationToken);

        return new DisplayFeedView(
            new FeedDeviceView(device.Id.Value, device.Name, device.Location),
            eventView,
            now,
            settings.FeedRefreshSeconds,
            device.NextChangeAfter(now),
            configuration?.Id.Value,
            configuration?.Name,
            slides);
    }

    private async Task<List<FeedSlide>> ResolveAsync(
        DisplayConfiguration configuration,
        Guid eventId,
        DateTimeOffset now,
        DisplaysOptions settings,
        CancellationToken cancellationToken)
    {
        FeedSchedule? schedule = null;
        var slides = new List<FeedSlide>();

        foreach (var slide in configuration.Slides)
        {
            var image = slide.AssetId is { } assetId ? AssetUrls.For(assetId) : null;

            switch (slide.Kind)
            {
                case SlideKind.Schedule:
                    schedule ??= await ScheduleAsync(eventId, now, settings.ScheduleNextCount, cancellationToken);
                    slides.Add(Slide(slide, image) with { Schedule = schedule });
                    break;

                case SlideKind.Menu:
                    slides.Add(Slide(slide, image) with { Menu = await boards.GetMenuAsync(eventId, slide.KraftIds, cancellationToken) });
                    break;

                case SlideKind.OrdersReady:
                    slides.Add(Slide(slide, image) with
                    {
                        ReadyOrders = await boards.GetReadyOrdersAsync(eventId, slide.KraftIds, settings.ReadyOrdersLimit, cancellationToken)
                    });
                    break;

                case SlideKind.DjBooth:
                    var booth = music.FirstOrDefault();
                    slides.Add(Slide(slide, image) with
                    {
                        Music = booth is null ? null : await booth.GetAsync(eventId, settings.DjQueueCount, cancellationToken)
                    });
                    break;

                default:
                    slides.Add(Slide(slide, image));
                    break;
            }
        }

        return slides;
    }

    private async Task<FeedSchedule> ScheduleAsync(Guid eventId, DateTimeOffset now, int nextCount, CancellationToken cancellationToken)
    {
        var upcoming = await programme.ListUpcomingAsync(eventId, now, 50, cancellationToken);

        return new FeedSchedule(
            [.. upcoming.Where(x => x.StartsAt <= now).Select(Item)],
            [.. upcoming.Where(x => x.StartsAt > now).Take(nextCount).Select(Item)]);

        static FeedProgrammeItem Item(Domain.Programme.ProgrammeSlot x) => new(x.Title, x.Who, x.Stage, x.StartsAt, x.EndsAt);
    }

    private static FeedSlide Slide(DisplaySlide slide, Uri? image) =>
        new(slide.Id, slide.Kind.ToString(), slide.DurationSeconds, slide.Title, slide.Body, image, null, null, null);
}

/// <summary><see cref="IDisplayBoardSignal" />: Commerce changes → screens of that event re-fetch.</summary>
public sealed class DisplayBoardSignal(IDisplayRealtime realtime) : IDisplayBoardSignal
{
    public Task OrdersChangedAsync(Guid eventId, CancellationToken cancellationToken) =>
        realtime.EventChangedAsync(eventId, "orders", cancellationToken);

    public Task MenuChangedAsync(Guid eventId, CancellationToken cancellationToken) =>
        realtime.EventChangedAsync(eventId, "menu", cancellationToken);

    public Task MusicChangedAsync(Guid eventId, CancellationToken cancellationToken) =>
        realtime.EventChangedAsync(eventId, "music", cancellationToken);
}
