 using System.Reflection;
using System.Text.Json.Serialization;
using Api.Middlewares;
using Common.Application.Extensions;
using Common.Application.JsonConverters;
using Common.Application.Options;
using Common.Application.Search;
using Common.Endpoints.Versioning;
using Common.Infrastructure.Auditing;
using Common.Infrastructure.Auth;
using Common.Infrastructure.Caching;
using Common.Infrastructure.EventBus;
using Common.Infrastructure.FeatureManagement;
using Common.Infrastructure.Persistence;
using Common.InterModuleRequests;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Json;

namespace Api.Infrastructure;

internal static partial class Setup
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment env)
    {
        var moduleAssemblies = services.GetActiveModuleAssemblies();

        return services
            .Configure<JsonOptions>(x =>
            {
                x.SerializerOptions.Converters.Add(
                    new StronglyTypedIdJsonConverterFactory());

                x.SerializerOptions.Converters.Add(
                    new StrictDateTimeOffsetJsonConverter());

                x.SerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(allowIntegerValues: false));

                x.SerializerOptions.UnmappedMemberHandling =
                    JsonUnmappedMemberHandling.Disallow;
            })
            .Configure<HostOptions>(x =>
            {
                x.ServicesStartConcurrently = true;
                x.ServicesStopConcurrently = true;
            })
    .AddVersioning()
    .AddHttpContextAccessor()
    .AddCustomForwardedHeaders(configuration)
    .AddHttpRequestLogging()
    .AddGlobalExceptionHandlingMiddleware()
    .AddCustomizedProblemDetails(env)
    .AddEndpointsApiExplorer()
    .AddObservability(configuration, env)
    .AddCustomCors(configuration)
    .AddValidatorsFromAssemblies(moduleAssemblies)

    // Common infrastructure
    .AddCommonCaching(configuration)
    .AddCommonEventHandling(moduleAssemblies)
    .AddCommonInterModuleRequests()
    .AddCommonOptions(configuration, env)
    .AddScoped<ISearchLanguageResolver, SearchLanguageResolver>()
    .AddCommonPersistence(configuration)
    .AddCommonFeatureManagement(configuration)
    .AddCommonAuth()
    .AddCommonAuditing()

    .AddCustomMassTransit(configuration, moduleAssemblies)
    .AddCustomHealthChecks(configuration)
    .AddEnrichLogsWithUserInfoMiddleware();
    }

    public static IApplicationBuilder UseInfrastructure(
        this IApplicationBuilder app)
    {
        app
            .UseForwardedHeaders()
            .UseMiddleware<SecurityHeadersMiddleware>()
            .UseHttpRequestLogging()
            .UseCors()
            .UseGlobalExceptionHandlingMiddleware();

        var hasAuth =
            app.ApplicationServices
                .GetService<IAuthenticationSchemeProvider>() != null;

        if (hasAuth)
        {
            app.UseAuthentication()
                .UseMiddleware<EnrichLogsWithUserInfoMiddleware>();
        }

        app.UseRateLimiter();

        // Authorization is added by UseModuleAwareAuthorization, after the modules' middleware: permission checks
        // are scoped to the tenant that Tenancy resolves from the host name, so it must run first.
        return app;
    }

    /// <summary>
    ///     Adds authorization after <c>UseModules</c>. Order: authentication → module middleware (tenant resolution,
    ///     which also validates the token's <c>tid</c> against the host) → authorization → endpoints.
    /// </summary>
    public static IApplicationBuilder UseModuleAwareAuthorization(this IApplicationBuilder app)
    {
        return app.ApplicationServices.GetService<IAuthenticationSchemeProvider>() is null
            ? app
            : app.UseAuthorization();
    }

    private static IServiceCollection AddCustomCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var corsOptions = configuration
                                  .GetSection(nameof(CorsOptions))
                                  .Get<CorsOptions>()
                              ?? throw new InvalidOperationException(
                                  $"Missing configuration: {nameof(CorsOptions)}.");

        return services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                if (corsOptions.AllowedOrigins.Count == 0)
                {
                    builder.SetIsOriginAllowed(_ => false);
                }
                else if (corsOptions.AllowedOrigins is ["*"])
                {
#pragma warning disable S5122
                    builder.AllowAnyOrigin();
#pragma warning restore S5122
                }
                else
                {
                    builder.WithOrigins([.. corsOptions.AllowedOrigins]);
                }

                if (corsOptions.AllowedMethods is ["*"])
                {
                    builder.AllowAnyMethod();
                }
                else
                {
                    builder.WithMethods([.. corsOptions.AllowedMethods]);
                }

                if (corsOptions.AllowedHeaders is ["*"])
                {
                    builder.AllowAnyHeader();
                }
                else
                {
                    builder.WithHeaders([.. corsOptions.AllowedHeaders]);
                }

                if (corsOptions.AllowCredentials)
                {
                    builder.AllowCredentials();
                }
                else
                {
                    builder.DisallowCredentials();
                }

                builder.SetPreflightMaxAge(
                    TimeSpan.FromSeconds(corsOptions.MaxAgeInSeconds));
            });
        });
    }

    private static IServiceCollection AddCustomizedProblemDetails(
        this IServiceCollection services,
        IWebHostEnvironment env)
    {
        return services.AddProblemDetails(opt =>
        {
            opt.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance =
                    $"{context.HttpContext.Request.Method} " +
                    $"{context.HttpContext.Request.Path.Value}";

                context.ProblemDetails.Extensions.TryAdd(
                    "traceId",
                    context.HttpContext.TraceIdentifier);

                context.ProblemDetails.Extensions.TryAdd(
                    "environment",
                    env.EnvironmentName);
            };
        });
    }

    private static IServiceCollection AddEnrichLogsWithUserInfoMiddleware(
        this IServiceCollection services)
    {
        return services.AddScoped<EnrichLogsWithUserInfoMiddleware>();
    }
}
