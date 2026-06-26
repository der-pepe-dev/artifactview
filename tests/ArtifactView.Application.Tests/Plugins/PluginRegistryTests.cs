using System.IO;
using System.Text.Json;
using ArtifactView.Application.Plugins;
using ArtifactView.Application.Settings;
using ArtifactView.Infrastructure.Plugins;
using System.Threading.Tasks;

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

    [Test]
    public async Task CoreOnly_BlocksAllPlugins()
    {
        WriteManifest("p1", new { Id = "a", Name = "A", Version = "1.0", Author = "x", License = "MIT", IsOpenSource = true });
        WriteManifest("p2", new { Id = "b", Name = "B", Version = "1.0", Author = "x", License = "MIT", SignatureInfo = "signed" });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.CoreOnly);

        await Assert.That(reg.Permitted).IsEmpty();
        await Assert.That(reg.IsRegistered("a")).IsFalse();
        await Assert.That(reg.IsRegistered("b")).IsFalse();
    }

    // ── CoreAndOpenSource ─────────────────────────────────────────────────────

    [Test]
    public async Task CoreAndOpenSource_AllowsOpenSourceOnly()
    {
        WriteManifest("oss",    new { Id = "oss",    Name = "OSS",    Version = "1.0", Author = "x", License = "MIT",        IsOpenSource = true  });
        WriteManifest("closed", new { Id = "closed", Name = "Closed", Version = "1.0", Author = "x", License = "Proprietary", IsOpenSource = false });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.CoreAndOpenSource);

        await Assert.That(reg.Permitted).HasSingleItem();
        await Assert.That(reg.IsRegistered("oss")).IsTrue();
        await Assert.That(reg.IsRegistered("closed")).IsFalse();
    }

    // ── CoreAndSigned ─────────────────────────────────────────────────────────

    [Test]
    public async Task CoreAndSigned_AllowsSignedOnly()
    {
        WriteManifest("signed",   new { Id = "signed",   Name = "Signed",   Version = "1.0", Author = "x", License = "MIT", SignatureInfo = "abc123" });
        WriteManifest("unsigned", new { Id = "unsigned", Name = "Unsigned", Version = "1.0", Author = "x", License = "MIT" });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.CoreAndSigned);

        await Assert.That(reg.Permitted).HasSingleItem();
        await Assert.That(reg.IsRegistered("signed")).IsTrue();
        await Assert.That(reg.IsRegistered("unsigned")).IsFalse();
    }

    // ── Full ──────────────────────────────────────────────────────────────────

    [Test]
    public async Task Full_AllowsAllPlugins()
    {
        WriteManifest("p1", new { Id = "a", Name = "A", Version = "1.0", Author = "x", License = "MIT" });
        WriteManifest("p2", new { Id = "b", Name = "B", Version = "1.0", Author = "x", License = "Proprietary" });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.Full);

        await Assert.That(reg.Permitted.Count).IsEqualTo(2);
        await Assert.That(reg.IsRegistered("a")).IsTrue();
        await Assert.That(reg.IsRegistered("b")).IsTrue();
    }

    // ── Reload clears state ───────────────────────────────────────────────────

    [Test]
    public async Task Load_ClearsPreviousState_OnReload()
    {
        WriteManifest("p1", new { Id = "first", Name = "First", Version = "1.0", Author = "x", License = "MIT" });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.Full);
        await Assert.That(reg.Permitted).HasSingleItem();

        // Switch to CoreOnly — previous permitted set must be wiped.
        reg.Load(_tempDir, PluginPolicy.CoreOnly);
        await Assert.That(reg.Permitted).IsEmpty();
        await Assert.That(reg.IsRegistered("first")).IsFalse();
    }

    // ── Empty folder ─────────────────────────────────────────────────────────

    [Test]
    public async Task Load_EmptyFolder_PermitsNothing()
    {
        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.Full);

        await Assert.That(reg.Permitted).IsEmpty();
    }

    // ── Non-existent folder ───────────────────────────────────────────────────

    [Test]
    public async Task Load_NonExistentFolder_PermitsNothing()
    {
        var reg = MakeRegistry();
        reg.Load(Path.Combine(_tempDir, "missing"), PluginPolicy.Full);

        await Assert.That(reg.Permitted).IsEmpty();
    }

    // ── Mixed open-source + signed under CoreAndOpenSource ───────────────────

    [Test]
    public async Task CoreAndOpenSource_AllowsOssSignedPlugin()
    {
        WriteManifest("both", new
        {
            Id = "both", Name = "Both", Version = "1.0", Author = "x",
            License = "Apache-2.0", IsOpenSource = true, SignatureInfo = "sig"
        });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.CoreAndOpenSource);

        await Assert.That(reg.IsRegistered("both")).IsTrue();
    }

    // ── CoreAndSigned rejects open-source-but-unsigned plugin ────────────────

    [Test]
    public async Task CoreAndSigned_RejectsOssUnsignedPlugin()
    {
        WriteManifest("oss-unsigned", new
        {
            Id = "oss-unsigned", Name = "OSS Unsigned", Version = "1.0",
            Author = "x", License = "MIT", IsOpenSource = true
        });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.CoreAndSigned);

        await Assert.That(reg.Permitted).IsEmpty();
    }

    // ── TryActivate ──────────────────────────────────────────────────────────

    [Test]
    public async Task TryActivate_UnknownPluginId_ReturnsNull()
    {
        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.Full);

        var result = reg.TryActivate<object>("non-existent-plugin");
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task TryActivate_PluginBlockedByPolicy_ReturnsNull()
    {
        WriteManifest("blocked", new
        {
            Id = "blocked", Name = "Blocked", Version = "1.0", Author = "x",
            License = "MIT", IsOpenSource = false
        });

        var reg = MakeRegistry();
        reg.Load(_tempDir, PluginPolicy.CoreOnly);

        var result = reg.TryActivate<object>("blocked");
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task TryActivate_PermittedPluginWithNoAssemblyInfo_ReturnsNull()
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
        await Assert.That(result).IsNull();
    }
}