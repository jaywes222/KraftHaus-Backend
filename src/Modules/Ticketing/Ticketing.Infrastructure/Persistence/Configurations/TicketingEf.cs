using Common.Domain.Entities;
using Common.Domain.StronglyTypedIds;
using Common.Infrastructure.Persistence.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ticketing.Infrastructure.Persistence.Configurations;

/// <summary>Mapping helpers shared by the Ticketing configurations (same conventions as Events).</summary>
internal static class TicketingEf
{
    private static readonly ValueConverter<ApplicationUserId?, Guid?> UserIdConverter = new(
        id => id.HasValue ? id.Value.Value : null,
        value => value.HasValue ? new ApplicationUserId(value.Value) : null);

    public static void ConfigureKey<TEntity, TId>(EntityTypeBuilder<TEntity> builder)
        where TEntity : AuditableEntity<TId>
        where TId : struct, IStronglyTypedId
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(new StronglyTypedIdValueConverter<TId>()).ValueGeneratedNever();
    }

    public static void ConfigureAudit<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : AuditableEntity
    {
        builder.Property(x => x.CreatedOn).IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(UserIdConverter);
        builder.Property(x => x.LastModifiedOn);
        builder.Property(x => x.LastModifiedBy).HasConversion(UserIdConverter);
    }

    public static void ConfigureAggregateVersion<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class =>
        builder.Property("Version").HasColumnName("aggregate_version").IsConcurrencyToken();
}
