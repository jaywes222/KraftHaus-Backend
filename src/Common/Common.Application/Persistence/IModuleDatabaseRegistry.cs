namespace Common.Application.Persistence;

public interface IModuleDatabaseRegistry
{
    IReadOnlyCollection<ModuleDatabaseDescriptor> GetAll();

    void Register(ModuleDatabaseDescriptor descriptor);
}
