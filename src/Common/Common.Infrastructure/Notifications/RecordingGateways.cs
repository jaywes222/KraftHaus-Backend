using Common.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Common.Infrastructure.Notifications;

/// <summary>
///     Wraps the registered <see cref="ISmsGateway" /> and <see cref="IEmailGateway" /> so every transactional send is
///     recorded by Communications (<see cref="IMessageRecorder" />, when that module runs in this process). Recording
///     is best effort: it never changes what the caller sees, and a send failure is still thrown to the caller.
/// </summary>
public static class RecordingGatewayRegistration
{
    public static IServiceCollection AddMessageRecording(this IServiceCollection services)
    {
        Decorate<ISmsGateway>(services, (inner, sp) => new RecordingSmsGateway(
            inner, sp, sp.GetRequiredService<ILogger<RecordingSmsGateway>>()));
        Decorate<IEmailGateway>(services, (inner, sp) => new RecordingEmailGateway(
            inner, sp, sp.GetRequiredService<ILogger<RecordingEmailGateway>>()));

        return services;
    }

    private static void Decorate<TService>(IServiceCollection services, Func<TService, IServiceProvider, TService> wrap)
        where TService : class
    {
        var descriptor = services.LastOrDefault(x => x.ServiceType == typeof(TService) && !x.IsKeyedService);
        if (descriptor is null)
        {
            return;
        }

        services.Remove(descriptor);
        services.Add(ServiceDescriptor.Describe(
            typeof(TService),
            sp => wrap(Create<TService>(descriptor, sp), sp),
            descriptor.Lifetime));
    }

    private static TService Create<TService>(ServiceDescriptor descriptor, IServiceProvider sp)
        where TService : class
    {
        if (descriptor.ImplementationInstance is TService instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            return (TService)factory(sp);
        }

        return (TService)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
    }
}

public sealed partial class RecordingSmsGateway(
    ISmsGateway inner,
    IServiceProvider services,
    ILogger<RecordingSmsGateway> logger)
    : ISmsGateway
{
    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken) =>
        SendWithReceiptAsync(message, cancellationToken);

    public async Task<SmsReceipt> SendWithReceiptAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        SmsReceipt receipt;
        try
        {
            receipt = await inner.SendWithReceiptAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordAsync(message, null, ex.GetType().Name);
            throw;
        }

        await RecordAsync(message, receipt, null);
        return receipt;
    }

    private async Task RecordAsync(SmsMessage message, SmsReceipt? receipt, string? error)
    {
        if (MessageRecording.IsSuppressed || services.GetService<IMessageRecorder>() is not { } recorder)
        {
            return;
        }

        try
        {
            // Not the caller's token: the send already happened, the row should be written even if the caller gives up.
            await recorder.RecordSmsAsync(message, receipt, error, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogRecordFailed(logger, ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not record a sent SMS ({ErrorType}); the message itself was not affected.")]
    private static partial void LogRecordFailed(ILogger logger, string errorType);
}

public sealed partial class RecordingEmailGateway(
    IEmailGateway inner,
    IServiceProvider services,
    ILogger<RecordingEmailGateway> logger)
    : IEmailGateway
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            await inner.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordAsync(message, ex.GetType().Name);
            throw;
        }

        await RecordAsync(message, null);
    }

    private async Task RecordAsync(EmailMessage message, string? error)
    {
        if (MessageRecording.IsSuppressed || services.GetService<IMessageRecorder>() is not { } recorder)
        {
            return;
        }

        try
        {
            await recorder.RecordEmailAsync(message, error, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogRecordFailed(logger, ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not record a sent e-mail ({ErrorType}); the message itself was not affected.")]
    private static partial void LogRecordFailed(ILogger logger, string errorType);
}
