using Common.Application.Persistence;
using Identity.Infrastructure.Database;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Seeding;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Infrastructure;

public static class Setup
{
    public static IServiceCollection AddIdentity(
        this IServiceCollection services)
    {
        services.AddIdentityPersistence();

        services.AddScoped<IDatabaseSeeder, IdentitySeeder>();

        return services;
    }
}
