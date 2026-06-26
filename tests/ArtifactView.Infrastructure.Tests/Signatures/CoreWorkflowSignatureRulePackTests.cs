using ArtifactView.Contracts.Signatures;
using ArtifactView.Infrastructure.Signatures;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Signatures;

public sealed class CoreWorkflowSignatureRulePackTests
{
    private readonly CoreWorkflowSignatureRulePack _pack = new();

    private sealed class Ctx : ISignatureContext
    {
        public string? SoftwareTag    { get; init; }
        public string? CameraModel    { get; init; }
        public int?    ImageWidth     { get; init; }
        public int?    ImageHeight    { get; init; }
        public string? DetectedMimeType { get; init; }
        public string? FileName       { get; init; }
        public bool    HasGpsData     { get; init; }
        public IReadOnlyList<string> Capabilities { get; init; } = [];
    }

    [Test]
    public async Task Returns_empty_for_no_signals()
    {
        var results = _pack.Match(new Ctx());
        await Assert.That(results).IsEmpty();
    }

    [Test]
    public async Task Detects_google_pixel_from_camera_model()
    {
        var results = _pack.Match(new Ctx { CameraModel = "Google Pixel 7" });
        await Assert.That(results).Contains(r => r.ProfileId == "platform.google-pixel");
    }

    [Test]
    public async Task Detects_google_pixel_strong_when_model_and_gps()
    {
        var results = _pack.Match(new Ctx { CameraModel = "Google Pixel 8", HasGpsData = true });
        var match = await Assert.That(results).HasSingleItem();
        await Assert.That(match.Strength).IsEqualTo(MatchStrength.Strong);
    }

    [Test]
    public async Task Detects_iphone_from_camera_model()
    {
        var results = _pack.Match(new Ctx { CameraModel = "Apple iPhone 15 Pro" });
        await Assert.That(results).Contains(r => r.ProfileId == "platform.apple-iphone");
    }

    [Test]
    public async Task Detects_samsung_from_model_prefix()
    {
        var results = _pack.Match(new Ctx { CameraModel = "SM-S918B" });
        await Assert.That(results).Contains(r => r.ProfileId == "platform.samsung-galaxy");
    }

    [Test]
    public async Task Detects_instagram_from_software_tag()
    {
        var results = _pack.Match(new Ctx { SoftwareTag = "Instagram 300.0.0" });
        await Assert.That(results).Contains(r => r.ProfileId == "workflow.instagram");
        foreach (var r in results.Where(r => r.ProfileId == "workflow.instagram"))
            await Assert.That(r.Strength).IsEqualTo(MatchStrength.Strong);
    }

    [Test]
    public async Task Detects_whatsapp_from_software_tag()
    {
        var results = _pack.Match(new Ctx { SoftwareTag = "WhatsApp" });
        await Assert.That(results).Contains(r => r.ProfileId == "workflow.whatsapp");
    }

    [Test]
    public async Task Detects_photoshop_editing_workflow()
    {
        var results = _pack.Match(new Ctx { SoftwareTag = "Adobe Photoshop CC 2024" });
        await Assert.That(results).Contains(r => r.ProfileId == "workflow.photoshop");
        await Assert.That(results.First(r => r.ProfileId == "workflow.photoshop").Strength).IsEqualTo(MatchStrength.Strong);
    }

    [Test]
    public async Task Detects_lightroom_editing_workflow()
    {
        var results = _pack.Match(new Ctx { SoftwareTag = "Adobe Lightroom 7.0" });
        await Assert.That(results).Contains(r => r.ProfileId == "workflow.lightroom");
    }

    [Test]
    public async Task Detects_snapseed_editing_workflow()
    {
        var results = _pack.Match(new Ctx { SoftwareTag = "Snapseed" });
        await Assert.That(results).Contains(r => r.ProfileId == "workflow.snapseed");
    }

    [Test]
    public async Task Detects_screenshot_explicit_software_tag()
    {
        var results = _pack.Match(new Ctx { SoftwareTag = "Screenshot" });
        var match = await Assert.That(results).HasSingleItem();
        await Assert.That(match.Strength).IsEqualTo(MatchStrength.Strong);
    }

    [Test]
    public async Task Detects_screenshot_weak_from_iphone_dimensions_no_camera()
    {
        var results = _pack.Match(new Ctx
        {
            ImageWidth  = 1170,
            ImageHeight = 2532,
            HasGpsData  = false
        });
        var match = await Assert.That(results).HasSingleItem();
        await Assert.That(match.Strength).IsEqualTo(MatchStrength.Weak);
    }

    [Test]
    public async Task Does_not_flag_screenshot_when_camera_model_present()
    {
        var results = _pack.Match(new Ctx
        {
            CameraModel = "Apple iPhone 15 Pro",
            ImageWidth  = 1170,
            ImageHeight = 2532
        });
        await Assert.That(results).DoesNotContain(r => r.ProfileId == "workflow.screenshot");
    }

    [Test]
    public async Task Does_not_flag_screenshot_when_gps_present()
    {
        var results = _pack.Match(new Ctx
        {
            ImageWidth  = 1170,
            ImageHeight = 2532,
            HasGpsData  = true
        });
        await Assert.That(results).DoesNotContain(r => r.ProfileId == "workflow.screenshot");
    }

    [Test]
    public async Task Pack_has_expected_id_and_version()
    {
        await Assert.That(_pack.Id).IsEqualTo("core.sig.workflow-core");
        await Assert.That(string.IsNullOrEmpty(_pack.Version)).IsFalse();
    }

    [Test]
    public async Task Social_match_takes_priority_over_editing_for_mixed_tag()
    {
        // Instagram tag should match social, not editing workflow.
        var results = _pack.Match(new Ctx { SoftwareTag = "Instagram" });
        await Assert.That(results).Contains(r => r.ProfileId == "workflow.instagram");
        await Assert.That(results).DoesNotContain(r => r.ProfileId.StartsWith("workflow.photoshop"));
    }
}