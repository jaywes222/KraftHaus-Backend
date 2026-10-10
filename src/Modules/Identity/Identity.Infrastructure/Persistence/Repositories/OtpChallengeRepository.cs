using Common.Domain.Tenancy;
using Identity.Application.Abstractions;
using Identity.Domain.Common;
using Identity.Domain.Otp;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

internal sealed class OtpChallengeRepository(
    IdentityDbContext db)
    : IOtpChallengeRepository
{
    public Task<OtpChallenge?> FindLatestPendingAsync(
        TenantId tenantId,
        OtpChannel channel,
        string destination,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        return db.OtpChallenges
            .Where(x =>
                x.TenantId == tenantId &&
                x.Channel == channel &&
                x.Destination == destination &&
                x.Purpose == purpose &&
                x.Status == OtpChallengeStatus.Pending)
            .OrderByDescending(x => x.IssuedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<OtpChallenge?> FindAsync(
        OtpChallengeId challengeId,
        CancellationToken cancellationToken)
    {
        return db.OtpChallenges
            .SingleOrDefaultAsync(
                x => x.Id == challengeId,
                cancellationToken);
    }

    public Task AddAsync(
        OtpChallenge challenge,
        CancellationToken cancellationToken)
    {
        db.OtpChallenges.Add(challenge);

        return Task.CompletedTask;
    }

    public Task SaveAsync(
        OtpChallenge challenge,
        CancellationToken cancellationToken)
    {
        db.OtpChallenges.Update(challenge);

        return Task.CompletedTask;
    }
}
