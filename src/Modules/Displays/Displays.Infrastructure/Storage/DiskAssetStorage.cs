using Common.Application.Tenancy;
using Displays.Application;
using Displays.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Displays.Infrastructure.Storage;

/// <summary>
///     Images under <c>Displays:AssetsRootPath</c> (relative paths are under the API's content root), one folder per
///     tenant: <c>{root}/{tenantId}/{assetId}.{ext}</c>. Paths come from ids only, never from uploaded names. Back this
///     folder up with the databases.
/// </summary>
internal sealed class DiskAssetStorage(IOptions<DisplaysOptions> options, IHostEnvironment environment, ICurrentTenant tenant) : IAssetStorage
{
    public async Task<string> SaveAsync(Guid assetId, string extension, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var relative = Path.Combine(TenantFolder(), $"{assetId:N}{extension}");
        var full = Resolve(relative);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, content.ToArray(), cancellationToken);

        return relative.Replace('\\', '/');
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

    private string TenantFolder() =>
        tenant.IsResolved ? tenant.TenantId.Value.ToString("N") : "_";

    private string Resolve(string relativePath)
    {
        var configured = options.Value.AssetsRootPath;
        var root = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured));
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Asset path escapes the storage root.");
        }

        return full;
    }
}
