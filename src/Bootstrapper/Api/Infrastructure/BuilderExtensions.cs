using System;
using Microsoft.AspNetCore.Builder;

namespace Api.Infrastructure;

internal static class BuilderExtensions
{
    public static WebApplicationBuilder AddConfigurations(
        this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Configuration
            .AddJsonFile(
                "appsettings.json",
                optional: true,
                reloadOnChange: true)
            .AddJsonFile(
                $"appsettings.{builder.Environment.EnvironmentName}.json",
                optional: true,
                reloadOnChange: true)
            .AddEnvironmentVariables();

        return builder;
    }
}
