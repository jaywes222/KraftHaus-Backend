using Microsoft.EntityFrameworkCore;
using Tenancy.Application.Abstractions;

namespace Tenancy.Infrastructure.Database;

public sealed class ModuleDatabaseRegistry : IModuleDatabaseRegistry
{
    private readonly object _sync = new();
    private readonly List<ModuleDatabaseDescriptor> _descriptors = [];

    public IReadOnlyCollection<ModuleDatabaseDescriptor> GetAll()
    {
        lock (_sync)
        {
            return _descriptors.ToArray();
        }
    }

    public void Register(ModuleDatabaseDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_sync)
        {
            if (_descriptors.Any(x => x.ModuleName == descriptor.ModuleName && x.Scope == descriptor.Scope))
            {
                throw new InvalidOperationException($"Database module '{descriptor.ModuleName}' is already registered for scope '{descriptor.Scope}'.");
            }

            _descriptors.Add(descriptor);
        }
    }
}
