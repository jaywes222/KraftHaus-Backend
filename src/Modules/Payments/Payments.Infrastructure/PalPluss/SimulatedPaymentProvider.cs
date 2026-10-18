using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Payments.Application.Provider;
using Payments.Domain.Payments;

namespace Payments.Infrastructure.PalPluss;

/// <summary>
///     Development stand-in for PalPluss: no network, no money. A prompt "completes" after
///     <c>SimulatedCompletionSeconds</c>; phones ending 1032 cancel and ending 0001 fail. Only allowed in Development
///     (and the Testing environment).
/// </summary>
internal sealed class SimulatedPaymentProvider(IOptions<PalPlussOptions> options, TimeProvider time) : IPaymentProvider
{
    private static readonly ConcurrentDictionary<string, (DateTimeOffset At, ProviderStkRequest Request)> Transactions = new();

    public Task<ProviderStartResult> StartStkAsync(ProviderStkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = Guid.NewGuid().ToString("D");
        Transactions[id] = (time.GetUtcNow(), request);

        return Task.FromResult<ProviderStartResult>(new ProviderStartResult.Accepted(id, $"ws_CO_SIM_{id[..8]}"));
    }

    public Task<ProviderLookup> GetTransactionAsync(string transactionId, CancellationToken cancellationToken)
    {
        if (!Transactions.TryGetValue(transactionId, out var entry))
        {
            return Task.FromResult<ProviderLookup>(new ProviderLookup.NotFound());
        }

        var done = time.GetUtcNow() - entry.At >= TimeSpan.FromSeconds(options.Value.SimulatedCompletionSeconds);
        var phone = entry.Request.Phone;

        var (status, code, description) = Outcome(done, phone);

        return Task.FromResult<ProviderLookup>(new ProviderLookup.Found(new ProviderRecord(
            transactionId,
            status,
            entry.Request.Amount,
            entry.Request.AccountReference,
            status == ProviderTransactionStatus.Success ? $"SIM{transactionId[..7].ToUpperInvariant()}" : null,
            code,
            description)));
    }

    private static (ProviderTransactionStatus Status, string? Code, string? Description) Outcome(bool done, string phone)
    {
        if (!done)
        {
            return (ProviderTransactionStatus.Pending, null, null);
        }

        if (phone.EndsWith("1032", StringComparison.Ordinal))
        {
            return (ProviderTransactionStatus.Cancelled, "1032", "Request cancelled by user");
        }

        return phone.EndsWith("0001", StringComparison.Ordinal)
            ? (ProviderTransactionStatus.Failed, "1", "The balance is insufficient for the transaction.")
            : (ProviderTransactionStatus.Success, "0", "The service request is processed successfully.");
    }
}
