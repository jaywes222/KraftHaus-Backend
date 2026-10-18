using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Ticketing.Domain.Common;
using Common.Domain.Phones;

namespace Ticketing.Domain.Waitlist;

/// <summary>
///     One phone number on one event's waitlist (UF-001). The phone is unique per event; early access (BR: limited
///     to waitlisted numbers during its window) later checks this table by phone at checkout. Privacy acceptance is
///     stored with the policy version and time; marketing consent is separate and optional.
/// </summary>
public sealed class WaitlistEntry : AggregateRoot<WaitlistEntryId>
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 80;
    public const int LocationMinLength = 2;
    public const int LocationMaxLength = 80;
    public const int EmailMaxLength = 254;
    public const int PolicyVersionMaxLength = 32;
    public const int MaxAge = 100;

    /// <summary>The validation error key every invalid form maps to (field details are in the sub-errors).</summary>
    public const string InvalidKey = "WaitlistInvalid";

    private WaitlistEntry(WaitlistEntryId id)
        : base(id)
    {
    }

    /// <summary>The Events module's event id (no cross-module foreign key: different schema, same tenant database).</summary>
    public Guid EventId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>E.164 Kenyan mobile.</summary>
    public string Phone { get; private set; } = string.Empty;

    public int Age { get; private set; }

    public WaitlistGender Gender { get; private set; }

    public string Location { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    public string PrivacyPolicyVersion { get; private set; } = string.Empty;

    public DateTimeOffset PrivacyAcceptedAt { get; private set; }

    public bool MarketingConsent { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    /// <summary>When the SMS gateway accepted the confirmation. <c>null</c> until then (a resend job can pick it up).</summary>
    public DateTimeOffset? ConfirmationSentAt { get; private set; }

    /// <summary>Confirmation SMS attempts that the gateway did not accept (the resend job stops after a few).</summary>
    public int ConfirmationAttempts { get; private set; }

    /// <summary>
    ///     Validates every field and reports all problems in one error (<c>Ticketing.WaitlistInvalid</c>, sub-errors
    ///     <c>"Field: message"</c>). <paramref name="minimumAge" /> comes from configuration until the product owner
    ///     confirms it.
    /// </summary>
    public static Result<WaitlistEntry> Join(
        WaitlistEntryId id,
        Guid eventId,
        WaitlistApplicant applicant,
        int minimumAge,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(applicant);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumAge, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumAge, MaxAge);

        var valid = Validate(applicant, minimumAge);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var (name, phone, location, email, policyVersion) = valid.Value;

        var entry = new WaitlistEntry(id)
        {
            EventId = eventId,
            Name = name,
            Phone = phone.Value,
            Age = applicant.Age,
            Gender = applicant.Gender,
            Location = location,
            Email = email,
            PrivacyPolicyVersion = policyVersion,
            PrivacyAcceptedAt = now,
            MarketingConsent = applicant.MarketingConsent,
            JoinedAt = now
        };

        entry.RaiseEvent(new WaitlistJoined(id, eventId, applicant.MarketingConsent));

        return entry;
    }

    /// <summary>Records that the confirmation SMS was accepted. Idempotent: a second call changes nothing.</summary>
    public void MarkConfirmationSent(DateTimeOffset now)
    {
        if (ConfirmationSentAt is not null)
        {
            return;
        }

        var @event = new WaitlistConfirmationSent(Id, EventId);

        ConfirmationSentAt = now;

        RaiseEvent(@event);
    }

    /// <summary>The gateway did not accept the confirmation; the resend job will try again (up to its limit).</summary>
    public void RecordConfirmationFailed()
    {
        if (ConfirmationSentAt is null)
        {
            ConfirmationAttempts++;
        }
    }

    /// <summary>Normalises the phone the same way <see cref="Join" /> does (for duplicate checks before joining).</summary>
    public static bool TryNormalizePhone(string? phone, out string normalized)
    {
        if (KenyanMobile.TryCreate(phone, out var mobile))
        {
            normalized = mobile!.Value;
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    private static Result<(string Name, KenyanMobile Phone, string Location, string? Email, string PolicyVersion)>
        Validate(WaitlistApplicant applicant, int minimumAge)
    {
        var errors = new List<string>();

        var name = RequiredText(applicant.Name, "Name", NameMinLength, NameMaxLength, errors);

        if (!KenyanMobile.TryCreate(applicant.Phone, out var phone))
        {
            errors.Add("Phone: Enter a Kenyan mobile number, e.g. 0712 345 678.");
        }

        if (applicant.Age < minimumAge || applicant.Age > MaxAge)
        {
            errors.Add($"Age: Age must be between {minimumAge} and {MaxAge}.");
        }

        if (!Enum.IsDefined(applicant.Gender))
        {
            errors.Add("Gender: Choose one of the options.");
        }

        var location = RequiredText(applicant.Location, "Location", LocationMinLength, LocationMaxLength, errors);

        var email = OptionalEmail(applicant.Email, errors);

        if (!applicant.PrivacyAccepted)
        {
            errors.Add("PrivacyAccepted: Accept the privacy policy to join the waitlist.");
        }

        var policyVersion = applicant.PrivacyPolicyVersion?.Trim();
        if (string.IsNullOrEmpty(policyVersion) ||
            policyVersion.Length > PolicyVersionMaxLength ||
            !policyVersion.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
        {
            errors.Add("PrivacyPolicyVersion: The privacy policy version is missing or invalid.");
        }

        return errors.Count > 0
            ? TicketingErrors.Invalid(InvalidKey, errors)
            : (name!, phone!, location!, email, policyVersion!);
    }

    private static string? RequiredText(string? value, string field, int minLength, int maxLength, List<string> errors)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < minLength || trimmed.Length > maxLength)
        {
            errors.Add($"{field}: Must be between {minLength} and {maxLength} characters.");
            return null;
        }

        if (trimmed.Any(char.IsControl))
        {
            errors.Add($"{field}: Contains characters that are not allowed.");
            return null;
        }

        return trimmed;
    }

    private static string? OptionalEmail(string? value, List<string> errors)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        var looksValid =
            trimmed.Length <= EmailMaxLength &&
            at > 0 &&
            at == trimmed.LastIndexOf('@') &&
            trimmed.IndexOf('.', at) > at + 1 &&
            !trimmed.EndsWith('.') &&
            !trimmed.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));

        if (!looksValid)
        {
            errors.Add("Email: Enter a valid email address or leave it empty.");
            return null;
        }

        return trimmed;
    }
}
