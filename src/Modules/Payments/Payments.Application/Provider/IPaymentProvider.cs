using Payments.Domain.Payments;

namespace Payments.Application.Provider;

/// <summary>The M-Pesa aggregator (PalPluss). One STK push per call: never retried automatically, each retry is a new prompt.</summary>
public interface IPaymentProvider
{
    Task<ProviderStartResult> StartStkAsync(ProviderStkRequest request, CancellationToken cancellationToken);

    Task<ProviderLookup> GetTransactionAsync(string transactionId, CancellationToken cancellationToken);
}

/// <param name="Phone">E.164.</param>
public sealed record ProviderStkRequest(
    decimal Amount,
    string Phone,
    string AccountReference,
    string Description,
    Uri CallbackUrl,
    Guid? ChannelId = null);

public abstract record ProviderStartResult
{
    /// <summary>The prompt was sent.</summary>
    public sealed record Accepted(string TransactionId, string? CheckoutId) : ProviderStartResult;

    /// <summary>The provider said no (balance, rate limit, channel, phone). Nothing was sent.</summary>
    public sealed record Refused(string ErrorCode, string? Message, int? RetryAfterSeconds) : ProviderStartResult;

    /// <summary>No answer (timeout, network). The prompt may or may not have been sent.</summary>
    public sealed record Unknown(string Reason) : ProviderStartResult;
}

public abstract record ProviderLookup
{
    public sealed record Found(ProviderRecord Record) : ProviderLookup;

    public sealed record NotFound : ProviderLookup;

    public sealed record Unavailable(string Reason) : ProviderLookup;
}
