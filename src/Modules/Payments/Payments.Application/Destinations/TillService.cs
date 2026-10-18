using Common.Application.Commerce;
using Common.Application.Payments;
using Common.Domain.Phones;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Common.IntegrationEvents.Payments;
using Microsoft.Extensions.Options;
using Payments.Application.Abstractions;
using Payments.Application.Payments;
using Payments.Domain.Common;
using Payments.Domain.Destinations;

namespace Payments.Application.Destinations;

/// <summary>
///     Kraft tills (UF-017, Spec 08 §7.1): the Kraft submits its till or paybill for an event; Finance creates the
///     matching PalPluss payment channel, runs a small test payment through it, and approves the till once the test
///     passes. Only approved tills take Kraft sales.
/// </summary>
public sealed class TillService(
    IPaymentDestinationRepository destinations,
    IPaymentsUnitOfWork unitOfWork,
    IKraftDirectory krafts,
    IMobilePayments mobilePayments,
    IOptions<PaymentsOptions> options,
    TimeProvider time)
{
    private const int MaxListLimit = 200;

    /// <summary>What the platform till is called where a Kraft name would show.</summary>
    public const string PlatformName = "KRAFTHAUS";

    public async Task<Result<TillView>> SubmitAsync(
        Guid kraftId,
        SubmitTillRequest request,
        ApplicationUserId submittedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kraft = await krafts.FindAtEventAsync(kraftId, request.EventId, cancellationToken);
        if (kraft is null)
        {
            return new Error
            {
                Key = "Payments.KraftNotAtEvent",
                Description = "This Kraft does not take part in that event.",
                StatusCode = System.Net.HttpStatusCode.NotFound
            };
        }

        if (await destinations.FindActiveAsync(kraftId, request.EventId, cancellationToken) is not null)
        {
            return TillAlreadySubmitted;
        }

        var submitted = PaymentDestination.SubmitKraftTill(
            new PaymentDestinationId(Guid.CreateVersion7()),
            kraftId,
            request.EventId,
            request.Name,
            request.ShortcodeType,
            request.Shortcode,
            request.AccountNumber,
            submittedBy.Value,
            time.GetUtcNow());

        if (submitted.IsFailure)
        {
            return submitted.Error!;
        }

        destinations.Add(submitted.Value!);

        var saved = await unitOfWork.TrySaveChangesAsync(cancellationToken);

        return saved.IsFailure ? saved.Error! : TillView.From(submitted.Value!, kraft.Name);
    }

    /// <summary>Sets the KRAFTHAUS till or paybill. It takes money once Finance has tested and approved it.</summary>
    public async Task<Result<TillView>> SubmitPlatformAsync(
        SubmitPlatformTillRequest request,
        ApplicationUserId submittedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await destinations.FindActiveAsync(Guid.Empty, Guid.Empty, cancellationToken) is { } active)
        {
            return active.Status == DestinationStatus.Approved
                ? PaymentsErrors.InvalidState("PlatformTillActive", "Disable the current KRAFTHAUS till before setting a new one.")
                : PaymentsErrors.InvalidState("PlatformTillPending", "A new KRAFTHAUS till is already waiting for its test. Test and approve it, or reject it.");
        }

        var submitted = PaymentDestination.SubmitPlatformTill(
            new PaymentDestinationId(Guid.CreateVersion7()),
            request.Name,
            request.ShortcodeType,
            request.Shortcode,
            request.AccountNumber,
            submittedBy.Value,
            time.GetUtcNow());

        if (submitted.IsFailure)
        {
            return submitted.Error!;
        }

        destinations.Add(submitted.Value!);

        var saved = await unitOfWork.TrySaveChangesAsync(cancellationToken);

        return saved.IsFailure ? saved.Error! : TillView.From(submitted.Value!, PlatformName);
    }

    /// <summary>The KRAFTHAUS tills, newest first: the one in use (or waiting) and the ones before it.</summary>
    public async Task<Result<IReadOnlyList<TillView>>> ListPlatformAsync(CancellationToken cancellationToken)
    {
        var tills = await destinations.ListPlatformTillsAsync(20, cancellationToken);

        return Result<IReadOnlyList<TillView>>.Success([.. tills.Select(x => TillView.From(x, PlatformName))]);
    }

    public async Task<Result<IReadOnlyList<TillView>>> ListForKraftAsync(Guid kraftId, CancellationToken cancellationToken)
    {
        var tills = await destinations.ListForKraftAsync(kraftId, cancellationToken);
        var names = await krafts.NamesAsync([kraftId], cancellationToken);

        return Result<IReadOnlyList<TillView>>.Success(
            [.. tills.Select(x => TillView.From(x, names.GetValueOrDefault(x.KraftId)))]);
    }

    public async Task<Result<IReadOnlyList<TillView>>> ListAsync(
        DestinationStatus? status,
        Guid? eventId,
        int? limit,
        CancellationToken cancellationToken)
    {
        var tills = await destinations.ListAsync(status, eventId, Math.Clamp(limit ?? 50, 1, MaxListLimit), cancellationToken);
        var names = await krafts.NamesAsync([.. tills.Where(x => !x.IsPlatform).Select(x => x.KraftId).Distinct()], cancellationToken);

        return Result<IReadOnlyList<TillView>>.Success(
            [.. tills.Select(x => TillView.From(x, x.IsPlatform ? PlatformName : names.GetValueOrDefault(x.KraftId)))]);
    }

    public async Task<Result<TillView>> GetAsync(PaymentDestinationId id, CancellationToken cancellationToken)
    {
        var till = await destinations.FindAsync(id, cancellationToken);

        return till is null ? PaymentsErrors.TillNotFound(id.Value) : await ViewAsync(till, cancellationToken);
    }

    /// <summary>
    ///     Records the PalPluss channel and sends <see cref="PaymentsOptions.TillTestAmount" /> through it to the tester's
    ///     phone. The outcome arrives like any payment (callback or status job) and is recorded on the till.
    /// </summary>
    public async Task<Result<TillView>> TestAsync(
        PaymentDestinationId id,
        TestTillRequest request,
        string callbackHost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!KenyanMobile.TryCreate(request.Phone, out var phone))
        {
            return PaymentsErrors.Fields("TillTestInvalid", ["Phone: Enter the Kenyan mobile number that will receive the test prompt."]);
        }

        var till = await destinations.FindAsync(id, cancellationToken);
        if (till is null)
        {
            return PaymentsErrors.TillNotFound(id.Value);
        }

        var paymentId = Guid.CreateVersion7();
        var started = till.StartTest(request.ChannelId, paymentId, time.GetUtcNow());
        if (started.IsFailure)
        {
            return started.Error!;
        }

        var saved = await unitOfWork.TrySaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        var payment = await mobilePayments.StartAsync(
            new StartMobilePayment(
                paymentId,
                PaymentPurpose.TillTest,
                till.Id.Value,
                options.Value.TillTestAmount,
                phone!.Value,
                "TILLTEST",
                "Till test",
                callbackHost,
                till.Id.Value),
            cancellationToken);

        if (payment.Status == MobilePaymentStatus.Failed)
        {
            // Refused before a prompt went out (channel, rate limit): the test failed on the spot.
            till.RecordTestResult(paymentId, passed: false, time.GetUtcNow());
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }

        return await ViewAsync(till, cancellationToken);
    }

    public Task<Result<TillView>> ApproveAsync(PaymentDestinationId id, ApplicationUserId decidedBy, CancellationToken cancellationToken) =>
        DecideAsync(id, till => till.Approve(decidedBy.Value, time.GetUtcNow()), cancellationToken);

    public Task<Result<TillView>> RejectAsync(PaymentDestinationId id, TillDecisionRequest request, ApplicationUserId decidedBy, CancellationToken cancellationToken) =>
        DecideAsync(id, till => till.Reject(request?.Note, decidedBy.Value, time.GetUtcNow()), cancellationToken);

    public Task<Result<TillView>> DisableAsync(PaymentDestinationId id, TillDecisionRequest request, ApplicationUserId decidedBy, CancellationToken cancellationToken) =>
        DecideAsync(id, till => till.Disable(request?.Note, decidedBy.Value, time.GetUtcNow()), cancellationToken);

    private async Task<Result<TillView>> DecideAsync(
        PaymentDestinationId id,
        Func<PaymentDestination, Result> decide,
        CancellationToken cancellationToken)
    {
        var till = await destinations.FindAsync(id, cancellationToken);
        if (till is null)
        {
            return PaymentsErrors.TillNotFound(id.Value);
        }

        var decided = decide(till);
        if (decided.IsFailure)
        {
            return decided.Error!;
        }

        var saved = await unitOfWork.TrySaveChangesAsync(cancellationToken);

        return saved.IsFailure ? saved.Error! : await ViewAsync(till, cancellationToken);
    }

    private async Task<Result<TillView>> ViewAsync(PaymentDestination till, CancellationToken cancellationToken)
    {
        if (till.IsPlatform)
        {
            return TillView.From(till, PlatformName);
        }

        var names = await krafts.NamesAsync([till.KraftId], cancellationToken);
        return TillView.From(till, names.GetValueOrDefault(till.KraftId));
    }

    private static Error TillAlreadySubmitted =>
        PaymentsErrors.InvalidState("TillAlreadySubmitted", "This Kraft already has a till waiting for approval or approved for this event.");
}

