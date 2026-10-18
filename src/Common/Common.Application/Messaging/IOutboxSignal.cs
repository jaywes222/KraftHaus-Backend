namespace Common.Application.Messaging;

/// <summary>
///     Wakes the outbox dispatcher early after a commit that wrote outbox rows, so a paid order turns into tickets in
///     about a second instead of waiting for the next poll.
/// </summary>
public interface IOutboxSignal
{
    void Notify();
}
