using Common.Domain.Events;
using Displays.Domain.Common;

namespace Displays.Domain.Assets;

public sealed record DisplayAssetUploaded(DisplayAssetId AssetId, string ContentType, long SizeBytes) : DomainEvent;

public sealed record DisplayAssetDeleted(DisplayAssetId AssetId) : DomainEvent;
