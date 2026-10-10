using Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Api.IntegrationTests.Infrastructure;

public sealed class KraftHausApiFactory : WebApplicationFactory<Host.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["OpenApiOptions:EnableSwagger"] = "false",

                    ["CorsOptions:AllowedOrigins:0"] = "*",
                    ["CorsOptions:AllowedMethods:0"] = "*",
                    ["CorsOptions:AllowedHeaders:0"] = "*",
                    ["CorsOptions:AllowCredentials"] = "false",
                    ["CorsOptions:MaxAgeInSeconds"] = "60",

                    ["CustomRateLimitingOptions:Global:Limit"] = "100",
                    ["CustomRateLimitingOptions:Global:PeriodInMs"] = "60000",
                    ["CustomRateLimitingOptions:Global:QueueLimit"] = "0",

                    ["CustomRateLimitingOptions:Sms:Limit"] = "2",
                    ["CustomRateLimitingOptions:Sms:PeriodInMs"] = "60000",
                    ["CustomRateLimitingOptions:Sms:QueueLimit"] = "0",

                    ["CustomRateLimitingOptions:OtpVerify:Limit"] = "2",
                    ["CustomRateLimitingOptions:OtpVerify:PeriodInMs"] = "60000",
                    ["CustomRateLimitingOptions:OtpVerify:QueueLimit"] = "0",

                    ["CustomRateLimitingOptions:TokenRefresh:Limit"] = "2",
                    ["CustomRateLimitingOptions:TokenRefresh:PeriodInMs"] = "60000",
                    ["CustomRateLimitingOptions:TokenRefresh:QueueLimit"] = "0",

                    ["CustomRateLimitingOptions:Register:Limit"] = "100",
                    ["CustomRateLimitingOptions:Register:PeriodInMs"] = "60000",
                    ["CustomRateLimitingOptions:Register:QueueLimit"] = "0",

                    ["CustomRateLimitingOptions:CreateStore:Limit"] = "100",
                    ["CustomRateLimitingOptions:CreateStore:PeriodInMs"] = "60000",
                    ["CustomRateLimitingOptions:CreateStore:QueueLimit"] = "0",

                    ["CustomRateLimitingOptions:TokenCreate:Limit"] = "100",
                    ["CustomRateLimitingOptions:TokenCreate:PeriodInMs"] = "60000",
                    ["CustomRateLimitingOptions:TokenCreate:QueueLimit"] = "0",

                    ["CustomRateLimitingOptions:CheckRegistration:Limit"] = "100",
                    ["CustomRateLimitingOptions:CheckRegistration:PeriodInMs"] = "60000",
                    ["CustomRateLimitingOptions:CheckRegistration:QueueLimit"] = "0",

                    ["CustomRateLimitingOptions:ExemptPathPrefixes:0"] = "/health"
                });
        });
    }
}
