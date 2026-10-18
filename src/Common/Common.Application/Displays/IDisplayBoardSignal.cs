namespace Common.Application.Displays;

/// <summary>
///     Lets screens refresh as soon as something they show changes (implemented by Displays; called by Commerce). Best
///     effort: screens also re-fetch their feed on a timer, so a lost signal only delays an update.
/// </summary>
public interface IDisplayBoardSignal
{
    Task OrdersChangedAsync(Guid eventId, CancellationToken cancellationToken);

    Task MenuChangedAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>The song-request queue changed (Music).</summary>
    Task MusicChangedAsync(Guid eventId, CancellationToken cancellationToken);
}
