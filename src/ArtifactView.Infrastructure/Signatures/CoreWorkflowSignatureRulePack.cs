using ArtifactView.Contracts.Signatures;

namespace ArtifactView.Infrastructure.Signatures;

// Matches common camera platforms, social-media outputs, and editing workflows
// using EXIF Software tag, camera model, GPS presence, and image dimensions.
// All matches use confidence-based language: never claims certainty.
public sealed class CoreWorkflowSignatureRulePack : ISignatureRulePack
{
    public string Id          => "core.sig.workflow-core";
    public string DisplayName => "Core workflow signatures";
    public string Version     => "1.0";

    public IReadOnlyList<SignatureMatchResult> Match(ISignatureContext ctx)
    {
        var results = new List<SignatureMatchResult>();

        MatchCameraPlatform(ctx, results);
        MatchSocialMedia(ctx, results);
        MatchEditingWorkflow(ctx, results);
        MatchScreenshot(ctx, results);

        return results;
    }

    // ── Camera platform detection ─────────────────────────────────────────────

    private static void MatchCameraPlatform(ISignatureContext ctx, List<SignatureMatchResult> out_)
    {
        if (ctx.CameraModel is null && ctx.SoftwareTag is null) return;

        var model  = ctx.CameraModel?.ToLowerInvariant()  ?? "";
        var sw     = ctx.SoftwareTag?.ToLowerInvariant() ?? "";

        // Google Pixel
        if (model.Contains("pixel") || sw.Contains("pixel"))
        {
            var factors = new List<string>();
            if (model.Contains("pixel")) factors.Add($"Camera model: \"{ctx.CameraModel}\"");
            if (sw.Contains("pixel"))    factors.Add($"Software tag: \"{ctx.SoftwareTag}\"");
            if (ctx.HasGpsData)          factors.Add("GPS data present");

            out_.Add(new SignatureMatchResult
            {
                ProfileId   = "platform.google-pixel",
                ProfileName = "Google Pixel camera",
                Strength    = factors.Count >= 2 ? MatchStrength.Strong : MatchStrength.Moderate,
                SupportingFactors = factors,
                Notes = "Consistent with Google Pixel native camera app."
            });
            return;
        }

        // Apple iPhone / iPad
        if (model.Contains("iphone") || model.Contains("ipad") ||
            sw.Contains("ios") || sw.Contains("iphone"))
        {
            var factors = new List<string>();
            if (model.Contains("iphone") || model.Contains("ipad"))
                factors.Add($"Camera model: \"{ctx.CameraModel}\"");
            if (sw.Contains("ios") || sw.Contains("iphone"))
                factors.Add($"Software tag: \"{ctx.SoftwareTag}\"");
            if (ctx.HasGpsData) factors.Add("GPS data present");

            out_.Add(new SignatureMatchResult
            {
                ProfileId   = "platform.apple-iphone",
                ProfileName = "Apple iPhone / iPad camera",
                Strength    = factors.Count >= 2 ? MatchStrength.Strong : MatchStrength.Moderate,
                SupportingFactors = factors,
                Notes = "Consistent with Apple iOS camera app output."
            });
            return;
        }

        // Samsung Galaxy
        if (model.Contains("samsung") || model.Contains("sm-") || sw.Contains("samsung"))
        {
            var factors = new List<string>();
            if (model.Contains("samsung") || model.Contains("sm-"))
                factors.Add($"Camera model: \"{ctx.CameraModel}\"");
            if (sw.Contains("samsung"))
                factors.Add($"Software tag: \"{ctx.SoftwareTag}\"");

            out_.Add(new SignatureMatchResult
            {
                ProfileId   = "platform.samsung-galaxy",
                ProfileName = "Samsung Galaxy camera",
                Strength    = MatchStrength.Moderate,
                SupportingFactors = factors
            });
        }
    }

    // ── Social media / messaging re-encode detection ─────────────────────────

    private static readonly (string Key, string ProfileId, string ProfileName)[] s_socialProfiles =
    [
        ("instagram",   "workflow.instagram",   "Instagram"),
        ("whatsapp",    "workflow.whatsapp",    "WhatsApp"),
        ("facebook",    "workflow.facebook",    "Facebook"),
        ("snapchat",    "workflow.snapchat",    "Snapchat"),
        ("twitter",     "workflow.twitter",     "Twitter / X"),
        ("telegram",    "workflow.telegram",    "Telegram"),
        ("tiktok",      "workflow.tiktok",      "TikTok"),
        ("wechat",      "workflow.wechat",      "WeChat"),
        ("line\b",      "workflow.line",        "LINE"),
    ];

    private static void MatchSocialMedia(ISignatureContext ctx, List<SignatureMatchResult> out_)
    {
        var sw = ctx.SoftwareTag?.ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(sw)) return;

