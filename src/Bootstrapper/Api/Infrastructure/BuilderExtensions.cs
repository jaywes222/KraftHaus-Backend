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
                reloadOnChange: true);

        // Re-add user secrets after the JSON files: CreateBuilder loads them first, so without this the JSON files
        // above would silently override local secrets such as connection strings.
        if (builder.Environment.IsDevelopment())
        {
            builder.Configuration.AddUserSecrets(
                typeof(BuilderExtensions).Assembly,
                optional: true,
                reloadOnChange: true);
        }

        builder.Configuration.AddEnvironmentVariables();

        return builder;
    }
}
