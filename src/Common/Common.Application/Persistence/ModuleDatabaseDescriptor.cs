using Microsoft.EntityFrameworkCore;

namespace Common.Application.Persistence;

public sealed record ModuleDatabaseDescriptor(
    string ModuleName,
    DatabaseScope Scope,
    Func<string, DbContext> CreateDbContext);
