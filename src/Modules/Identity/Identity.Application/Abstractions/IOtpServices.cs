using System.Net.Mail;
using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Identity.Domain.Common;
using Identity.Domain.Otp;
using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

public interface IOtpChallengeRepository
{
    /// <summary>The newest pending challenge for this destination and purpose issued on <paramref name="tenantId" />.</summary>
    Task<OtpChallenge?> FindLatestPendingAsync(
        TenantId tenantId,
        OtpChannel channel,
        string destination,
        OtpPurpose purpose,
        CancellationToken cancellationToken);

    Task<OtpChallenge?> FindAsync(
        OtpChallengeId challengeId,
        CancellationToken cancellationToken);

    Task AddAsync(
        OtpChallenge challenge,
        CancellationToken cancellationToken);

    Task SaveAsync(
        OtpChallenge challenge,
        CancellationToken cancellationToken);
}

public interface IOtpCodeGenerator
{
    string Generate();
}

public interface IOtpCodeHasher
{
    string Hash(string code);
}

public interface IOtpSender
{
    Task SendAsync(
        OtpChannel channel,
        string destination,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken);
}

public interface IIdentityUserRepository
{
    Task<ApplicationUser?> FindAsync(
        ApplicationUserId id,
        CancellationToken cancellationToken);

    /// <summary>Finds the user with this phone number in one identity realm (numbers are unique per realm).</summary>
    Task<ApplicationUser?> FindByPhoneAsync(
        IdentityRealm realm,
        PhoneNumber phone,
        CancellationToken cancellationToken);

    /// <summary>Finds the user with this e-mail address in one identity realm.</summary>
    Task<ApplicationUser?> FindByEmailAsync(
        IdentityRealm realm,
        EmailAddress email,
        CancellationToken cancellationToken);

    Task AddAsync(
        ApplicationUser user,
        CancellationToken cancellationToken);

    Task SaveAsync(
        ApplicationUser user,
        CancellationToken cancellationToken);
}

public interface IIdentityUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
