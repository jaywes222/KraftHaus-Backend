using System.Globalization;
using System.Text;
using Common.Application.Auditing;
using Common.Domain.Aggregates;
using Common.Domain.Events;
using Events.Domain.DomainEvents;
using Events.Domain.Venues;

namespace Events.Infrastructure.Auditing;

/// <summary>
///     Turns Events domain events into audit actions. Action names are stable API (dashboards key off them):
///     <c>events.event.published</c>, <c>events.event.tier_withdrawn</c>, <c>events.venue.created</c>, ...
/// </summary>
public static class EventsAuditMapper
{
    public const string Module = "events";

    public static (string Action, AuditTarget Target, IReadOnlyDictionary<string, string> Metadata) Map(
        DomainEvent domainEvent,
        IAggregateRoot aggregate)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(aggregate);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        var action = domainEvent switch
        {
            EventCreated created => WithSlug("events.event.created", created.Slug),
            EventDetailsUpdated updated => WithSlug("events.event.details_updated", updated.Slug),
            EventVenueSet set => WithSourceVenue(set),
            EventVenueUpdated => "events.event.venue_updated",
            EventPoliciesUpdated policies => WithPolicies(policies),
            EventStructureChanged structure => WithStructure(structure),
            EventPublished => "events.event.published",
            EventCancelled => "events.event.cancelled",
            EventCompleted => "events.event.completed",
            VenueCreated => "events.venue.created",
            VenueUpdated => "events.venue.updated",
            VenueEntranceChanged entrance => WithVenueEntrance(entrance),
            VenueArchived => "events.venue.archived",
            _ => $"events.{Snake(domainEvent.GetType().Name)}"
        };

        var targetType = aggregate is Venue ? "venue" : "event";

        return (
            action,
            new AuditTarget(targetType, aggregate.Id.Value.ToString("D", CultureInfo.InvariantCulture)),
            metadata);

        string WithSlug(string name, string slug)
        {
            metadata["slug"] = slug;
            return name;
        }

        string WithSourceVenue(EventVenueSet set)
        {
            metadata["source"] = set.SourceVenueId is { } venueId
                ? venueId.Value.ToString("D", CultureInfo.InvariantCulture)
                : "inline";
            return "events.event.venue_set";
        }

        string WithPolicies(EventPoliciesUpdated policies)
        {
            metadata["refunds"] = Flag(policies.RefundsEnabled);
            metadata["transfers"] = Flag(policies.TransfersEnabled);
            metadata["lipaMdogoMdogo"] = Flag(policies.LipaMdogoMdogoEnabled);
            return "events.event.policies_updated";
        }

        string WithStructure(EventStructureChanged structure)
        {
            metadata["itemId"] = structure.ItemId.ToString("D", CultureInfo.InvariantCulture);
            return $"events.event.{Snake(structure.Part.ToString())}_{Snake(structure.Change.ToString())}";
        }

        string WithVenueEntrance(VenueEntranceChanged entrance)
        {
            metadata["itemId"] = entrance.EntranceId.Value.ToString("D", CultureInfo.InvariantCulture);
            return $"events.venue.entrance_{Snake(entrance.Change.ToString())}";
        }
    }

    private static string Flag(bool value) => value ? "on" : "off";

    /// <summary><c>SalePhase</c> → <c>sale_phase</c>.</summary>
    internal static string Snake(string value)
    {
        var builder = new StringBuilder(value.Length + 4);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (char.IsUpper(c) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
