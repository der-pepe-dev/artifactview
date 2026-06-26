using ArtifactView.Core.Models;

namespace ArtifactView.Infrastructure.Analysis;

// Compares the magic-byte-detected format against the file extension.
// A mismatch is a significant forensic signal: it can indicate
// deliberate renaming, format conversion without re-saving, or
// file corruption that replaced the header.
public static class FormatMismatchAnalyzer
{
    public static Finding? Analyze(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
            return null;

        var detected = MagicByteFormatDetector.Detect(path);
        if (detected is null)
            return null; // unrecognised format — nothing to compare

        var expected = MagicByteFormatDetector.ExpectedFormatForExtension(ext);
        if (expected is null)
            return null; // extension not in our known set — can't compare

        if (detected == expected)
        {
            return new Finding
            {
                Id = "format-match",
                Category = "Format",
                ReviewPriority = ReviewPriority.None,
                Observation = $"File content ({detected}) matches extension ({ext}).",
                ObservationConfidence = new ConfidenceScore(99)
            };
        }

        // Mismatch: the file content doesn't match the extension.
        return new Finding
        {
            Id = "format-mismatch",
            Category = "Format",
            ReviewPriority = ReviewPriority.High,
            Observation =
                $"File extension is {ext} but content signature indicates {detected}.",
            ObservationConfidence = new ConfidenceScore(95),
            Interpretation =
                "Consistent with the file being renamed with a different extension, " +
                "converted without re-saving in the target format, or having its " +
                "header modified. The file may not open correctly in software that " +
                "relies on the extension to choose a decoder.",
            InterpretationConfidence = new ConfidenceScore(80)
        };
    }
}
