namespace Ticketing.Domain.Waitlist;

/// <summary>What a visitor typed into the waitlist form, before validation.</summary>
public sealed record WaitlistApplicant(
    string? Name,
    string? Phone,
    int Age,
    WaitlistGender Gender,
    string? Location,
    string? Email,
    bool PrivacyAccepted,
    string? PrivacyPolicyVersion,
    bool MarketingConsent);
