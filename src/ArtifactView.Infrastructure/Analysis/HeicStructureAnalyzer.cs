using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Formats.Heic;

namespace ArtifactView.Infrastructure.Analysis;

// Reports embedded-artifact findings derived from parsing the HEIC item structure.
// Requires only box-level parsing — does not decode pixels.
public static class HeicStructureAnalyzer
{
    public static IReadOnlyList<Finding> Analyze(Stream stream)
    {
        var result = HeicParser.Parse(stream);
        if (!result.IsValid) return [];

        var findings = new List<Finding>();

        var auxRefs   = result.AuxiliaryRefs.ToList();
        var thumbRefs = result.ThumbnailRefs.ToList();

        if (auxRefs.Count > 0)
        {
            var count = auxRefs.Count;
            findings.Add(new Finding
            {
                Id       = "heic-auxiliary-images",
                Category = "embedded-artifacts",
                Observation = count == 1
                    ? "1 auxiliary image item found in HEIC container"
                    : $"{count} auxiliary image items found in HEIC container",
                Interpretation = count == 1
                    ? "May be a depth map, disparity map, or gain map — specific type requires ipco parsing"
                    : "May include depth map, disparity map, and/or gain map",
                ObservationConfidence    = new ConfidenceScore(90),
                InterpretationConfidence = new ConfidenceScore(35),
                ReviewPriority           = ReviewPriority.None,
                Provenance               = "core.analyzer.heic-structure"
            });
        }

        if (thumbRefs.Count > 0)
        {
            findings.Add(new Finding
            {
                Id       = "heic-thumbnail-item",
                Category = "embedded-artifacts",
                Observation    = "Embedded thumbnail item found in HEIC container",
                Interpretation = "Separate from EXIF IFD1 thumbnail; encoded as a full HEVC image item",
                ObservationConfidence    = new ConfidenceScore(90),
                InterpretationConfidence = new ConfidenceScore(85),
                ReviewPriority           = ReviewPriority.None,
                Provenance               = "core.analyzer.heic-structure"
            });
        }

        // Report item-type inventory as a single informational finding.
        var typeCounts = result.Items
            .GroupBy(i => i.ItemType.TrimEnd('\0'))
            .Select(g => $"{g.Count()}×{g.Key}")
            .ToList();

        if (typeCounts.Count > 0)
        {
            findings.Add(new Finding
            {
                Id             = "heic-item-inventory",
                Category       = "structure",
                Observation    = $"HEIC item types: {string.Join(", ", typeCounts)}",
                Interpretation = null,
                ObservationConfidence    = new ConfidenceScore(90),
                InterpretationConfidence = ConfidenceScore.Unknown,
                ReviewPriority           = ReviewPriority.None,
                Provenance               = "core.analyzer.heic-structure"
            });
        }

        return findings;
    }
}
