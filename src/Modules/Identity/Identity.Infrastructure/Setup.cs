using Common.Application.Options;
using Common.Application.Persistence;
using Common.Infrastructure.Resiliency;
using Identity.Application.Abstractions;
using Identity.Application.Auth;
using Identity.Application.Authorization;
using Identity.Application.Invitations;
using Identity.Application.Options;
using Identity.Application.Otp;
using Identity.Application.Sessions;
using Identity.Infrastructure.Database;
using Identity.Infrastructure.IdentityServer;
using Identity.Infrastructure.Options;
using Identity.Infrastructure.Persistence.Repositories;
using Identity.Infrastructure.Seeding;
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

        services.AddKraftHausIdentityServer(
            configuration);

        services
           .AddOptions<OtpSecurityOptions>()
           .BindConfiguration("Identity:OtpSecurity")
           .ValidateOnStart();

        services
           .AddOptions<IdentityPolicyOptions>()
           .BindConfiguration(
               IdentityPolicyOptions.SectionName)
           .ValidateOnStart();

        services
           .AddOptions<SmsOptions>()
           .BindConfiguration("SmsOptions")
           .ValidateOnStart();

        services.AddResilientHttpClient<IOtpSender, OtpSender>(
            (sp, httpClient) =>
            {
                var options =
                    sp.GetRequiredService<IOptions<SmsOptions>>()
                        .Value;

                if (string.IsNullOrWhiteSpace(options.BaseUrl))
                {
                    throw new InvalidOperationException(
                        "SmsOptions.BaseUrl is required.");
                }

                httpClient.BaseAddress = new Uri(
                    options.BaseUrl,
                    UriKind.Absolute);
            });

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
}
