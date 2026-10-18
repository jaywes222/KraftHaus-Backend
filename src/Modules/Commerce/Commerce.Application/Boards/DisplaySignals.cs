using Common.Application.Displays;
using Microsoft.Extensions.Logging;

namespace Commerce.Application.Boards;

/// <summary>
///     Tells display screens that the menu or the ready orders of an event changed (Spec 08 §10). Displays is optional:
///     with the module off there is no signal to call. Best effort: screens poll as well.
/// </summary>
public sealed partial class DisplaySignals(IEnumerable<IDisplayBoardSignal> signals, ILogger<DisplaySignals> logger)
{
    public Task OrdersChangedAsync(Guid eventId, CancellationToken cancellationToken) =>
        SendAsync(eventId, (signal, ct) => signal.OrdersChangedAsync(eventId, ct), cancellationToken);

    public Task MenuChangedAsync(Guid eventId, CancellationToken cancellationToken) =>
        SendAsync(eventId, (signal, ct) => signal.MenuChangedAsync(eventId, ct), cancellationToken);

    private async Task SendAsync(Guid eventId, Func<IDisplayBoardSignal, CancellationToken, Task> send, CancellationToken cancellationToken)
    {
        foreach (var signal in signals)
        {
            try
            {
                await send(signal, cancellationToken);
            }
#pragma warning disable CA1031 // A screen refresh must never fail a sale or a kitchen action.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogSignalFailed(logger, eventId, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Display signal for event {EventId} failed.")]
    private static partial void LogSignalFailed(ILogger logger, Guid eventId, Exception exception);
}
