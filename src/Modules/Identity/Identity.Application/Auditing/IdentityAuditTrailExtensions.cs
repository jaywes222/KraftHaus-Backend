using Common.Domain.ResultMonad;

namespace Identity.Application.Auditing;

/// <summary>
///     Pipeline helpers that record a refused request after the command has run, leaving the result untouched.
/// </summary>
public static class IdentityAuditTrailExtensions
{
    public static async Task<Result<T>> AuditRejectionAsync<T>(
        this Task<Result<T>> resultTask,
        IIdentityAuditTrail auditTrail,
        Func<Error, IdentityAuditRejection> rejection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(rejection);

        var result = await resultTask;

        if (result.IsFailure)
        {
            await auditTrail.RecordRejectedAsync(
                rejection(result.Error!),
                cancellationToken);
        }

        return result;
    }

    public static async Task<Result> AuditRejectionAsync(
        this Task<Result> resultTask,
        IIdentityAuditTrail auditTrail,
        Func<Error, IdentityAuditRejection> rejection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(rejection);

        var result = await resultTask;

        if (result.IsFailure)
        {
            await auditTrail.RecordRejectedAsync(
                rejection(result.Error!),
                cancellationToken);
        }

        return result;
    }
}
