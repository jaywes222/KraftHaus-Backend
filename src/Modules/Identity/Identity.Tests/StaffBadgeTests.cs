using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Identity.Domain.Badges;
using Identity.Domain.Common;
using Xunit;

namespace Identity.Tests;

public sealed class StaffBadgeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    private static StaffBadge NewBadge() =>
        StaffBadge.Issue(new StaffBadgeId(Guid.CreateVersion7()), new TenantId(Guid.CreateVersion7()), new ApplicationUserId(Guid.CreateVersion7()), "ABCDEFGH23456789", Now);

    [Fact]
    public void ReissueChangesTheCode()
    {
        var badge = NewBadge();

        badge.Reissue("ZZZZEFGH23456789", Now.AddDays(1));

        Assert.Equal("ZZZZEFGH23456789", badge.Code);
        Assert.Equal(1, badge.TimesReissued);
        Assert.Contains(badge.Events, x => x is StaffBadgeIssued { Reissued: true });
    }

    [Fact]
    public void ReplacingThePhotoBumpsTheVersionAndReturnsTheOldPath()
    {
        var badge = NewBadge();

        Assert.Null(badge.ReplacePhoto("t/a-1.jpg", "image/jpeg"));
        Assert.Equal("t/a-1.jpg", badge.ReplacePhoto("t/a-2.png", "image/png"));
        Assert.Equal(2, badge.PhotoVersion);
    }

    [Fact]
    public void TitleIsTrimmedAndLimited()
    {
        var badge = NewBadge();

        Assert.False(badge.ChangeTitle("  Head of Security ").IsFailure);
        Assert.Equal("Head of Security", badge.Title);
        Assert.True(badge.ChangeTitle(new string('x', StaffBadge.TitleMaxLength + 1)).IsFailure);
    }
}
