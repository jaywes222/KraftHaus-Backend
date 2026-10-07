using System.Net.Mail;
using Identity.Domain.Common;
using Identity.Domain.Otp;
using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

public interface IOtpChallengeRepository
{
    Task<OtpChallenge?> FindLatestPendingAsync(
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
    Task<ApplicationUser?> FindByPhoneAsync(
        PhoneNumber phone,
        CancellationToken cancellationToken);

    Task<ApplicationUser?> FindByEmailAsync(
        EmailAddress email,
        CancellationToken cancellationToken);

    Task SaveAsync(
        ApplicationUser user,
        CancellationToken cancellationToken);
}

public interface IIdentityUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
