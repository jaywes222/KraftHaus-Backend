using Common.Application.Communications;
using Common.Application.EventCatalog;
using Common.Domain.ResultMonad;
using Communications.Application.Abstractions;
using Communications.Domain.Broadcasts;
using Communications.Domain.Common;
using Communications.Domain.Messages;
using Microsoft.Extensions.Options;

namespace Communications.Application.Broadcasts;

/// <summary>Broadcasts (UF-020): write, preview, schedule or cancel. Sending is <see cref="BroadcastDispatchJob" />'s.</summary>
public sealed class BroadcastService(
    IBroadcastRepository broadcasts,
    IOptOutRepository optOuts,
    ICommunicationsUnitOfWork unitOfWork,
    IEventCatalog events,
    IAudienceDirectory audiences,
    IOptions<CommunicationsOptions> options,
    TimeProvider time)
{
    public async Task<Result<IReadOnlyList<BroadcastView>>> ListAsync(Guid? eventId, CancellationToken cancellationToken)
    {
        var list = await broadcasts.ListAsync(eventId, cancellationToken);
        return Result<IReadOnlyList<BroadcastView>>.Success([.. list.Select(BroadcastView.From)]);
    }

    public async Task<Result<BroadcastView>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await broadcasts.FindAsync(new BroadcastId(id), cancellationToken) is { } broadcast
            ? BroadcastView.From(broadcast)
            : CommunicationsErrors.BroadcastNotFound;

    public async Task<Result<BroadcastView>> CreateAsync(SaveBroadcastRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.EventId != Guid.Empty && await events.GetSaleSnapshotAsync(input.EventId, cancellationToken) is null)
        {
            return CommunicationsErrors.EventNotFound;
        }

        var created = Broadcast.Create(
            new BroadcastId(Guid.CreateVersion7(time.GetUtcNow())), input.Name, input.EventId, input.Audience, input.Body);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        broadcasts.Add(created.Value!);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : BroadcastView.From(created.Value!);
    }

    public Task<Result<BroadcastView>> UpdateAsync(Guid id, SaveBroadcastRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ChangeAsync(id, b => b.Update(input.Name, input.Audience, input.Body), cancellationToken);
    }

    public Task<Result<BroadcastView>> ScheduleAsync(Guid id, ScheduleBroadcastRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ChangeAsync(id, b => b.Schedule(input.SendAt, time.GetUtcNow(), options.Value.MaxScheduleDays), cancellationToken);
    }

    public Task<Result<BroadcastView>> CancelAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, b => b.Cancel(time.GetUtcNow()), cancellationToken);

    /// <summary>How many it would reach right now, after opt-outs, and the text a recipient would get.</summary>
    public async Task<Result<AudiencePreviewView>> PreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var broadcast = await broadcasts.FindAsync(new BroadcastId(id), cancellationToken);
        if (broadcast is null)
        {
            return CommunicationsErrors.BroadcastNotFound;
        }

        var members = await audiences.ListAsync(broadcast.EventId, broadcast.Audience, cancellationToken);
        var phones = members.Select(x => x.Phone).Distinct(StringComparer.Ordinal).ToList();
        var optedOut = broadcast.IsMarketing ? (await optOuts.ActiveAmongAsync(phones, cancellationToken)).Count : 0;
        var sample = broadcast.RenderFor(members.Count > 0 ? members[0].FirstName : null, options.Value.MarketingFooter);

        return new AudiencePreviewView(
            broadcast.Audience,
            broadcast.IsMarketing,
            phones.Count,
            optedOut,
            phones.Count - optedOut,
            sample,
            MessageBody.SmsParts(sample));
    }

    private async Task<Result<BroadcastView>> ChangeAsync(Guid id, Func<Broadcast, Result> change, CancellationToken cancellationToken)
    {
        var broadcast = await broadcasts.FindAsync(new BroadcastId(id), cancellationToken);
        if (broadcast is null)
        {
            return CommunicationsErrors.BroadcastNotFound;
        }

        var changed = change(broadcast);
        if (changed.IsFailure)
        {
            return changed.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : BroadcastView.From(broadcast);
    }
}
