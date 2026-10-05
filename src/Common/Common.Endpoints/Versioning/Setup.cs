using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Endpoints.Versioning;

public static class Setup
{
    public static IServiceCollection AddVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            options.ReportApiVersions = true;
            options.AssumeDefaultVersionWhenUnspecified = false;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        });

        return services;
    }

    public static ApiVersionSet GetApiVersionSet(this IEndpointRouteBuilder endpoints)
    {
        return endpoints
            .NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            // .HasApiVersion(new ApiVersion(2, 0))
            // .HasApiVersion(new ApiVersion(3, 0))
            .Build();
    }
}
