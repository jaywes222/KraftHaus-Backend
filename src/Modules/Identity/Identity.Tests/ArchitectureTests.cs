using System.Xml.Linq;
using Xunit;

namespace Identity.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void IdentityProjectsReferenceOnlyCommonAndIdentityProjects()
    {
        var root = FindRepositoryRoot();
        var allowedRoots = new[]
        {
            Path.Combine(root, "src", "Common") + Path.DirectorySeparatorChar,
            Path.Combine(root, "src", "Modules", "Identity") + Path.DirectorySeparatorChar
        };

        var projectFiles = Directory.GetFiles(
            Path.Combine(root, "src", "Modules", "Identity"),
            "*.csproj",
            SearchOption.AllDirectories);

        Assert.NotEmpty(projectFiles);

        foreach (var projectFile in projectFiles)
        {
            foreach (var reference in ResolveProjectReferences(projectFile))
            {
                Assert.True(
                    allowedRoots.Any(allowed => reference.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)),
                    $"{Path.GetFileName(projectFile)} references {reference}, outside src/Common and the Identity module.");
            }
        }
    }

    [Fact]
    public void IdentityInfrastructureReferencesApplicationLayer()
    {
        var root = FindRepositoryRoot();
        var projectFile = Path.Combine(
            root,
            "src",
            "Modules",
            "Identity",
            "Identity.Infrastructure",
            "Identity.Infrastructure.csproj");

        var expected = Path.Combine(
            root,
            "src",
            "Modules",
            "Identity",
            "Identity.Application",
            "Identity.Application.csproj");

        Assert.Contains(
            ResolveProjectReferences(projectFile),
            x => string.Equals(x, expected, StringComparison.OrdinalIgnoreCase));
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
    public void InitialIdentityMigrationIsCheckedIn()
    {
        var root = FindRepositoryRoot();

        var migration = Path.Combine(
            root,
            "src",
            "Modules",
            "Identity",
            "Identity.Infrastructure",
            "Persistence",
            "Migrations",
            "20261007115231_InitialIdentity.cs");

        Assert.True(File.Exists(migration));
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
