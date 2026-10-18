using Displays.Application.Assets;
using Displays.Application.Devices;
using Displays.Domain.Configurations;
using Displays.Domain.Devices;
using Displays.Domain.Programme;
using Displays.Domain.Common;
using Xunit;

namespace Displays.Tests;

public sealed class DisplayRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid EventId = Guid.CreateVersion7();

    [Fact]
    public void AScreenShowsItsScheduleEntryAndOtherwiseItsDefault()
    {
        var defaultConfiguration = Guid.CreateVersion7();
        var afternoon = Guid.CreateVersion7();
        var device = Device(defaultConfiguration);

        Assert.False(device.SetSchedule([(afternoon, Now.AddHours(1), Now.AddHours(3))], Guid.CreateVersion7).IsFailure);

        Assert.Equal(defaultConfiguration, device.ConfigurationAt(Now));
        Assert.Equal(afternoon, device.ConfigurationAt(Now.AddHours(1)));
        Assert.Equal(afternoon, device.ConfigurationAt(Now.AddHours(3).AddTicks(-1)));
        Assert.Equal(defaultConfiguration, device.ConfigurationAt(Now.AddHours(3)));
        Assert.Equal(Now.AddHours(1), device.NextChangeAfter(Now));
        Assert.Equal(Now.AddHours(3), device.NextChangeAfter(Now.AddHours(2)));
        Assert.Null(device.NextChangeAfter(Now.AddHours(4)));
    }

    [Fact]
    public void WithNoDefaultAndNothingScheduledAScreenShowsNoContent()
    {
        var device = Device(null);

        Assert.Null(device.ConfigurationAt(Now));
    }

    [Fact]
    public void ScheduleEntriesMayNotOverlap()
    {
        var device = Device(null);
        var one = Guid.CreateVersion7();

        var result = device.SetSchedule([(one, Now, Now.AddHours(2)), (one, Now.AddHours(1), Now.AddHours(3))], Guid.CreateVersion7);

        Assert.Equal("Displays.ScheduleInvalid", result.Error!.Key);
        Assert.Contains(result.Error.SubErrors!, x => x.Contains("overlap", StringComparison.Ordinal));
    }

    [Fact]
    public void BackToBackEntriesAreFine()
    {
        var device = Device(null);
        var one = Guid.CreateVersion7();

        Assert.False(device.SetSchedule([(one, Now.AddHours(1), Now.AddHours(2)), (one, Now, Now.AddHours(1))], Guid.CreateVersion7).IsFailure);
        Assert.Equal(Now, device.Schedule[0].StartsAt);
    }

    [Fact]
    public void ARevokedScreenIsOfflineAndCannotChange()
    {
        var device = Device(null);

        Assert.True(device.IsOnline(Now, TimeSpan.FromSeconds(90)));
        Assert.Equal("Displays.RevokeInvalid", device.Revoke(" ", Guid.CreateVersion7(), Now).Error!.Key);
        Assert.False(device.Revoke("Stolen", Guid.CreateVersion7(), Now).IsFailure);

        Assert.False(device.IsOnline(Now, TimeSpan.FromSeconds(90)));
        Assert.Equal("Displays.DeviceRevoked", device.Update(new DisplayDeviceSpec("TV", null, EventId, null)).Error!.Key);
        Assert.Equal("Displays.DeviceRevoked", device.Revoke("Again", Guid.CreateVersion7(), Now).Error!.Key);
    }

    [Fact]
    public void AScreenGoesOfflineAfterTheWindow()
    {
        var device = Device(null);

        Assert.True(device.IsOnline(Now.AddSeconds(90), TimeSpan.FromSeconds(90)));
        Assert.False(device.IsOnline(Now.AddSeconds(91), TimeSpan.FromSeconds(90)));
    }

    [Fact]
    public void MovingAScreenToAnotherEventClearsItsSchedule()
    {
        var device = Device(null);
        Assert.False(device.SetSchedule([(Guid.CreateVersion7(), Now, Now.AddHours(1))], Guid.CreateVersion7).IsFailure);

        Assert.False(device.Update(new DisplayDeviceSpec("Main bar TV", "Bar", Guid.CreateVersion7(), null)).IsFailure);

        Assert.Empty(device.Schedule);
    }

    [Fact]
    public void SlidesAreCheckedByKind()
    {
        var result = DisplayConfiguration.Create(
            new DisplayConfigurationId(Guid.CreateVersion7()),
            EventId,
            "Main bar",
            [
                new DisplaySlideSpec(SlideKind.Announcement, 10, null, null, null, null),
                new DisplaySlideSpec(SlideKind.Sponsor, 10, "Thanks", null, null, null),
                new DisplaySlideSpec(SlideKind.Schedule, 2, null, null, null, [Guid.CreateVersion7()]),
                new DisplaySlideSpec(SlideKind.Menu, 10, null, null, Guid.Empty, null)
            ],
            Guid.CreateVersion7);

        var errors = result.Error!.SubErrors!;
        Assert.Contains(errors, x => x.StartsWith("Slides[0].Title", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.StartsWith("Slides[1].AssetId", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.StartsWith("Slides[2].DurationSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.StartsWith("Slides[2].KraftIds", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.StartsWith("Slides[3].AssetId", StringComparison.Ordinal));
    }

    [Fact]
    public void AValidPlaylistKeepsItsOrderAndKnowsItsImages()
    {
        var logo = Guid.CreateVersion7();
        var kraft = Guid.CreateVersion7();
        var configuration = DisplayConfiguration.Create(
            new DisplayConfigurationId(Guid.CreateVersion7()),
            EventId,
            " Main bar ",
            [
                new DisplaySlideSpec(SlideKind.Schedule, 15, null, null, null, null),
                new DisplaySlideSpec(SlideKind.Menu, 20, "Food", null, null, [kraft, kraft]),
                new DisplaySlideSpec(SlideKind.Sponsor, 8, null, null, logo, null)
            ],
            Guid.CreateVersion7).Value!;

        Assert.Equal("Main bar", configuration.Name);
        Assert.Equal([SlideKind.Schedule, SlideKind.Menu, SlideKind.Sponsor], configuration.Slides.Select(x => x.Kind));
        Assert.Single(configuration.Slides[1].KraftIds);
        Assert.True(configuration.UsesAsset(logo));
        Assert.All(configuration.Slides, x => Assert.NotEqual(Guid.Empty, x.Id));

        Assert.False(configuration.Archive().IsFailure);
        Assert.Equal("Displays.ConfigurationArchived", configuration.Update("x y", [], Guid.CreateVersion7).Error!.Key);
    }

    [Fact]
    public void AProgrammeSlotNeedsATitleAndASaneTime()
    {
        var bad = ProgrammeSlot.Add(
            new ProgrammeSlotId(Guid.CreateVersion7()),
            EventId,
            new ProgrammeSlotSpec(null, "x", null, null, null, Now, Now));

        Assert.Contains(bad.Error!.SubErrors!, x => x.StartsWith("Title", StringComparison.Ordinal));
        Assert.Contains(bad.Error.SubErrors!, x => x.StartsWith("EndsAt", StringComparison.Ordinal));

        var good = ProgrammeSlot.Add(
            new ProgrammeSlotId(Guid.CreateVersion7()),
            EventId,
            new ProgrammeSlotSpec(null, " Opening set ", "DJ Kace", "Courtyard", null, Now, Now.AddHours(1))).Value!;

        Assert.Equal("Opening set", good.Title);
    }

    [Fact]
    public void ImagesAreRecognisedByContentNotName()
    {
        Assert.Equal("image/jpeg", ImageSniffer.Detect([0xFF, 0xD8, 0xFF, 0xE0])!.ContentType);
        Assert.Equal("image/png", ImageSniffer.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00])!.ContentType);
        Assert.Equal("image/webp", ImageSniffer.Detect("RIFF\0\0\0\0WEBPVP8 "u8)!.ContentType);
        Assert.Null(ImageSniffer.Detect("<svg xmlns='http://www.w3.org/2000/svg'/>"u8));
        Assert.Null(ImageSniffer.Detect("GIF89a"u8));
    }

    [Fact]
    public void DeviceTokensAreLongRandomAndOnlyTheirHashIsCompared()
    {
        var token = DeviceTokens.New();

        Assert.StartsWith(DeviceTokens.Prefix, token, StringComparison.Ordinal);
        Assert.True(DeviceTokens.LooksLikeToken(token));
        Assert.False(DeviceTokens.LooksLikeToken("khd_short"));
        Assert.NotEqual(token, DeviceTokens.New());
        Assert.True(DeviceTokens.Matches(token, DeviceTokens.Hash(token)));
        Assert.False(DeviceTokens.Matches(token + "x", DeviceTokens.Hash(token)));
        Assert.False(DeviceTokens.Matches(null, DeviceTokens.Hash(token)));
    }

    private static DisplayDevice Device(Guid? defaultConfiguration) =>
        DisplayDevice.Pair(
            new DisplayDeviceId(Guid.CreateVersion7()),
            new DisplayDeviceSpec("Main bar TV", "Bar", EventId, defaultConfiguration),
            DeviceTokens.Hash(DeviceTokens.New()),
            Guid.CreateVersion7(),
            Now).Value!;
}
