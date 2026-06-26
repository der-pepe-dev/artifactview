using ArtifactView.Core.Models;

namespace ArtifactView.Infrastructure.Analysis;

// Compares the embedded EXIF thumbnail against the main image using perceptual
// hashing (dHash).  A significant visual difference is consistent with the main
// image being replaced or heavily edited after the thumbnail was embedded.
//
// All findings use confidence-based language.  Camera firmware, export tools,
// and editing software can all produce mismatches without any tampering.
public static class RepresentativeFrameAnalyzer
{
    public static Finding? Analyze(string mainImagePath, byte[] thumbnailBytes)
    {
        var mainHash  = DHashComputer.ComputeFromFile(mainImagePath);
        var thumbHash = DHashComputer.ComputeFromBytes(thumbnailBytes);

        if (mainHash is null || thumbHash is null)
            return null;

        var distance = PerceptualHash.HammingDistance(mainHash.Value, thumbHash.Value);
        return distance switch
        {
            <= 5  => ConsistentFinding(distance),
            <= 10 => MinorDifferenceFinding(distance),
            <= 20 => SignificantDifferenceFinding(distance),
            _     => ContentDiffersFinding(distance)
        };
    }

    private static Finding ConsistentFinding(int distance) => new()
    {
        Id       = "thumb-content-consistent",
        Category = "Embedded Thumbnail",
        ReviewPriority           = ReviewPriority.None,
        Observation              = $"Embedded thumbnail content is perceptually consistent with the main image (dHash Δ{distance}).",
        ObservationConfidence    = new ConfidenceScore(88),
        Interpretation           = "No visual evidence of thumbnail–main content mismatch.",
        InterpretationConfidence = new ConfidenceScore(80)
    };

    private static Finding MinorDifferenceFinding(int distance) => new()
    {
        Id       = "thumb-content-minor-diff",
        Category = "Embedded Thumbnail",
        ReviewPriority           = ReviewPriority.Low,
        Observation              = $"Embedded thumbnail shows minor perceptual differences from the main image (dHash Δ{distance}).",
        ObservationConfidence    = new ConfidenceScore(80),
        Interpretation           = "Consistent with minor edits, brightness/contrast adjustments, " +
                                   "or different JPEG compression applied to thumbnail vs. main image.",
        InterpretationConfidence = new ConfidenceScore(60)
    };

    private static Finding SignificantDifferenceFinding(int distance) => new()
    {
        Id       = "thumb-content-significant-diff",
        Category = "Embedded Thumbnail",
        ReviewPriority           = ReviewPriority.Medium,
        Observation              = $"Embedded thumbnail content differs perceptually from the main image (dHash Δ{distance}).",
        ObservationConfidence    = new ConfidenceScore(82),
        Interpretation           = "Consistent with the main image being substantially edited or cropped " +
                                   "after the thumbnail was embedded, or the thumbnail sourced from a " +
                                   "different frame. Could also result from heavy JPEG re-compression.",
        InterpretationConfidence = new ConfidenceScore(55),
        SupportingFactors        = [$"Perceptual hash distance: {distance} (threshold: >10 = significant, >20 = likely different content)"]
    };

    private static Finding ContentDiffersFinding(int distance) => new()
    {
        Id       = "thumb-content-differs",
        Category = "Embedded Thumbnail",
        ReviewPriority           = ReviewPriority.High,
        Observation              = $"Embedded thumbnail content appears to differ substantially from the main image (dHash Δ{distance}).",
        ObservationConfidence    = new ConfidenceScore(83),
        Interpretation           = "Consistent with the main image being replaced or the thumbnail being " +
                                   "transplanted from a different file. Review the thumbnail and main image " +
                                   "side by side. Legitimate causes include collage export or aggressive " +
                                   "AI-based editing tools that do not update the EXIF thumbnail.",
        InterpretationConfidence = new ConfidenceScore(60),
        SupportingFactors        = [$"Perceptual hash distance: {distance} (threshold: >20 = likely different content)"]
    };
}
