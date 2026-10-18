using Identity.Application.Badges;
using Identity.Infrastructure.Storage;
using Common.Application.Customers;
using Common.Application.Options;
using Common.Application.Persistence;
using Common.Infrastructure.Resiliency;
using Identity.Application.Abstractions;
using Identity.Application.Auditing;
using Identity.Application.Auth;
using Identity.Application.Authorization;
using Identity.Application.Invitations;
using Identity.Application.Options;
using Identity.Application.Otp;
using Identity.Application.Sessions;
using Identity.Application.Staff;
using Identity.Application.Users;
using Identity.Infrastructure.Auditing;
using Identity.Infrastructure.Authorization;
using Identity.Infrastructure.Database;
using Identity.Infrastructure.IdentityServer;
using Identity.Infrastructure.Options;
using Identity.Infrastructure.Otp;
using Identity.Infrastructure.Persistence.Repositories;
using Identity.Infrastructure.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure;

public static class Setup
{
    public static IServiceCollection AddIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIdentityPersistence();

        services.AddIdentityAuditing();

        services.AddKraftHausIdentityServer(
            configuration);

        services
           .AddOptions<OtpSecurityOptions>()
           .BindConfiguration("Identity:OtpSecurity")
           .Validate(
               x => !string.IsNullOrWhiteSpace(x.HashSecret) && x.HashSecret.Length >= 16,
               "Identity:OtpSecurity:HashSecret is required (at least 16 characters).")
           .ValidateOnStart();

        services
           .AddOptions<IdentityPolicyOptions>()
           .BindConfiguration(
               IdentityPolicyOptions.SectionName)
           .ValidateOnStart();

        // ValidateOnStart only runs registered IValidateOptions: without this the FluentValidation rules never ran.
        services.AddSingleton<IValidateOptions<IdentityPolicyOptions>>(
            new FluentValidateOptions<IdentityPolicyOptions>(new IdentityPolicyOptionsValidator()));

        // OtpSender only routes to ISmsGateway / IEmailGateway; the resilient HTTP clients live in those gateways
        // (Notifications:Sms / Notifications:Email). It is not an HTTP client itself.
        services.AddScoped<ICustomerDirectory, CustomerDirectory>();

        services.AddScoped<OtpSender>();
        services.AddScoped<IOtpSender>(sp => ActivatorUtilities.CreateInstance<DevelopmentOtpLogSender>(
            sp,
            sp.GetRequiredService<OtpSender>()));

        services
            .AddOptions<IdentityDevelopmentOptions>()
            .BindConfiguration(IdentityDevelopmentOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<
            IValidateOptions<IdentityDevelopmentOptions>,
            IdentityDevelopmentOptionsValidator>();

        services
            .AddOptions<IdentityBootstrapOptions>()
            .BindConfiguration(IdentityBootstrapOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<
            IValidateOptions<IdentityBootstrapOptions>,
            IdentityBootstrapOptionsValidator>();

        services.AddScoped<
            IPermissionAuthorizationService,
            PermissionAuthorizationService>();

        services.AddSingleton<
            IAuthorizationPolicyProvider,
            PermissionPolicyProvider>();

        services.AddScoped<
            IAuthorizationHandler,
            PermissionAuthorizationHandler>();

        services.AddScoped<
            ISessionTokenIssuer,
            IdentityServerSessionTokenIssuer>();

        services.AddScoped<IDatabaseSeeder, IdentitySeeder>();
        services.AddScoped<IDatabaseSeeder, BootstrapAdminSeeder>();

        services.AddScoped<
            IOtpChallengeRepository,
            OtpChallengeRepository>();

        services.AddScoped<
            IIdentityUserRepository,
            IdentityUserRepository>();

        services.AddScoped<
            IAuthSessionRepository,
            AuthSessionRepository>();

        services.AddScoped<
            IRoleAssignmentRepository,
            RoleAssignmentRepository>();

        services.AddScoped<
            IStaffInvitationRepository,
            StaffInvitationRepository>();

        services.AddScoped<
            IStaffDirectory,
            StaffDirectory>();

        services.AddScoped<
            IIdentityUnitOfWork,
            IdentityUnitOfWork>();

        services.AddScoped<
            IOtpProofService,
            OtpProofService>();

        services.AddOptions<StaffBadgeOptions>().BindConfiguration(StaffBadgeOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<StaffBadgeOptions>, StaffBadgeOptionsValidator>();
        services.AddScoped<IStaffBadgeRepository, StaffBadgeRepository>();
        services.AddSingleton<IStaffPhotoStorage, DiskStaffPhotoStorage>();
        services.AddScoped<StaffBadgeService>();

        services.AddScoped<IStaffInvitationNotifier, StaffInvitationNotifier>();
        services.AddScoped<AcceptStaffInvitationService>();
        services.AddScoped<CreateStaffInvitationService>();
        services.AddScoped<RevokeStaffInvitationService>();
        services.AddScoped<ListStaffInvitationsService>();
        services.AddScoped<GrantRoleService>();
        services.AddScoped<RevokeRoleService>();
        services.AddScoped<ListUserRolesService>();
        services.AddScoped<ListStaffMembersService>();
        services.AddScoped<GetCurrentUserService>();

        services.AddScoped<RequestOtpService>();
        services.AddScoped<CompleteOtpLoginService>();

        services.AddScoped<RefreshAuthSessionService>();
        services.AddScoped<RevokeAuthSessionService>();
        services.AddScoped<RotateAuthSessionService>();
        services.AddScoped<CreateAuthSessionService>();

        services.AddScoped<IOtpCodeGenerator, OtpCodeGenerator>();
        services.AddScoped<IOtpCodeHasher, OtpCodeHasher>();

        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        return services;
    }

    private static void AddIdentityAuditing(
        this IServiceCollection services)
    {
        services.AddSingleton<IdentityAuditEventMapper>();
        services.AddScoped<IdentityAuditEntryFactory>();
        services.AddScoped<IdentityAuditSaveChangesInterceptor>();
        services.AddScoped<IIdentityAuditTrail, IdentityAuditTrail>();
        services.AddScoped<SessionStatusCacheInvalidator>();
    }
}
