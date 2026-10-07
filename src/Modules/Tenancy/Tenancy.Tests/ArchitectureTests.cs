using System.Xml.Linq;
using Xunit;

namespace Tenancy.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void TenancyModuleDoesNotReferenceOtherModuleProjects()
    {
        var projectFiles = Directory.GetFiles(FindRepositoryRoot(), "*.csproj", SearchOption.AllDirectories);
        var tenancyProjects = projectFiles.Where(x => x.Contains(Path.Combine("Modules", "Tenancy"), StringComparison.OrdinalIgnoreCase));

        foreach (var projectFile in tenancyProjects)
        {
            var project = XDocument.Load(projectFile);
            var references = project.Descendants("ProjectReference")
                .Select(x => (string?)x.Attribute("Include"))
                .Where(x => x is not null)
                .Select(x => x!)
                .ToArray();

            Assert.DoesNotContain(references, x => x.Contains(Path.Combine("Modules", ""), StringComparison.OrdinalIgnoreCase) && !x.Contains(Path.Combine("Modules", "Tenancy"), StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