        foreach (var (key, profileId, profileName) in s_socialProfiles)
        {
            if (!sw.Contains(key)) continue;

            out_.Add(new SignatureMatchResult
            {
                ProfileId   = profileId,
                ProfileName = profileName,
                Strength    = MatchStrength.Strong,
                SupportingFactors = [$"Software tag contains \"{key}\": \"{ctx.SoftwareTag}\""],
                Notes = $"Consistent with image re-encoded or saved by {profileName}. " +
                        "Original camera metadata may have been stripped or modified."
            });
            return; // one social match per image is enough
        }
    }

    // ── Editing workflow detection ────────────────────────────────────────────

    private static readonly (string Key, string ProfileId, string ProfileName, string Note)[] s_editingProfiles =
    [
        ("photoshop",      "workflow.photoshop",      "Adobe Photoshop",    "Consistent with manual editing or compositing in Photoshop."),
        ("lightroom",      "workflow.lightroom",      "Adobe Lightroom",    "Consistent with Lightroom develop/export workflow."),
        ("adobe camera raw","workflow.acr",           "Adobe Camera Raw",   "Consistent with ACR raw development."),
        ("capture one",    "workflow.capture-one",    "Capture One",        "Consistent with Capture One tethered or batch export."),
        ("darktable",      "workflow.darktable",      "Darktable",          "Consistent with Darktable open-source raw processing."),
        ("rawtherapee",    "workflow.rawtherapee",    "RawTherapee",        "Consistent with RawTherapee raw processing."),
        ("gimp",           "workflow.gimp",           "GIMP",               "Consistent with GIMP editing or export."),
        ("snapseed",       "workflow.snapseed",       "Snapseed",           "Consistent with Snapseed mobile editing."),
        ("facetune",       "workflow.facetune",       "Facetune",           "Consistent with Facetune retouching."),
        ("vsco",           "workflow.vsco",           "VSCO",               "Consistent with VSCO filter/export."),
        ("picsart",        "workflow.picsart",        "PicsArt",            "Consistent with PicsArt mobile editing."),
        ("affinity photo", "workflow.affinity-photo", "Affinity Photo",     "Consistent with Affinity Photo editing or export."),
        ("on1",            "workflow.on1",            "ON1 Photo RAW",      "Consistent with ON1 raw processing."),
        ("paintshop",      "workflow.paintshop",      "PaintShop Pro",      "Consistent with PaintShop Pro editing."),
        ("meitu",          "workflow.meitu",          "Meitu",              "Consistent with Meitu beauty/editing app."),
    ];

    private static void MatchEditingWorkflow(ISignatureContext ctx, List<SignatureMatchResult> out_)
    {
        var sw = ctx.SoftwareTag?.ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(sw)) return;

        foreach (var (key, profileId, profileName, note) in s_editingProfiles)
        {
            if (!sw.Contains(key)) continue;

            out_.Add(new SignatureMatchResult
            {
                ProfileId   = profileId,
                ProfileName = profileName,
                Strength    = MatchStrength.Strong,
                SupportingFactors = [$"Software tag: \"{ctx.SoftwareTag}\""],
                Notes = note
            });
            return;
        }
    }

    // ── Screenshot heuristic ─────────────────────────────────────────────────
    // Screenshots have no GPS, no camera model, and often dimensions that match
    // common device screen sizes. This is weak — software tag absence means
    // many legitimate photos would also match. Only fire if ALL conditions hold.

    private static readonly (int W, int H, string Device)[] s_screenSizes =
    [
        (1170, 2532, "iPhone 12/13/14"),
        (1179, 2556, "iPhone 14/15 Pro"),
        (1290, 2796, "iPhone 14/15 Pro Max"),
        (1080, 2400, "Android FHD+ common"),
        (1440, 3200, "Android QHD+ common"),
        (2532, 1170, "iPhone 12/13/14 landscape"),
        (2556, 1179, "iPhone 14/15 Pro landscape"),
    ];

    private static void MatchScreenshot(ISignatureContext ctx, List<SignatureMatchResult> out_)
    {
        // Must have no camera model and no GPS — otherwise likely a real photo.
        if (ctx.CameraModel is not null || ctx.HasGpsData) return;

        var sw = ctx.SoftwareTag?.ToLowerInvariant() ?? "";
        if (sw.Contains("screenshot")) // explicit
        {
            out_.Add(new SignatureMatchResult
            {
                ProfileId   = "workflow.screenshot",
                ProfileName = "Screenshot",
                Strength    = MatchStrength.Strong,
                SupportingFactors = [$"Software tag indicates screenshot: \"{ctx.SoftwareTag}\""],
                Notes = "Consistent with device screenshot."
            });
            return;
        }

        if (ctx.ImageWidth is null || ctx.ImageHeight is null) return;

        foreach (var (w, h, device) in s_screenSizes)
        {
            if (ctx.ImageWidth != w || ctx.ImageHeight != h) continue;

            out_.Add(new SignatureMatchResult
            {
                ProfileId   = "workflow.screenshot",
                ProfileName = "Screenshot (possible)",
                Strength    = MatchStrength.Weak,
                SupportingFactors =
                [
                    $"Dimensions {w}×{h} match {device} screen",
                    "No camera model in metadata",
                    "No GPS data"
                ],
                Notes = "Possible screenshot. Dimensions match a known device screen size, " +
                        "but this is weak evidence — many photos share these dimensions."
            });
            return;
        }
    }
}
