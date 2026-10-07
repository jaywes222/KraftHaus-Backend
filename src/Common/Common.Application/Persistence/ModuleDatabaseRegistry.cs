namespace Common.Application.Persistence;

public sealed class ModuleDatabaseRegistry(
    IEnumerable<ModuleDatabaseDescriptor> descriptors)
    : IModuleDatabaseRegistry
{
    private readonly Lock _sync = new();

    private readonly List<ModuleDatabaseDescriptor> _descriptors =
        [.. descriptors];

    public IReadOnlyCollection<ModuleDatabaseDescriptor> GetAll()
    {
        lock (_sync)
        {
            return [.. _descriptors];
        }
    }

    public void Register(ModuleDatabaseDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        lock (_sync)
        {
            if (_descriptors.Any(
                    x =>
                        x.ModuleName == descriptor.ModuleName &&
                        x.Scope == descriptor.Scope))
            {
                throw new InvalidOperationException(
                    $"Database module '{descriptor.ModuleName}' is already " +
                    $"registered for scope '{descriptor.Scope}'.");
            }

            _descriptors.Add(descriptor);
        }
    }
}
