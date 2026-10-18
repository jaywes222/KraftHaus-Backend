using Displays.Domain.Common;
using Displays.Domain.Gallery;
using Xunit;

namespace Displays.Tests;

public sealed class GalleryPhotoTests
{
    [Fact]
    public void CaptionIsTrimmedAndLimited()
    {
        var photo = GalleryPhoto.Add(new GalleryPhotoId(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(), "  Golden hour ", 0, DateTimeOffset.UnixEpoch).Value!;

        Assert.Equal("Golden hour", photo.Caption);
        Assert.True(photo.Change(new string('x', GalleryPhoto.CaptionMaxLength + 1), 0).IsFailure);
        Assert.True(photo.Change(null, -1).IsFailure);
    }
}
