using Common.Application.Auditing;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Infrastructure.Auditing;

public static class Setup
{
    /// <summary>
    ///     Registers the shared audit plumbing (request context + fingerprinting). Each module owns its own audit
    ///     table and capture; see Identity.Infrastructure/Auditing for the first consumer.
    /// </summary>
    public static IServiceCollection AddCommonAuditing(this IServiceCollection services)
    {
        return services
            .AddScoped<IAuditRequestContextProvider, HttpAuditRequestContextProvider>()
            .AddSingleton<IAuditFingerprinter, HmacAuditFingerprinter>();
    }
}
