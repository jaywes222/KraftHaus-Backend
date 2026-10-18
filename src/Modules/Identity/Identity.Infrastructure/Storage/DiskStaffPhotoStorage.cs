using Common.Domain.Tenancy;
using Identity.Application.Badges;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Storage;

/// <summary>
///     Staff photos under <c>Identity:StaffBadges:PhotosRootPath</c>: <c>{root}/{tenantId}/{badgeId}-{version}.{ext}</c>.
///     Paths come from ids only; a path that would leave the root is refused.
/// </summary>
internal sealed class DiskStaffPhotoStorage(IOptions<StaffBadgeOptions> options, IHostEnvironment environment) : IStaffPhotoStorage
{
    public async Task<string> SaveAsync(TenantId tenantId, Guid badgeId, int version, string extension, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var relative = $"{tenantId.Value:N}/{badgeId:N}-{version}{extension}";
        var full = Resolve(relative);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, content.ToArray(), cancellationToken);

        return relative;
    }

    public Stream? OpenRead(string relativePath)
    {
        var full = Resolve(relativePath);

        return File.Exists(full) ? new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null;
    }

    public void Delete(string relativePath)
    {
        var full = Resolve(relativePath);
        if (File.Exists(full))
        {
            File.Delete(full);
        }
    }

    private string Resolve(string relativePath)
    {
        var configured = options.Value.PhotosRootPath;
        var root = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured));
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Photo path escapes the storage root.");
        }

        return full;
    }
}
