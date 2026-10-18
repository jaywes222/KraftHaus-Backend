using Common.Domain.ResultMonad;
using Displays.Domain.Assets;
using Displays.Domain.Common;
using Displays.Domain.Configurations;
using Displays.Domain.Devices;
using Displays.Domain.Programme;

namespace Displays.Application.Abstractions;

public interface IDisplayDeviceRepository
{
    Task<DisplayDevice?> FindAsync(DisplayDeviceId id, CancellationToken cancellationToken);

    Task<DisplayDevice?> FindForReadAsync(DisplayDeviceId id, CancellationToken cancellationToken);

    /// <summary>The active screen holding this token (by hash), for authentication.</summary>
    Task<DisplayDevice?> FindActiveByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<DisplayDevice>> ListAsync(Guid? eventId, DisplayDeviceStatus? status, CancellationToken cancellationToken);

    void Add(DisplayDevice device);

    /// <summary>Records that the screen is alive, at most once per <paramref name="minInterval" /> (a plain UPDATE, no version bump).</summary>
    Task TouchAsync(DisplayDeviceId id, DateTimeOffset now, TimeSpan minInterval, CancellationToken cancellationToken);
}

public interface IDisplayPairingRepository
{
    Task<DisplayPairing?> FindAsync(DisplayPairingId id, CancellationToken cancellationToken);

    Task<DisplayPairing?> FindWaitingByCodeAsync(string code, DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> CodeInUseAsync(string code, DateTimeOffset now, CancellationToken cancellationToken);

    void Add(DisplayPairing pairing);

    /// <summary>Deletes requests that ended (expired, or delivered) before <paramref name="before" />.</summary>
    Task<int> DeleteFinishedAsync(DateTimeOffset before, CancellationToken cancellationToken);
}

public interface IDisplayConfigurationRepository
{
    Task<DisplayConfiguration?> FindAsync(DisplayConfigurationId id, CancellationToken cancellationToken);

    Task<DisplayConfiguration?> FindForReadAsync(DisplayConfigurationId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<DisplayConfiguration>> ListAsync(Guid? eventId, bool includeArchived, CancellationToken cancellationToken);

    void Add(DisplayConfiguration configuration);
}

public interface IProgrammeRepository
{
    Task<ProgrammeSlot?> FindAsync(ProgrammeSlotId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProgrammeSlot>> ListForEventAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>Slots not yet over at <paramref name="now" />, by start time.</summary>
    Task<IReadOnlyList<ProgrammeSlot>> ListUpcomingAsync(Guid eventId, DateTimeOffset now, int limit, CancellationToken cancellationToken);

    void Add(ProgrammeSlot slot);

    void Remove(ProgrammeSlot slot);
}

public interface IDisplayAssetRepository
{
    Task<DisplayAsset?> FindAsync(DisplayAssetId id, CancellationToken cancellationToken);

    Task<DisplayAsset?> FindForReadAsync(DisplayAssetId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<DisplayAsset>> ListAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<DisplayAsset>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    void Add(DisplayAsset asset);

    void Remove(DisplayAsset asset);
}

public interface IDisplaysUnitOfWork
{
    /// <summary>Saves; a lost optimistic-concurrency race comes back as <see cref="DisplaysErrors.ConcurrentUpdate" />.</summary>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Image files on the server's disk, per tenant (TD: uploads on the VM, included in backups).</summary>
public interface IAssetStorage
{
    /// <returns>The path relative to the storage root, to keep on the asset row.</returns>
    Task<string> SaveAsync(Guid assetId, string extension, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    /// <summary>Null when the file is missing.</summary>
    Stream? OpenRead(string relativePath);

    void Delete(string relativePath);
}

/// <summary>Live nudges to screens (SignalR). Screens re-fetch their feed when told; they also poll.</summary>
public interface IDisplayRealtime
{
    Task DeviceChangedAsync(Guid deviceId, CancellationToken cancellationToken);

    Task EventChangedAsync(Guid eventId, string reason, CancellationToken cancellationToken);

    Task DeviceRevokedAsync(Guid deviceId, CancellationToken cancellationToken);
}

/// <summary>Encrypts the device token for the short time it waits for the screen to pick it up.</summary>
public interface IPairingTokenProtector
{
    string Protect(string token);

    string? Unprotect(string protectedToken);
}

/// <summary>Forgets cached token lookups when a screen is revoked, so the token stops working at once.</summary>
public interface IDeviceTokenCache
{
    void Evict(string tokenHash);
}
