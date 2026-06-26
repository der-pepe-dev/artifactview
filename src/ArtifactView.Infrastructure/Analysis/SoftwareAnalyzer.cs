using ArtifactView.Core.Models;

namespace ArtifactView.Infrastructure.Analysis;

// Inspects the EXIF Software field for known editing and post-processing tools.
// The software field is set by the writing application, not the camera hardware,
// so its presence or content can indicate the image has been processed or re-exported.
public static class SoftwareAnalyzer
{
    // Sub-strings matched case-insensitively against the software tag value.
    private static readonly string[] s_editingKeywords =
    [
        "photoshop", "lightroom", "gimp", "snapseed", "facetune",
        "instagram", "whatsapp", "picsart", "vsco", "afterlight",
        "fotor", "pixlr", "canva", "lens studio", "retouch", "meitu",
        "beauty", "adobe camera raw", "capture one", "on1", "darktable",
        "rawtherapee", "paintshop", "affinity photo"
    ];

    public static IReadOnlyList<Finding> Analyze(string? softwareTag)
    {
        if (string.IsNullOrWhiteSpace(softwareTag))
            return [];

        var lower = softwareTag.ToLowerInvariant();

        if (s_editingKeywords.Any(kw => lower.Contains(kw)))
        {
            return
            [
                new Finding
                {
                    Id = "software-editing-tool",
                    Category = "Metadata",
                    ReviewPriority = ReviewPriority.Medium,
                    Observation = $"Software field identifies an editing or processing tool: \"{softwareTag}\".",
                    ObservationConfidence = new ConfidenceScore(95),
                    Interpretation =
                        "Consistent with post-processing, re-export, or social-media compression. " +
                        "The original camera software field may have been overwritten.",
                    InterpretationConfidence = new ConfidenceScore(75)
                }
            ];
        }

        // Software field present but not a known editing tool — report as info.
        return
        [
            new Finding
            {
                Id = "software-field-present",
                Category = "Metadata",
                ReviewPriority = ReviewPriority.None,
                Observation = $"Software field: \"{softwareTag}\".",
                ObservationConfidence = new ConfidenceScore(99)
            }
        ];
    }
}
