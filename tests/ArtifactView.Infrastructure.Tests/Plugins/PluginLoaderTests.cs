using System.IO;
using System.Text.Json;
using ArtifactView.Infrastructure.Plugins;
using ArtifactView.Plugins.Abstractions;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Plugins;

public sealed class PluginLoaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public PluginLoaderTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteManifest(string subDir, object manifest)
    {
        var dir = Path.Combine(_tempDir, subDir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "plugin.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest));
        return path;
    }

    [Test]
    public async Task Discover_NonExistentFolder_ReturnsEmpty()
    {
        var loader = new PluginLoader();
        var result = loader.Discover(Path.Combine(_tempDir, "does-not-exist"));
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Discover_EmptyFolder_ReturnsEmpty()
    {
        var loader = new PluginLoader();
        var result = loader.Discover(_tempDir);
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Discover_ValidManifest_ReturnsManifest()
    {
        WriteManifest("plugin-a", new
        {
            Id      = "test.plugin.a",
            Name    = "Test Plugin A",
            Version = "1.0.0",
            Author  = "Tester",
            License = "MIT"
        });

        var loader = new PluginLoader();
        var result = loader.Discover(_tempDir);

        await Assert.That(result).HasSingleItem();
        await Assert.That(result[0].Id).IsEqualTo("test.plugin.a");
        await Assert.That(result[0].Name).IsEqualTo("Test Plugin A");
    }

    [Test]
    public async Task Discover_MalformedManifest_SkipsBadAndReturnsGood()
    {
        // Bad manifest
        File.WriteAllText(Path.Combine(_tempDir, "plugin.json"), "{ not valid json }}}");

        // Good manifest in subdirectory
        WriteManifest("good-plugin", new
        {
            Id      = "test.plugin.good",
            Name    = "Good Plugin",
            Version = "2.0.0",
            Author  = "Author",
            License = "Apache-2.0"
        });

        var loader = new PluginLoader();
        var result = loader.Discover(_tempDir);

        await Assert.That(result).HasSingleItem();
        await Assert.That(result[0].Id).IsEqualTo("test.plugin.good");
    }

    [Test]
    public async Task Discover_NullDeserializationResult_SkipsManifest()
    {
        // Serializing 'null' produces the literal "null" which deserializes to null.
        var dir = Path.Combine(_tempDir, "null-plugin");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), "null");

        var loader = new PluginLoader();
        var result = loader.Discover(_tempDir);

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Discover_MultipleValidManifests_ReturnsAll()
    {
        for (var i = 0; i < 3; i++)
            WriteManifest($"plugin-{i}", new
            {
                Id      = $"test.plugin.{i}",
                Name    = $"Plugin {i}",
                Version = "1.0.0",
                Author  = "Author",
                License = "MIT"
            });

        var loader = new PluginLoader();
        var result = loader.Discover(_tempDir);

        await Assert.That(result.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Discover_SetsManifestDirectory_ToContainingFolder()
    {
        WriteManifest("my-plugin", new
        {
            Id = "test.dir", Name = "Dir Test", Version = "1.0", Author = "x", License = "MIT"
        });

        var loader = new PluginLoader();
        var result = loader.Discover(_tempDir);

        await Assert.That(result).HasSingleItem();
        await Assert.That(result[0].ManifestDirectory).IsEqualTo(Path.Combine(_tempDir, "my-plugin"));
    }

    [Test]
    public async Task Discover_CategoryField_DeserializesCorrectly()
    {
        WriteManifest("cat-plugin", new
        {
            Id       = "test.cat",
            Name     = "Cat Test",
            Version  = "1.0",
            Author   = "x",
            License  = "MIT",
            Category = (int)PluginCategory.Analyzer
        });

        var loader = new PluginLoader();
        var result = loader.Discover(_tempDir);

        await Assert.That(result).HasSingleItem();
        await Assert.That(result[0].Category).IsEqualTo(PluginCategory.Analyzer);
    }

    [Test]
    public async Task Discover_MissingCategoryField_DefaultsToUnknown()
    {
        WriteManifest("no-cat", new
        {
            Id = "test.nocat", Name = "No Cat", Version = "1.0", Author = "x", License = "MIT"
        });

        var loader = new PluginLoader();
        var result = loader.Discover(_tempDir);

        await Assert.That(result).HasSingleItem();
        await Assert.That(result[0].Category).IsEqualTo(PluginCategory.Unknown);
    }

    [Test]
    public async Task Discover_AssemblyAndEntryType_RoundTrip()
    {
        WriteManifest("asm-plugin", new
        {
            Id            = "test.asm",
            Name          = "Asm Test",
            Version       = "1.0",
            Author        = "x",
            License       = "MIT",
            AssemblyName  = "MyPlugin.dll",
            EntryTypeName = "MyPlugin.MyPluginEntry"
        });

        var loader = new PluginLoader();
        var result = loader.Discover(_tempDir);

        await Assert.That(result).HasSingleItem();
        await Assert.That(result[0].AssemblyName).IsEqualTo("MyPlugin.dll");
        await Assert.That(result[0].EntryTypeName).IsEqualTo("MyPlugin.MyPluginEntry");
    }
}