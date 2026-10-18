using Displays.Application.Gallery;
using Displays.Domain.Gallery;
using Common.Domain.ResultMonad;
using Displays.Application.Abstractions;
using Displays.Domain.Assets;
using Displays.Domain.Common;
using Displays.Domain.Configurations;
using Displays.Domain.Devices;
using Displays.Domain.Programme;
using Microsoft.EntityFrameworkCore;

namespace Displays.Infrastructure.Persistence.Repositories;

internal sealed class DisplayDeviceRepository(DisplaysDbContext db) : IDisplayDeviceRepository
{
    public Task<DisplayDevice?> FindAsync(DisplayDeviceId id, CancellationToken cancellationToken) =>
        db.Devices.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<DisplayDevice?> FindForReadAsync(DisplayDeviceId id, CancellationToken cancellationToken) =>
        db.Devices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<DisplayDevice?> FindActiveByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.Devices.AsNoTracking().TagWith(nameof(FindActiveByTokenHashAsync))
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash && x.Status == DisplayDeviceStatus.Active, cancellationToken);

    public async Task<IReadOnlyList<DisplayDevice>> ListAsync(Guid? eventId, DisplayDeviceStatus? status, CancellationToken cancellationToken)
    {
        var query = db.Devices.AsNoTracking();

        if (eventId is { } filter)
        {
            query = query.Where(x => x.EventId == filter);
        }

        if (status is { } wanted)
        {
            query = query.Where(x => x.Status == wanted);
        }

        return await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public void Add(DisplayDevice device) => db.Devices.Add(device);

    public Task TouchAsync(DisplayDeviceId id, DateTimeOffset now, TimeSpan minInterval, CancellationToken cancellationToken)
    {
        var threshold = now - minInterval;

        return db.Devices
            .Where(x => x.Id == id && (x.LastSeenAt == null || x.LastSeenAt < threshold))
            .ExecuteUpdateAsync(x => x.SetProperty(d => d.LastSeenAt, now), cancellationToken);
    }
}

internal sealed class DisplayPairingRepository(DisplaysDbContext db) : IDisplayPairingRepository
{
    public Task<DisplayPairing?> FindAsync(DisplayPairingId id, CancellationToken cancellationToken) =>
        db.Pairings.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<DisplayPairing?> FindWaitingByCodeAsync(string code, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Pairings
            .Where(x => x.Code == code && x.Status == DisplayPairingStatus.Waiting && x.ExpiresAt > now)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> CodeInUseAsync(string code, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Pairings.AnyAsync(x => x.Code == code && x.Status == DisplayPairingStatus.Waiting && x.ExpiresAt > now, cancellationToken);

    public void Add(DisplayPairing pairing) => db.Pairings.Add(pairing);

    public Task<int> DeleteFinishedAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        db.Pairings
            .Where(x => x.ExpiresAt < before && x.Status != DisplayPairingStatus.Claimed)
            .ExecuteDeleteAsync(cancellationToken);
}

internal sealed class DisplayConfigurationRepository(DisplaysDbContext db) : IDisplayConfigurationRepository
{
    public Task<DisplayConfiguration?> FindAsync(DisplayConfigurationId id, CancellationToken cancellationToken) =>
        db.Configurations.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<DisplayConfiguration?> FindForReadAsync(DisplayConfigurationId id, CancellationToken cancellationToken) =>
        db.Configurations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DisplayConfiguration>> ListAsync(Guid? eventId, bool includeArchived, CancellationToken cancellationToken)
    {
        var query = db.Configurations.AsNoTracking();

        if (eventId is { } filter)
        {
            query = query.Where(x => x.EventId == filter);
        }

        if (!includeArchived)
        {
            query = query.Where(x => x.Status == DisplayConfigurationStatus.Active);
        }

        return await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public void Add(DisplayConfiguration configuration) => db.Configurations.Add(configuration);
}

internal sealed class ProgrammeRepository(DisplaysDbContext db) : IProgrammeRepository
{
    public Task<ProgrammeSlot?> FindAsync(ProgrammeSlotId id, CancellationToken cancellationToken) =>
        db.ProgrammeSlots.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ProgrammeSlot>> ListForEventAsync(Guid eventId, CancellationToken cancellationToken) =>
        await db.ProgrammeSlots.AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.StartsAt).ThenBy(x => x.Title)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProgrammeSlot>> ListUpcomingAsync(Guid eventId, DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        await db.ProgrammeSlots.AsNoTracking()
            .Where(x => x.EventId == eventId && x.EndsAt > now)
            .OrderBy(x => x.StartsAt).ThenBy(x => x.Title)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public void Add(ProgrammeSlot slot) => db.ProgrammeSlots.Add(slot);

    public void Remove(ProgrammeSlot slot) => db.ProgrammeSlots.Remove(slot);
}

internal sealed class DisplayAssetRepository(DisplaysDbContext db) : IDisplayAssetRepository
{
    public Task<DisplayAsset?> FindAsync(DisplayAssetId id, CancellationToken cancellationToken) =>
        db.Assets.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<DisplayAsset?> FindForReadAsync(DisplayAssetId id, CancellationToken cancellationToken) =>
        db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DisplayAsset>> ListAsync(int limit, CancellationToken cancellationToken) =>
        await db.Assets.AsNoTracking().OrderByDescending(x => x.UploadedAt).Take(limit).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DisplayAsset>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var keys = ids.Select(x => new DisplayAssetId(x)).ToList();

        return await db.Assets.AsNoTracking().Where(x => keys.Contains(x.Id)).ToListAsync(cancellationToken);
    }

    public void Add(DisplayAsset asset) => db.Assets.Add(asset);

    public void Remove(DisplayAsset asset) => db.Assets.Remove(asset);
}

internal sealed class DisplaysUnitOfWork(DisplaysDbContext db) : IDisplaysUnitOfWork
{
    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            return DisplaysErrors.ConcurrentUpdate;
        }
    }
}

internal sealed class GalleryRepository(DisplaysDbContext db) : IGalleryRepository
{
    public Task<GalleryPhoto?> FindAsync(GalleryPhotoId id, CancellationToken cancellationToken) =>
        db.Gallery.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<GalleryPhoto>> ListForEventAsync(Guid eventId, CancellationToken cancellationToken) =>
        await db.Gallery.AsNoTracking().Where(x => x.EventId == eventId).OrderBy(x => x.SortOrder).ThenBy(x => x.AddedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GalleryPhoto>> ListAllAsync(int limit, CancellationToken cancellationToken) =>
        await db.Gallery.AsNoTracking().OrderByDescending(x => x.AddedAt).Take(limit).ToListAsync(cancellationToken);

    public Task<bool> UsesAssetAsync(Guid assetId, CancellationToken cancellationToken) =>
        db.Gallery.AsNoTracking().AnyAsync(x => x.AssetId == assetId, cancellationToken);

    public void Add(GalleryPhoto photo) => db.Gallery.Add(photo);

    public void Remove(GalleryPhoto photo) => db.Gallery.Remove(photo);
}
