using System.Xml.Linq;
using Common.Application.Persistence;
using Displays.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Displays.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void DisplaysProjectsReferenceOnlyCommonAndDisplaysProjects()
    {
        var root = FindRepositoryRoot();
        var allowedRoots = new[]
        {
            Path.Combine(root, "src", "Common") + Path.DirectorySeparatorChar,
            Path.Combine(root, "src", "Modules", "Displays") + Path.DirectorySeparatorChar
        };

        var projectFiles = Directory.GetFiles(
            Path.Combine(root, "src", "Modules", "Displays"),
            "*.csproj",
            SearchOption.AllDirectories);

        Assert.NotEmpty(projectFiles);

        foreach (var projectFile in projectFiles)
        {
            foreach (var reference in ResolveProjectReferences(projectFile))
            {
                Assert.True(
                    allowedRoots.Any(allowed => reference.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)),
                    $"{Path.GetFileName(projectFile)} references {reference}, outside src/Common and the Displays module.");
            }
        }
    }

    /// <summary>
    ///     Resolves ProjectReference paths to absolute, platform-normalised paths. Project files use Windows
    ///     separators; normalising them keeps these checks meaningful on Linux CI as well.
    /// </summary>
    private static string[] ResolveProjectReferences(string projectFile)
    {
        var directory = Path.GetDirectoryName(projectFile)!;

        return XDocument.Load(projectFile)
            .Descendants("ProjectReference")
            .Select(x => (string?)x.Attribute("Include"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Path.GetFullPath(
                Path.Combine(
                    directory,
                    x!.Replace('\\', Path.DirectorySeparatorChar))))
            .ToArray();
    }

    [Fact]
    public void DisplaysStoreIsTenantScoped()
    {
        using var services = new ServiceCollection()
            .AddDisplaysMigrations()
            .BuildServiceProvider();

        var descriptor = Assert.Single(services.GetServices<ModuleDatabaseDescriptor>());
        Assert.Equal(DatabaseScope.Tenant, descriptor.Scope);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(
                   Path.Combine(
                       directory.FullName,
                       "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Repository root not found.");
    }
}
