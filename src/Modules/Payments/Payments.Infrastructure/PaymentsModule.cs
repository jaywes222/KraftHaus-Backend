using Common.Infrastructure.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.Infrastructure.Endpoints;

namespace Payments.Infrastructure;

/// <summary>
///     Payments module (Spec 08 §4, §7): M-Pesa STK push through PalPluss, callbacks verified against PalPluss's own
///     record, a status-check job, and the Finance view. Tenant-scoped (schema <c>payments</c>).
/// </summary>
public sealed class PaymentsModule : IModule
{
    public string Name => "Payments";

    public int StartupPriority => 35;

    public IEnumerable<string> ActivitySourceNames => ["Krafthaus.Payments"];

    public IEnumerable<string> MeterNames => ["Krafthaus.Payments"];

    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddPayments(configuration);

    public void UseModule(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapPaymentEndpoints();
}
