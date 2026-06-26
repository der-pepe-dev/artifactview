using ArtifactView.Contracts.Analysis;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Plugins;
using ArtifactView.Plugins.Abstractions;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Plugins;

// A minimal IAnalyzer used as the activation target in happy-path tests.
// Lives in this assembly so TryActivate can load it via AssemblyLoadContext.Default.
internal sealed class TestAnalyzerPlugin : IAnalyzer
{
    public string Id           => "test-activation";
    public string DisplayName  => "Test Activation Plugin";
    public int    CostHint     => 0;
    public bool Supports(IAnalyzerContext context) => false;
    public ValueTask<IReadOnlyList<Finding>> AnalyzeAsync(IAnalyzerContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult<IReadOnlyList<Finding>>([]);
}

public sealed class PluginActivatorTests
{
    private static PluginManifest Make(
        string? assemblyName   = null,
        string? entryTypeName  = null,
        string? manifestDir    = null) => new()
    {
        Id                = "test-plugin",
        Name              = "Test Plugin",
        Version           = "1.0.0",
        Author            = "Tester",
        License           = "MIT",
        AssemblyName      = assemblyName,
        EntryTypeName     = entryTypeName,
        ManifestDirectory = manifestDir
    };

    [Test]
    public void TryActivate_NoAssemblyName_ReturnsNull()
    {
        var result = new PluginActivator().TryActivate<IAnalyzer>(
            Make(assemblyName: null, entryTypeName: "Some.Type", manifestDir: "/tmp"));

        Assert.Null(result);
    }

    [Test]
    public void TryActivate_NoEntryTypeName_ReturnsNull()
    {
        var result = new PluginActivator().TryActivate<IAnalyzer>(
            Make(assemblyName: "Some.dll", entryTypeName: null, manifestDir: "/tmp"));

        Assert.Null(result);
    }

    [Test]
    public void TryActivate_NoManifestDirectory_ReturnsNull()
    {
        var result = new PluginActivator().TryActivate<IAnalyzer>(
            Make(assemblyName: "Some.dll", entryTypeName: "Some.Type", manifestDir: null));

        Assert.Null(result);
    }

    [Test]
    public void TryActivate_AssemblyFileNotFound_ReturnsNull()
    {
        var result = new PluginActivator().TryActivate<IAnalyzer>(
            Make(assemblyName: "NonExistent.dll", entryTypeName: "Some.Type", manifestDir: Path.GetTempPath()));

        Assert.Null(result);
    }

    [Test]
    public void TryActivate_TypeNotFoundInAssembly_ReturnsNull()
    {
        var assemblyPath = typeof(PluginActivatorTests).Assembly.Location;
        var result = new PluginActivator().TryActivate<IAnalyzer>(
            Make(assemblyName: Path.GetFileName(assemblyPath),
                 entryTypeName: "NonExistent.Fake.TypeName",
                 manifestDir: Path.GetDirectoryName(assemblyPath)));

        Assert.Null(result);
    }

    [Test]
    public void TryActivate_TypeDoesNotImplementInterface_ReturnsNull()
    {
        var assemblyPath = typeof(PluginActivatorTests).Assembly.Location;
        // PluginActivatorTests itself is in the assembly but is not an IAnalyzer.
        var result = new PluginActivator().TryActivate<IAnalyzer>(
            Make(assemblyName: Path.GetFileName(assemblyPath),
                 entryTypeName: typeof(PluginActivatorTests).FullName,
                 manifestDir: Path.GetDirectoryName(assemblyPath)));

        Assert.Null(result);
    }

    [Test]
    public async Task TryActivate_ValidPlugin_ReturnsInstance()
    {
        var assemblyPath = typeof(PluginActivatorTests).Assembly.Location;
        var result = new PluginActivator().TryActivate<IAnalyzer>(
            Make(assemblyName: Path.GetFileName(assemblyPath),
                 entryTypeName: typeof(TestAnalyzerPlugin).FullName,
                 manifestDir: Path.GetDirectoryName(assemblyPath)));

        Assert.NotNull(result);
        await Assert.That(result!.Id).IsEqualTo("test-activation");
    }
}