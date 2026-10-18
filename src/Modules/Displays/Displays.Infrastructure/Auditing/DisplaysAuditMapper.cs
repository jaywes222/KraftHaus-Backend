using Displays.Domain.Gallery;
using System.Globalization;
using Common.Application.Auditing;
using Common.Domain.Aggregates;
using Common.Domain.Events;
using Displays.Domain.Assets;
using Displays.Domain.Configurations;
using Displays.Domain.Devices;
using Displays.Domain.Programme;

namespace Displays.Infrastructure.Auditing;

/// <summary>Displays domain events → audit actions (<c>displays.device.*</c>, <c>displays.configuration.*</c>, …).</summary>
public static class DisplaysAuditMapper
{
    public const string Module = "displays";

    public static (string Action, AuditTarget Target, IReadOnlyDictionary<string, string> Metadata) Map(
        DomainEvent domainEvent,
        IAggregateRoot aggregate)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(aggregate);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        var (action, targetType) = domainEvent switch
        {
            DisplayDevicePaired paired => (Device("displays.device.paired", paired.EventId, paired.DefaultConfigurationId), "display_device"),
            DisplayDeviceUpdated updated => (Device("displays.device.updated", updated.EventId, updated.DefaultConfigurationId), "display_device"),
            DisplayScheduleSet schedule => (With("displays.device.schedule_set", "entries", schedule.Entries.ToString(CultureInfo.InvariantCulture)), "display_device"),
            DisplayDeviceRevoked revoked => (With("displays.device.revoked", "reason", revoked.Reason), "display_device"),
            DisplayConfigurationCreated created => (Created(created), "display_configuration"),
            DisplayConfigurationUpdated updated => (With("displays.configuration.updated", "slides", updated.Slides.ToString(CultureInfo.InvariantCulture)), "display_configuration"),
            DisplayConfigurationArchived => ("displays.configuration.archived", "display_configuration"),
            ProgrammeSlotAdded added => (Slot("displays.programme.slot_added", added.StartsAt, added.EventId), "programme_slot"),
            ProgrammeSlotUpdated updated => (Slot("displays.programme.slot_updated", updated.StartsAt, null), "programme_slot"),
            ProgrammeSlotRemoved removed => (With("displays.programme.slot_removed", "eventId", Id(removed.EventId)), "programme_slot"),
            DisplayAssetUploaded uploaded => (Uploaded(uploaded), "display_asset"),
            DisplayAssetDeleted => ("displays.asset.deleted", "display_asset"),
            GalleryPhotoAdded => ("displays.gallery.photo_added", "gallery_photo"),
            GalleryPhotoChanged => ("displays.gallery.photo_changed", "gallery_photo"),
            GalleryPhotoRemoved => ("displays.gallery.photo_removed", "gallery_photo"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(domainEvent),
                domainEvent.GetType().Name,
                "Every Displays domain event needs a stable audit action.")
        };

        return (action, new AuditTarget(targetType, aggregate.Id.Value.ToString("D", CultureInfo.InvariantCulture)), metadata);

        string With(string name, string key, string value)
        {
            metadata[key] = value;
            return name;
        }

        string Device(string name, Guid eventId, Guid? configurationId)
        {
            metadata["eventId"] = Id(eventId);
            if (configurationId is { } id)
            {
                metadata["configurationId"] = Id(id);
            }

            return name;
        }

        string Created(DisplayConfigurationCreated created)
        {
            metadata["eventId"] = Id(created.EventId);
            metadata["slides"] = created.Slides.ToString(CultureInfo.InvariantCulture);
            return "displays.configuration.created";
        }

        string Slot(string name, DateTimeOffset startsAt, Guid? eventId)
        {
            metadata["startsAt"] = startsAt.ToString("O", CultureInfo.InvariantCulture);
            if (eventId is { } id)
            {
                metadata["eventId"] = Id(id);
            }

            return name;
        }

        string Uploaded(DisplayAssetUploaded uploaded)
        {
            metadata["contentType"] = uploaded.ContentType;
            metadata["sizeBytes"] = uploaded.SizeBytes.ToString(CultureInfo.InvariantCulture);
            return "displays.asset.uploaded";
        }
    }

    private static string Id(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);
}
