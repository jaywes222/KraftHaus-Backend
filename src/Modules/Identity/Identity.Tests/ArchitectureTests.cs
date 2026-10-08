using System.Xml.Linq;
using Xunit;

namespace Identity.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void IdentityProjectsDoNotReferenceOtherModuleProjects()
    {
        var root = FindRepositoryRoot();
        var projectFiles = Directory.GetFiles(
            Path.Combine(root, "src", "Modules", "Identity"),
            "*.csproj",
            SearchOption.AllDirectories);

        foreach (var projectFile in projectFiles)
        {
            var project = XDocument.Load(projectFile);
            var references = project
                .Descendants("ProjectReference")
                .Select(x => (string?)x.Attribute("Include"))
                .Where(x => x is not null)
                .Select(x => x!)
                .ToArray();

            Assert.DoesNotContain(
                references,
                x =>
                    x.Contains(
                        Path.Combine("Modules", "Tenancy"),
                        StringComparison.OrdinalIgnoreCase));
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

        var project = XDocument.Load(projectFile);

        var references = project
            .Descendants("ProjectReference")
            .Select(x => (string?)x.Attribute("Include"))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToArray();

        Assert.Contains(
            references,
            x => x.Contains(
                Path.Combine("Identity.Application", "Identity.Application.csproj"),
                StringComparison.OrdinalIgnoreCase));
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
