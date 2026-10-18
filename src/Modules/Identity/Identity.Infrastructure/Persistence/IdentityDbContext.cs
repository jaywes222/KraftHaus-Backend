using Common.Infrastructure.Persistence.ValueConverters;
using Identity.Domain.Badges;
using Identity.Domain.Customers;
using Identity.Domain.Invitations;
using Identity.Domain.Otp;
using Identity.Domain.Roles;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Identity.Infrastructure.Auditing;
using Identity.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options)
    : DbContext(options)
{
    public DbSet<ApplicationUser> Users =>
        Set<ApplicationUser>();

    public DbSet<ConsentRecord> Consents =>
        Set<ConsentRecord>();

    public DbSet<OtpChallenge> OtpChallenges =>
        Set<OtpChallenge>();

    public DbSet<AuthSession> Sessions =>
        Set<AuthSession>();

    public DbSet<RoleAssignment> RoleAssignments =>
        Set<RoleAssignment>();

    public DbSet<StaffInvitation> StaffInvitations =>
        Set<StaffInvitation>();

    public DbSet<StaffBadge> StaffBadges =>
        Set<StaffBadge>();

    public DbSet<CustomerRef> CustomerRefs =>
        Set<CustomerRef>();

    /// <summary>Append-only security audit trail (WP-08). Written by the audit pipeline only.</summary>
    public DbSet<IdentityAuditRecord> SecurityAuditLog =>
        Set<IdentityAuditRecord>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");

        modelBuilder.ApplyConfiguration(
            new ApplicationUserConfiguration());

        modelBuilder.ApplyConfiguration(
            new ConsentRecordConfiguration());

        modelBuilder.ApplyConfiguration(
            new OtpChallengeConfiguration());

        modelBuilder.ApplyConfiguration(
            new AuthSessionConfiguration());

        modelBuilder.ApplyConfiguration(
            new RoleAssignmentConfiguration());

        modelBuilder.ApplyConfiguration(
            new StaffInvitationConfiguration());

        modelBuilder.ApplyConfiguration(
            new CustomerRefConfiguration());

        modelBuilder.ApplyConfiguration(
            new StaffBadgeConfiguration());

        modelBuilder.ApplyConfiguration(
            new IdentityAuditRecordConfiguration());

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(
        ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<UtcDateTimeOffsetConverter>();

        base.ConfigureConventions(configurationBuilder);
    }
}
