using System.Text.Json.Serialization;
using Ticketing.Domain.Waitlist;

namespace Ticketing.Application.Waitlist;

/// <summary>Body of <c>POST /api/client/waitlist</c> (Client docs/api/WAITLIST-CONTRACT.md).</summary>
public sealed record JoinWaitlistRequest(
    string? EventSlug,
    string? Name,
    string? Phone,
    int Age,
    WaitlistGenderInput Gender,
    string? Location,
    string? Email,
    bool PrivacyAccepted,
    string? PrivacyPolicyVersion,
    bool MarketingConsent);

/// <summary>Wire values of the gender field. Unknown strings fail JSON binding (400).</summary>
public enum WaitlistGenderInput
{
    [JsonStringEnumMemberName("woman")] Woman = 1,
    [JsonStringEnumMemberName("man")] Man = 2,
    [JsonStringEnumMemberName("non_binary")] NonBinary = 3,
    [JsonStringEnumMemberName("prefer_not_to_say")] PreferNotToSay = 4
}

/// <param name="SmsSent">
///     <c>true</c> when the SMS gateway accepted the confirmation; <c>false</c> when it is still pending (the entry
///     is saved either way).
/// </param>
public sealed record WaitlistJoinedView(Guid WaitlistEntryId, bool SmsSent);

internal static class WaitlistMapping
{
    public static WaitlistGender ToDomain(this WaitlistGenderInput input) =>
        input switch
        {
            WaitlistGenderInput.Woman => WaitlistGender.Woman,
            WaitlistGenderInput.Man => WaitlistGender.Man,
            WaitlistGenderInput.NonBinary => WaitlistGender.NonBinary,
            WaitlistGenderInput.PreferNotToSay => WaitlistGender.PreferNotToSay,
            _ => (WaitlistGender)0
        };
}
