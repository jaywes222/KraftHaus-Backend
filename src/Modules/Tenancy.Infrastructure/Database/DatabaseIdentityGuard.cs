using Common.Application.Persistence;
using Tenancy.Application.Abstractions;

namespace Tenancy.Infrastructure.Database;

public static class DatabaseIdentityGuard
{
    public static void Validate(
        string existingKind,
        Guid? existingTenantId,
        string existingDatabaseName,
        DatabaseScope expectedScope,
        Guid? expectedTenantId,
        string expectedDatabaseName)
    {
        var expectedKind = expectedScope == DatabaseScope.Platform ? "Platform" : "Tenant";
        if (!string.Equals(existingKind, expectedKind, StringComparison.Ordinal) ||
            existingTenantId != expectedTenantId ||
            !string.Equals(existingDatabaseName, expectedDatabaseName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Database identity guard failed for '{expectedDatabaseName}'. Existing identity does not match the requested database scope.");
        }
    }
}
