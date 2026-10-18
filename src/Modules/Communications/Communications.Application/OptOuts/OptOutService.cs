using Common.Application.Customers;
using Common.Domain.Phones;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Communications.Application.Abstractions;
using Communications.Domain.Common;
using Communications.Domain.OptOuts;

namespace Communications.Application.OptOuts;

/// <summary>Marketing opt-outs: staff keep the list; a signed-in customer switches marketing off or on for their number.</summary>
public sealed class OptOutService(
    IOptOutRepository optOuts,
    ICommunicationsUnitOfWork unitOfWork,
    ICustomerDirectory customers,
    TimeProvider time)
{
    public const int MaxListed = 500;

    public async Task<Result<IReadOnlyList<OptOutView>>> ListAsync(CancellationToken cancellationToken)
    {
        var list = await optOuts.ListActiveAsync(MaxListed, cancellationToken);
        return Result<IReadOnlyList<OptOutView>>.Success([.. list.Select(OptOutView.From)]);
    }

    public async Task<Result<OptOutView>> AddAsync(AddOptOutRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!KenyanMobile.TryCreate(input.Phone, out var mobile))
        {
            return CommunicationsErrors.Invalid("OptOutInvalid", ["Phone: Enter a Kenyan mobile number."]);
        }

        var optOut = await SetAsync(mobile!.Value, optedOut: true, OptOutSource.Staff, cancellationToken);
        return optOut.IsFailure ? optOut.Error! : OptOutView.From(optOut.Value!);
    }

    public async Task<Result> RemoveAsync(string phone, CancellationToken cancellationToken)
    {
        if (!KenyanMobile.TryCreate(phone, out var mobile))
        {
            return CommunicationsErrors.Invalid("OptOutInvalid", ["Phone: Enter a Kenyan mobile number."]);
        }

        var existing = await optOuts.FindByPhoneAsync(mobile!.Value, cancellationToken);
        if (existing is not { IsActive: true })
        {
            return CommunicationsErrors.OptOutNotFound;
        }

        var set = await SetAsync(mobile.Value, optedOut: false, OptOutSource.Staff, cancellationToken);
        return set.IsFailure ? set.Error! : Result.Success;
    }

    public async Task<Result<MessagePreferencesView>> GetPreferencesAsync(ApplicationUserId userId, CancellationToken cancellationToken)
    {
        var phone = await PhoneOfAsync(userId, cancellationToken);
        if (phone is null)
        {
            return CommunicationsErrors.PhoneRequired;
        }

        var existing = await optOuts.FindByPhoneAsync(phone, cancellationToken);
        return new MessagePreferencesView(existing is not { IsActive: true });
    }

    public async Task<Result<MessagePreferencesView>> SetPreferencesAsync(
        ApplicationUserId userId,
        MessagePreferencesRequest input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var phone = await PhoneOfAsync(userId, cancellationToken);
        if (phone is null)
        {
            return CommunicationsErrors.PhoneRequired;
        }

        var set = await SetAsync(phone, optedOut: !input.Marketing, OptOutSource.Customer, cancellationToken);
        return set.IsFailure ? set.Error! : new MessagePreferencesView(input.Marketing);
    }

    private async Task<string?> PhoneOfAsync(ApplicationUserId userId, CancellationToken cancellationToken) =>
        (await customers.FindAsync(userId, cancellationToken))?.Phone is { Length: > 0 } phone ? phone : null;

    /// <returns>The row after the change; <c>null</c> when marketing is switched on for a number that never opted out.</returns>
    private async Task<Result<OptOut?>> SetAsync(string phone, bool optedOut, OptOutSource source, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var existing = await optOuts.FindByPhoneAsync(phone, cancellationToken);

        if (existing is null)
        {
            if (!optedOut)
            {
                return Result<OptOut?>.Success(null);
            }

            existing = OptOut.Create(new OptOutId(Guid.CreateVersion7(now)), phone, source, now);
            optOuts.Add(existing);
        }
        else if (optedOut)
        {
            existing.Activate(source, now);
        }
        else
        {
            existing.Deactivate(source, now);
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : Result<OptOut?>.Success(existing);
    }
}
