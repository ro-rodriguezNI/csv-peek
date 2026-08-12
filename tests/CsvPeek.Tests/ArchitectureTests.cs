using CsvPeek.Application;
using CsvPeek.Core;
using CsvPeek.Infrastructure;

namespace CsvPeek.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void ProjectDependenciesPointTowardCore()
    {
        string[] coreReferences = ReferencedProjects(typeof(CsvDialect).Assembly);
        string[] applicationReferences = ReferencedProjects(typeof(ICsvDocumentSession).Assembly);
        string[] infrastructureReferences = ReferencedProjects(typeof(CsvRecordSourceFactory).Assembly);

        Assert.Empty(coreReferences);
        Assert.Equal(["CsvPeek.Core"], applicationReferences);
        Assert.Equal(["CsvPeek.Application", "CsvPeek.Core"], infrastructureReferences);
    }

    private static string[] ReferencedProjects(System.Reflection.Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("CsvPeek.", StringComparison.Ordinal))
            .Order()
            .ToArray();
}
