using System.Text.Json;
using ArtifactView.Application.Plugins;
using ArtifactView.Application.Settings;
using ArtifactView.Infrastructure.Plugins;
using Xunit;

namespace ArtifactView.Application.Tests.Plugins;

public sealed class PluginRegistryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public PluginRegistryTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void WriteManifest(string subDir, object manifest)
    {
        var dir = Path.Combine(_tempDir, subDir);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), JsonSerializer.Serialize(manifest));
    }

    private PluginRegistry MakeRegistry() => new(new PluginLoader());

    // ── CoreOnly ─────────────────────────────────────────────────────────────

    [Fact]
    public void CoreOnly_BlocksAllPlugins()
    {
        WriteManifest("p1", new { Id = "a", Name = "A", Version = "1.0", Author = "x", License = "MIT", IsOpenSource = true });
        WriteManifest("p2", new { Id = "b", Name = "B", Version = "1.0", Author = "x", License = "MIT", SignatureInfo = "signed" });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.CoreOnly);

        Assert.Empty(reg.Permitted);
        Assert.False(reg.IsRegistered("a"));
        Assert.False(reg.IsRegistered("b"));
    }

    // ── CoreAndOpenSource ─────────────────────────────────────────────────────

    [Fact]
    public void CoreAndOpenSource_AllowsOpenSourceOnly()
    {
        WriteManifest("oss",    new { Id = "oss",    Name = "OSS",    Version = "1.0", Author = "x", License = "MIT",        IsOpenSource = true  });
        WriteManifest("closed", new { Id = "closed", Name = "Closed", Version = "1.0", Author = "x", License = "Proprietary", IsOpenSource = false });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.CoreAndOpenSource);

        Assert.Single(reg.Permitted);
        Assert.True(reg.IsRegistered("oss"));
        Assert.False(reg.IsRegistered("closed"));
    }

    // ── CoreAndSigned ─────────────────────────────────────────────────────────

    [Fact]
    public void CoreAndSigned_AllowsSignedOnly()
    {
        WriteManifest("signed",   new { Id = "signed",   Name = "Signed",   Version = "1.0", Author = "x", License = "MIT", SignatureInfo = "abc123" });
        WriteManifest("unsigned", new { Id = "unsigned", Name = "Unsigned", Version = "1.0", Author = "x", License = "MIT" });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.CoreAndSigned);

        Assert.Single(reg.Permitted);
        Assert.True(reg.IsRegistered("signed"));
        Assert.False(reg.IsRegistered("unsigned"));
    }

    // ── Full ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Full_AllowsAllPlugins()
    {
        WriteManifest("p1", new { Id = "a", Name = "A", Version = "1.0", Author = "x", License = "MIT" });
        WriteManifest("p2", new { Id = "b", Name = "B", Version = "1.0", Author = "x", License = "Proprietary" });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.Full);

        Assert.Equal(2, reg.Permitted.Count);
        Assert.True(reg.IsRegistered("a"));
        Assert.True(reg.IsRegistered("b"));
    }

    // ── Reload clears state ───────────────────────────────────────────────────

    [Fact]
    public void Load_ClearsPreviousState_OnReload()
    {
        WriteManifest("p1", new { Id = "first", Name = "First", Version = "1.0", Author = "x", License = "MIT" });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.Full);
        Assert.Single(reg.Permitted);

        // Switch to CoreOnly — previous permitted set must be wiped.
        reg.Load(_tempDir, PluginPolicy.CoreOnly);
        Assert.Empty(reg.Permitted);
        Assert.False(reg.IsRegistered("first"));
    }

    // ── Empty folder ─────────────────────────────────────────────────────────

    [Fact]
    public void Load_EmptyFolder_PermitsNothing()
    {
        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.Full);

        Assert.Empty(reg.Permitted);
    }

    // ── Non-existent folder ───────────────────────────────────────────────────

    [Fact]
    public void Load_NonExistentFolder_PermitsNothing()
    {
        var reg = MakeRegistry();
        reg.Load(Path.Combine(_tempDir, "missing"), PluginPolicy.Full);

        Assert.Empty(reg.Permitted);
    }

    // ── Mixed open-source + signed under CoreAndOpenSource ───────────────────

    [Fact]
    public void CoreAndOpenSource_AllowsOssSignedPlugin()
    {
        WriteManifest("both", new
        {
            Id = "both", Name = "Both", Version = "1.0", Author = "x",
            License = "Apache-2.0", IsOpenSource = true, SignatureInfo = "sig"
        });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.CoreAndOpenSource);

        Assert.True(reg.IsRegistered("both"));
    }

    // ── CoreAndSigned rejects open-source-but-unsigned plugin ────────────────

    [Fact]
    public void CoreAndSigned_RejectsOssUnsignedPlugin()
    {
        WriteManifest("oss-unsigned", new
        {
            Id = "oss-unsigned", Name = "OSS Unsigned", Version = "1.0",
            Author = "x", License = "MIT", IsOpenSource = true
        });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.CoreAndSigned);

        Assert.Empty(reg.Permitted);
    }

    // ── TryActivate ──────────────────────────────────────────────────────────

    [Fact]
    public void TryActivate_UnknownPluginId_ReturnsNull()
    {
        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.Full);

        var result = reg.TryActivate<object>("non-existent-plugin");
        Assert.Null(result);
    }

    [Fact]
    public void TryActivate_PluginBlockedByPolicy_ReturnsNull()
    {
        WriteManifest("blocked", new
        {
            Id = "blocked", Name = "Blocked", Version = "1.0", Author = "x",
            License = "MIT", IsOpenSource = false
        });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.CoreOnly);

        var result = reg.TryActivate<object>("blocked");
        Assert.Null(result);
    }

    [Fact]
    public void TryActivate_PermittedPluginWithNoAssemblyInfo_ReturnsNull()
    {
        WriteManifest("no-asm", new
        {
            Id = "no-asm", Name = "No Asm", Version = "1.0",
            Author = "x", License = "MIT", IsOpenSource = true
        });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.Full);

        // Manifest is permitted but has no AssemblyName — activation must return null.
        var result = reg.TryActivate<object>("no-asm");
        Assert.Null(result);
    }
}
