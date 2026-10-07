using Identity.Domain.Common;
using Identity.Domain.Customers;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tenancy.Domain.Common;

namespace Identity.Infrastructure.Persistence;

public sealed class CustomerRefConfiguration
    : IEntityTypeConfiguration<CustomerRef>
{
    public void Configure(
        EntityTypeBuilder<CustomerRef> builder)
    {
        builder.ToTable("customer_ref");

        builder.HasKey(x => x.Id);

        builder.Ignore(x => x.Events);

        builder.Property(x => x.Id)
            .HasConversion(
                IdentityEfConversions
                    .IdConverter<CustomerRefId>());

        builder.Property(x => x.IdentityScope)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ExternalIdentityId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Phone)
            .HasConversion(
                new ValueConverter<PhoneNumber?, string?>(
                    x => x == null
                        ? null
                        : x.Value,
                    x => x == null
                        ? null
                        : PhoneNumber.Create(x).Value))
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.DisplayName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(
                x => new
                {
                    x.IdentityScope,
                    x.ExternalIdentityId
                })
            .IsUnique();

        builder.HasIndex(x => x.Phone);

        builder.Property(x => x.Version)
            .HasColumnName("aggregate_version")
            .IsConcurrencyToken();

        IdentityEfConversions.ConfigureAudit(builder);
    }
}
