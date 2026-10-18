using Commerce.Application.Abstractions;
using Commerce.Domain.Common;
using Common.Application.Commerce;

namespace Commerce.Application.Krafts;

/// <summary><see cref="IKraftDirectory" /> for other modules (Payments checks a till's Kraft here).</summary>
public sealed class KraftDirectory(IKraftRepository krafts) : IKraftDirectory
{
    public async Task<KraftAtEvent?> FindAtEventAsync(Guid kraftId, Guid eventId, CancellationToken cancellationToken)
    {
        var kraft = await krafts.FindForReadAsync(new KraftId(kraftId), cancellationToken);
        var terms = kraft?.AtEvent(eventId);

        return terms is null ? null : new KraftAtEvent(kraftId, kraft!.Name, eventId, terms.CommissionPercent);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> kraftIds, CancellationToken cancellationToken)
    {
        if (kraftIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var list = await krafts.ListByIdsForReadAsync(kraftIds, cancellationToken);

        return list.ToDictionary(x => x.Id.Value, x => x.Name);
    }
}
