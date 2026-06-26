using ArtifactView.Contracts.Signatures;
using ArtifactView.Infrastructure.Signatures;
using Xunit;

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

    [Fact]
    public void Returns_empty_for_no_signals()
    {
        var results = _pack.Match(new Ctx());
        Assert.Empty(results);
    }

    [Fact]
    public void Detects_google_pixel_from_camera_model()
    {
        var results = _pack.Match(new Ctx { CameraModel = "Google Pixel 7" });
        Assert.Contains(results, r => r.ProfileId == "platform.google-pixel");
    }

    [Fact]
    public void Detects_google_pixel_strong_when_model_and_gps()
    {
        var results = _pack.Match(new Ctx { CameraModel = "Google Pixel 8", HasGpsData = true });
        var match = Assert.Single(results, r => r.ProfileId == "platform.google-pixel");
        Assert.Equal(MatchStrength.Strong, match.Strength);
    }

    [Fact]
    public void Detects_iphone_from_camera_model()
    {
        var results = _pack.Match(new Ctx { CameraModel = "Apple iPhone 15 Pro" });
        Assert.Contains(results, r => r.ProfileId == "platform.apple-iphone");
    }

    [Fact]
    public void Detects_samsung_from_model_prefix()
    {
        var results = _pack.Match(new Ctx { CameraModel = "SM-S918B" });
        Assert.Contains(results, r => r.ProfileId == "platform.samsung-galaxy");
    }

    [Fact]
    public void Detects_instagram_from_software_tag()
    {
        var results = _pack.Match(new Ctx { SoftwareTag = "Instagram 300.0.0" });
        Assert.Contains(results, r => r.ProfileId == "workflow.instagram");
        Assert.All(results.Where(r => r.ProfileId == "workflow.instagram"),
            r => Assert.Equal(MatchStrength.Strong, r.Strength));
    }

    [Fact]
    public void Detects_whatsapp_from_software_tag()
    {
        var results = _pack.Match(new Ctx { SoftwareTag = "WhatsApp" });
        Assert.Contains(results, r => r.ProfileId == "workflow.whatsapp");
    }

    [Fact]
    public void Detects_photoshop_editing_workflow()
    {
        var results = _pack.Match(new Ctx { SoftwareTag = "Adobe Photoshop CC 2024" });
        Assert.Contains(results, r => r.ProfileId == "workflow.photoshop");
        Assert.Equal(MatchStrength.Strong, results.First(r => r.ProfileId == "workflow.photoshop").Strength);
    }

    [Fact]
    public void Detects_lightroom_editing_workflow()
    {
        var results = _pack.Match(new Ctx { SoftwareTag = "Adobe Lightroom 7.0" });
        Assert.Contains(results, r => r.ProfileId == "workflow.lightroom");
    }

    [Fact]
    public void Detects_snapseed_editing_workflow()
    {
        var results = _pack.Match(new Ctx { SoftwareTag = "Snapseed" });
        Assert.Contains(results, r => r.ProfileId == "workflow.snapseed");
    }

    [Fact]
    public void Detects_screenshot_explicit_software_tag()
    {
        var results = _pack.Match(new Ctx { SoftwareTag = "Screenshot" });
        var match = Assert.Single(results, r => r.ProfileId == "workflow.screenshot");
        Assert.Equal(MatchStrength.Strong, match.Strength);
    }

    [Fact]
    public void Detects_screenshot_weak_from_iphone_dimensions_no_camera()
    {
        var results = _pack.Match(new Ctx
        {
            ImageWidth  = 1170,
            ImageHeight = 2532,
            HasGpsData  = false
        });
        var match = Assert.Single(results, r => r.ProfileId == "workflow.screenshot");
        Assert.Equal(MatchStrength.Weak, match.Strength);
    }

    [Fact]
    public void Does_not_flag_screenshot_when_camera_model_present()
    {
        var results = _pack.Match(new Ctx
        {
            CameraModel = "Apple iPhone 15 Pro",
            ImageWidth  = 1170,
            ImageHeight = 2532
        });
        Assert.DoesNotContain(results, r => r.ProfileId == "workflow.screenshot");
    }

    [Fact]
    public void Does_not_flag_screenshot_when_gps_present()
    {
        var results = _pack.Match(new Ctx
        {
            ImageWidth  = 1170,
            ImageHeight = 2532,
            HasGpsData  = true
        });
        Assert.DoesNotContain(results, r => r.ProfileId == "workflow.screenshot");
    }

    [Fact]
    public void Pack_has_expected_id_and_version()
    {
        Assert.Equal("core.sig.workflow-core", _pack.Id);
        Assert.False(string.IsNullOrEmpty(_pack.Version));
    }

    [Fact]
    public void Social_match_takes_priority_over_editing_for_mixed_tag()
    {
        // Instagram tag should match social, not editing workflow.
        var results = _pack.Match(new Ctx { SoftwareTag = "Instagram" });
        Assert.Contains(results, r => r.ProfileId == "workflow.instagram");
        Assert.DoesNotContain(results, r => r.ProfileId.StartsWith("workflow.photoshop"));
    }
}