/// <summary>Body of <c>POST /api/admin/platform-till</c>: the KRAFTHAUS till or paybill.</summary>
public sealed record SubmitPlatformTillRequest(
    string? Name,
    ShortcodeType ShortcodeType,
    string? Shortcode,
    string? AccountNumber);

public sealed record SubmitTillRequest(
    Guid EventId,
    string? Name,
    ShortcodeType ShortcodeType,
    string? Shortcode,
    string? AccountNumber);

/// <param name="ChannelId">The PalPluss payment channel created for this till.</param>
/// <param name="Phone">The phone that receives the test prompt (Finance's own).</param>
public sealed record TestTillRequest(Guid ChannelId, string? Phone);

public sealed record TillDecisionRequest(string? Note);

public sealed record TillView(
    Guid Id,
    string Kind,
    Guid KraftId,
    string? KraftName,
    Guid EventId,
    string Name,
    string ShortcodeType,
    string Shortcode,
    string? AccountNumber,
    string Status,
    Guid? ChannelId,
    string TestStatus,
    Guid? TestPaymentId,
    DateTimeOffset? TestedAt,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? DecidedAt,
    string? DecisionNote)
{
    public static TillView From(PaymentDestination till, string? kraftName)
    {
        ArgumentNullException.ThrowIfNull(till);

        return new TillView(
            till.Id.Value,
            till.Kind.ToString(),
            till.KraftId,
            kraftName,
            till.EventId,
            till.Name,
            till.ShortcodeType.ToString(),
            till.Shortcode,
            till.AccountNumber,
            till.Status.ToString(),
            till.ProviderChannelId,
            till.TestStatus.ToString(),
            till.TestPaymentId,
            till.TestedAt,
            till.SubmittedAt,
            till.DecidedAt,
            till.DecisionNote);
    }
}
