using Tenancy.Application.Abstractions;
using Tenancy.Domain.Common;
using Tenancy.Domain.Tenants;

namespace Tenancy.Infrastructure.Resolution;

public sealed class TenantContext : ITenantContext
{
    public TenantId TenantId { get; private set; }

    public IdentityScope IdentityScope { get; private set; }

    public bool IsResolved { get; private set; }

    internal void Set(TenantDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        Set(
            descriptor.TenantId,
            descriptor.IdentityScope);
    }

    internal void Set(
        TenantId tenantId,
        IdentityScope identityScope)
    {
        TenantId = tenantId;
        IdentityScope = identityScope;
        IsResolved = true;
    }

    internal void Clear()
    {
        TenantId = default;
        IdentityScope = default;
        IsResolved = false;
    }
}

public sealed class TenantScopeFactory(
    TenantContext context) : ITenantScopeFactory
{
    public ValueTask<IDisposable> CreateAsync(
        TenantDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        cancellationToken.ThrowIfCancellationRequested();

        TenantContextSnapshot? previous =
            context.IsResolved
                ? new TenantContextSnapshot(
                    context.TenantId,
                    context.IdentityScope)
                : null;

        context.Set(descriptor);

        // Ownership of the Scope is intentionally transferred
        // to the caller through IDisposable.
#pragma warning disable CA2000
        return ValueTask.FromResult<IDisposable>(
            new Scope(context, previous));
#pragma warning restore CA2000
    }

    private sealed class Scope(
        TenantContext context,
        TenantContextSnapshot? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (previous is null)
            {
                context.Clear();
                return;
            }

            context.Set(
                previous.Value.TenantId,
                previous.Value.IdentityScope);
        }
    }

    private readonly record struct TenantContextSnapshot(
        TenantId TenantId,
        IdentityScope IdentityScope);
}
