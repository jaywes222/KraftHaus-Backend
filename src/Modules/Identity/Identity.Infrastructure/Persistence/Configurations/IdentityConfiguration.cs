using Common.Domain.Entities;
using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Identity.Domain.Common;
using Identity.Domain.Customers;
using Identity.Domain.Invitations;
using Identity.Domain.Otp;
using Identity.Domain.Roles;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Identity.Infrastructure.Persistence.Configurations;

internal static class IdentityEfConversions
{
    internal static void ConfigureAudit<TEntity>(
        EntityTypeBuilder<TEntity> builder)
        where TEntity : AuditableEntity
    {
        builder.Property(x => x.CreatedBy)
            .HasConversion(
                id => id.HasValue
                    ? id.Value.Value
                    : (DefaultIdType?)null,
                value => value.HasValue
                    ? new ApplicationUserId(value.Value)
                    : null);

        builder.Property(x => x.LastModifiedBy)
            .HasConversion(
                id => id.HasValue
                    ? id.Value.Value
                    : (DefaultIdType?)null,
                value => value.HasValue
                    ? new ApplicationUserId(value.Value)
                    : null);

        builder.Property(x => x.CreatedOn)
            .IsRequired();

        builder.Property(x => x.LastModifiedOn);
    }

    internal static void ConfigureAggregateVersion<TEntity>(
        EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property("Version")
            .HasColumnName("aggregate_version")
            .IsConcurrencyToken();
    }

    /// <summary>Maps <see cref="TenantId" /> (and <c>TenantId?</c>, where EF passes nulls through) to a uuid column.</summary>
    internal static ValueConverter<TenantId, DefaultIdType> TenantIdConverter() =>
        new(x => x.Value, x => new TenantId(x));

    internal static ValueConverter<TId, DefaultIdType>
        IdConverter<TId>()
        where TId : struct, IStronglyTypedId
    {
        return new ValueConverter<TId, DefaultIdType>(
            x => x.Value,
            x => CreateId<TId>(x));
    }

    private static TId CreateId<TId>(
        DefaultIdType value)
        where TId : struct, IStronglyTypedId
    {
        if (typeof(TId) == typeof(OtpChallengeId))
        {
            return (TId)(object)new OtpChallengeId(value);
        }

        if (typeof(TId) == typeof(AuthSessionId))
        {
            return (TId)(object)new AuthSessionId(value);
        }

        if (typeof(TId) == typeof(RoleAssignmentId))
        {
            return (TId)(object)new RoleAssignmentId(value);
        }

        if (typeof(TId) == typeof(StaffInvitationId))
        {
            return (TId)(object)new StaffInvitationId(value);
        }

        if (typeof(TId) == typeof(CustomerRefId))
        {
            return (TId)(object)new CustomerRefId(value);
        }

        if (typeof(TId) == typeof(ConsentRecordId))
        {
            return (TId)(object)new ConsentRecordId(value);
        }

        if (typeof(TId) == typeof(ApplicationUserId))
        {
            return (TId)(object)new ApplicationUserId(value);
        }

        if (typeof(TId) == typeof(StaffBadgeId))
        {
            return (TId)(object)new StaffBadgeId(value);
        }

        throw new InvalidOperationException(
            $"No Identity EF converter exists for {typeof(TId).Name}.");
    }
}
