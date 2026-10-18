using Commerce.Application;
using Commerce.Application.Images;
using Common.Application.Tenancy;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Commerce.Infrastructure.Storage;

/// <summary><c>{Commerce:ImagesRootPath}/{tenantId}/{ownerId}-{version}.{ext}</c>. A name that would leave the folder is refused.</summary>
internal sealed class DiskCommerceImageStorage(IOptions<CommerceOptions> options, IHostEnvironment environment, ICurrentTenant tenant) : ICommerceImageStorage
{
    public async Task<string> SaveAsync(Guid ownerId, int version, string extension, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var fileName = $"{ownerId:N}-{version}{extension}";
        var full = Resolve(fileName);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, content.ToArray(), cancellationToken);

        return fileName;
    }

    public Stream? OpenRead(string fileName)
    {
        var full = Resolve(fileName);
        return File.Exists(full) ? new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null;
    }

    public void Delete(string fileName)
    {
        var full = Resolve(fileName);
        if (File.Exists(full))
        {
            File.Delete(full);
        }
    }

    private string Resolve(string fileName)
    {
        var configured = options.Value.ImagesRootPath;
        var root = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured));
        var folder = Path.Combine(root, tenant.IsResolved ? tenant.TenantId.Value.ToString("N") : "_");
        var full = Path.GetFullPath(Path.Combine(folder, fileName));

        if (!full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Image path escapes the storage root.");
        }

        return full;
    }
}
