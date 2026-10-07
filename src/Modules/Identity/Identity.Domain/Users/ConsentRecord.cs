using Common.Domain.Entities;
using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;

namespace Identity.Domain.Users;

public sealed class ConsentRecord(ConsentRecordId id)
    : AuditableEntity<ConsentRecordId>(id)
{
    private ConsentRecord()
        : this(new ConsentRecordId(Guid.Empty))
    {
    }

    public required ApplicationUserId UserId { get; init; }

    public ConsentKind Kind { get; private set; }

    public bool Granted { get; private set; }

    public string PolicyKey { get; private set; } = default!;

    public int PolicyVersion { get; private set; }

    public string PolicyTextHash { get; private set; } = default!;

    public DateTimeOffset RecordedAt { get; private set; }

    public static ConsentRecord Create(
        ConsentRecordId id,
        ApplicationUserId userId,
        ConsentKind kind,
        bool granted,
        PolicyRef policy,
        DateTimeOffset recordedAt)
    {
        return new ConsentRecord(id)
        {
            UserId = userId,
            Kind = kind,
            Granted = granted,
            PolicyKey = policy.Key,
            PolicyVersion = policy.Version,
            PolicyTextHash = policy.TextHash,
            RecordedAt = recordedAt
        };
    }
}

public readonly record struct ConsentRecordId(Guid Value)
    : IStronglyTypedId;
