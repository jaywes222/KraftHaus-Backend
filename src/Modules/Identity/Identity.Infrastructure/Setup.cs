using Common.Application.Persistence;
using Identity.Application.Sessions;
using Identity.Infrastructure.Database;
using Identity.Infrastructure.IdentityServer;
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

        return services;
    }
}
