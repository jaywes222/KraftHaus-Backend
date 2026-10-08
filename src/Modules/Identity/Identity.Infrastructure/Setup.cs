using Common.Application.Persistence;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Application.Invitations;
using Identity.Application.Sessions;
using Identity.Infrastructure.Database;
using Identity.Infrastructure.IdentityServer;
using Identity.Infrastructure.Persistence.Repositories;
using Identity.Infrastructure.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Infrastructure;

public static class Setup
{
    public static IServiceCollection AddIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIdentityPersistence();

        services.AddKraftHausIdentityServer(
            configuration);

        services.AddScoped<
            ISessionTokenIssuer,
            IdentityServerSessionTokenIssuer>();

        services.AddScoped<IDatabaseSeeder, IdentitySeeder>();

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
            IIdentityUnitOfWork,
            IdentityUnitOfWork>();

        services.AddScoped<
            IOtpProofService,
            OtpProofService>();

        return services;
    }
}
