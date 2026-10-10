using Api.Middlewares;
using Common.Application.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Api.IntegrationTests.Configuration;

/// <summary>
///     The shipped request-logging configuration must never capture Identity bodies or query strings: they carry
///     OTP codes, access tokens and refresh tokens. Guards against the rules drifting from the real routes again.
/// </summary>
public sealed class RequestLoggingConfigurationTests
{
    public static TheoryData<string, string> IdentityRoutes => new()
    {
        { "Development", "/api/identity/otp/request" },
        { "Development", "/api/identity/otp/verify" },
        { "Development", "/api/identity/session/refresh" },
        { "Development", "/api/identity/session/revoke" },
        { "Testing", "/api/identity/otp/request" },
        { "Testing", "/api/identity/otp/verify" },
        { "Testing", "/api/identity/session/refresh" },
        { "Testing", "/api/identity/session/revoke" }
    };

    [Theory]
    [MemberData(nameof(IdentityRoutes))]
    public void IdentityPostRoutes_AreSensitiveForRequestResponseAndQuery(string environment, string route)
    {
        var options = Load(environment);
        var path = new PathString(route);

        Assert.True(RequestResponseBodyLoggingMiddleware.IsSensitive(path, "POST", options.SensitiveRequestBodyPaths));
        Assert.True(RequestResponseBodyLoggingMiddleware.IsSensitive(path, "POST", options.SensitiveResponseBodyPaths));
        Assert.True(RequestResponseBodyLoggingMiddleware.IsSensitive(path, "POST", options.SensitiveQueryParamPaths));
    }

    private static RequestLoggingOptions Load(string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ApiProjectDirectory(), $"appsettings.{environment}.json"), optional: false)
            .Build();

        var options = configuration.GetSection(nameof(RequestLoggingOptions)).Get<RequestLoggingOptions>();

        Assert.NotNull(options);

        return options;
    }

    private static string ApiProjectDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Bootstrapper", "Api");

            if (File.Exists(Path.Combine(candidate, "Api.csproj")))
            {
                return candidate;
            }

            candidate = Path.Combine(directory.FullName, "Bootstrapper", "Api");

            if (File.Exists(Path.Combine(candidate, "Api.csproj")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Api project directory.");
    }
}
