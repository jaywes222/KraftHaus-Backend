using Common.Domain.StronglyTypedIds;

namespace Displays.Domain.Common;

public readonly record struct DisplayDeviceId(Guid Value) : IStronglyTypedId;

public readonly record struct DisplayScheduleEntryId(Guid Value) : IStronglyTypedId;

public readonly record struct DisplayPairingId(Guid Value) : IStronglyTypedId;

public readonly record struct DisplayConfigurationId(Guid Value) : IStronglyTypedId;

public readonly record struct DisplaySlideId(Guid Value) : IStronglyTypedId;

public readonly record struct ProgrammeSlotId(Guid Value) : IStronglyTypedId;

public readonly record struct DisplayAssetId(Guid Value) : IStronglyTypedId;

public readonly record struct GalleryPhotoId(Guid Value) : IStronglyTypedId;
