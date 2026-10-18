using Common.Domain.Events;
using Ticketing.Domain.Common;

namespace Ticketing.Domain.Waitlist;

/// <summary>
///     Someone joined an event's waitlist (UF-001). Carries no personal data: the audit log and later consumers
///     (Communications) look the entry up by id when they need the phone number.
/// </summary>
public sealed record WaitlistJoined(WaitlistEntryId EntryId, Guid EventId, bool MarketingConsent) : DomainEvent;

/// <summary>The SMS gateway accepted the "you're on the list" confirmation for this entry.</summary>
public sealed record WaitlistConfirmationSent(WaitlistEntryId EntryId, Guid EventId) : DomainEvent;
