using System.Collections.Concurrent;
using Payments.Application.Provider;
using Payments.Domain.Payments;

namespace Api.IntegrationTests.Ticketing;

/// <summary>
///     PalPluss stand-in for HTTP tests. Every STK push is accepted (unless <see cref="RefuseNext" /> is set) and stays
///     PENDING until a test scripts its outcome with <see cref="Complete" />. The callback URL (with its token) is kept
///     so a test can play PalPluss calling back.
/// </summary>
public sealed class ScriptedPaymentProvider : IPaymentProvider
{
    private readonly ConcurrentDictionary<string, Prompt> _prompts = new();

    /// <summary>When set, transaction lookups leave the receipt out (PalPluss's GET schema may not carry it).</summary>
    public bool OmitReceipts { get; set; }

    /// <summary>When set, the next STK push is refused with this PalPluss error code.</summary>
    public string? RefuseNext { get; set; }

    public Task<ProviderStartResult> StartStkAsync(ProviderStkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (RefuseNext is { } code)
        {
            RefuseNext = null;
            return Task.FromResult<ProviderStartResult>(new ProviderStartResult.Refused(code, "scripted refusal", 30));
        }

        var id = Guid.NewGuid().ToString("D");
        _prompts[id] = new Prompt(id, request, ProviderTransactionStatus.Pending, request.Amount);

        return Task.FromResult<ProviderStartResult>(new ProviderStartResult.Accepted(id, "ws_CO_TEST"));
    }

    public Task<ProviderLookup> GetTransactionAsync(string transactionId, CancellationToken cancellationToken)
    {
        if (!_prompts.TryGetValue(transactionId, out var prompt))
        {
            return Task.FromResult<ProviderLookup>(new ProviderLookup.NotFound());
        }

        var success = prompt.Status == ProviderTransactionStatus.Success;

        return Task.FromResult<ProviderLookup>(new ProviderLookup.Found(new ProviderRecord(
            prompt.TransactionId,
            prompt.Status,
            prompt.ReportedAmount,
            prompt.Request.AccountReference,
            success && !OmitReceipts ? $"TST{prompt.TransactionId[..7].ToUpperInvariant()}" : null,
            success ? "0" : "1032",
            prompt.Status.ToString())));
    }

    /// <summary>All prompts sent to <paramref name="phone" /> (E.164), oldest first.</summary>
    public IReadOnlyList<Prompt> PromptsTo(string phone) =>
        [.. _prompts.Values.Where(x => x.Request.Phone == phone).OrderBy(x => x.Sequence)];

    public void Complete(string transactionId, ProviderTransactionStatus status, decimal? reportedAmount = null)
    {
        var prompt = _prompts[transactionId];
        _prompts[transactionId] = prompt with { Status = status, ReportedAmount = reportedAmount ?? prompt.Request.Amount };
    }

    public sealed record Prompt(
        string TransactionId,
        ProviderStkRequest Request,
        ProviderTransactionStatus Status,
        decimal ReportedAmount)
    {
        public long Sequence { get; } = Interlocked.Increment(ref _sequence);

        /// <summary>The path PalPluss would call back on, e.g. <c>/webhooks/palpluss/{token}</c>.</summary>
        public string CallbackPath => Request.CallbackUrl.AbsolutePath;
    }

    private static long _sequence;
}
