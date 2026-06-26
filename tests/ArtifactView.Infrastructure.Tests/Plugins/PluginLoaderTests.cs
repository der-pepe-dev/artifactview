using System.Text.Json;
using ArtifactView.Infrastructure.Plugins;
using ArtifactView.Plugins.Abstractions;
using Xunit;

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

    [Fact]
    public void Discover_NonExistentFolder_ReturnsEmpty()
    {
        var loader = new PluginLoader();
        var result = loader.Discover(Path.Combine(_tempDir, "does-not-exist"));
        Assert.Empty(result);
    }

    [Fact]
    public void Discover_EmptyFolder_ReturnsEmpty()
    {
        var loader = new PluginLoader();
        var result = loader.Discover(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public void Discover_ValidManifest_ReturnsManifest()
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

        Assert.Single(result);
        Assert.Equal("test.plugin.a", result[0].Id);
        Assert.Equal("Test Plugin A",  result[0].Name);
    }

    [Fact]
    public void Discover_MalformedManifest_SkipsBadAndReturnsGood()
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

        Assert.Single(result);
        Assert.Equal("test.plugin.good", result[0].Id);
    }

    [Fact]
    public void Discover_NullDeserializationResult_SkipsManifest()
    {
        // Serializing 'null' produces the literal "null" which deserializes to null.
        var dir = Path.Combine(_tempDir, "null-plugin");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), "null");

        var loader = new PluginLoader();
        var result = loader.Discover(_tempDir);

        Assert.Empty(result);
    }

    [Fact]
    public void Discover_MultipleValidManifests_ReturnsAll()
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

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Discover_SetsManifestDirectory_ToContainingFolder()
    {
        WriteManifest("my-plugin", new
        {
            Id = "test.dir", Name = "Dir Test", Version = "1.0", Author = "x", License = "MIT"
        });

        var loader = new PluginLoader();
        var result = loader.Discover(_tempDir);

        Assert.Single(result);
        Assert.Equal(Path.Combine(_tempDir, "my-plugin"), result[0].ManifestDirectory);
    }

    [Fact]
    public void Discover_CategoryField_DeserializesCorrectly()
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

        Assert.Single(result);
        Assert.Equal(PluginCategory.Analyzer, result[0].Category);
    }

    [Fact]
    public void Discover_MissingCategoryField_DefaultsToUnknown()
    {
        WriteManifest("no-cat", new
        {
            Id = "test.nocat", Name = "No Cat", Version = "1.0", Author = "x", License = "MIT"
        });

        var loader = new PluginLoader();
        var result = loader.Discover(_tempDir);

        Assert.Single(result);
        Assert.Equal(PluginCategory.Unknown, result[0].Category);
    }

    [Fact]
    public void Discover_AssemblyAndEntryType_RoundTrip()
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

        Assert.Single(result);
        Assert.Equal("MyPlugin.dll",        result[0].AssemblyName);
        Assert.Equal("MyPlugin.MyPluginEntry", result[0].EntryTypeName);
    }
}
