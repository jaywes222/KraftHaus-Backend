using Common.Domain.StronglyTypedIds;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tenancy.Domain.Common;

namespace Identity.Infrastructure.Persistence;

public sealed class ApplicationUserConfiguration
    : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(
        EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("application_user");

        builder.HasKey(x => x.Id);

        builder.Ignore(x => x.Events);

        builder.Property(x => x.Id)
            .HasConversion(
                IdentityEfConversions
                    .IdConverter<ApplicationUserId>());

        builder.Property(x => x.Scope)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.TenantId)
            .HasConversion(
                new ValueConverter<TenantId?, DefaultIdType>(
                    x => x.HasValue
                        ? x.Value.Value
                        : Guid.Empty,
                    x => x == Guid.Empty
                        ? null
                        : new TenantId(x)));

        builder.Property(x => x.Phone)
            .HasConversion(
                new ValueConverter<PhoneNumber?, string?>(
                    x => x == null
                        ? null
                        : x.Value,
                    x => x == null
                        ? null
                        : PhoneNumber.Create(x).Value))
            .HasMaxLength(32);

        builder.Property(x => x.Email)
            .HasConversion(
                new ValueConverter<EmailAddress?, string?>(
                    x => x == null
                        ? null
                        : x.Value,
                    x => x == null
                        ? null
                        : EmailAddress.Create(x).Value))
            .HasMaxLength(320);

        builder.Property(x => x.DisplayName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.LastSignedInAt);

        builder.HasIndex(
                x => new
                {
                    x.Scope,
                    x.Phone
                })
            .IsUnique()
            .HasFilter("\"Phone\" IS NOT NULL");

        builder.HasIndex(
                x => new
                {
                    x.Scope,
                    x.Email
                })
            .IsUnique()
            .HasFilter("\"Email\" IS NOT NULL");

        builder.HasMany(x => x.Consents)
            .WithOne()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        IdentityEfConversions.ConfigureAudit(builder);

        builder.Property(x => x.Version)
            .HasColumnName("aggregate_version")
            .IsConcurrencyToken();
    }
}
