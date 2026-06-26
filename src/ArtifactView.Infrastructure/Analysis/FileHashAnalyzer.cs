using System.Security.Cryptography;
using ArtifactView.Core.Models;

namespace ArtifactView.Infrastructure.Analysis;

// Computes a SHA-256 hash of the file payload for provenance verification.
// The result is an informational finding — it gives the analyst a fingerprint
// they can record, compare against a known-good reference, or cite in a report.
// Called off the UI thread via Task.Run in FindingsViewModel.
public static class FileHashAnalyzer
{
    public static Finding Analyze(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hashBytes = SHA256.HashData(stream);
            var hex       = Convert.ToHexString(hashBytes);

            // Group into 8-char blocks for readability: "3A7F1B4D 2E8C9F0A ..."
            var grouped = string.Join(" ", Enumerable.Range(0, 8).Select(i => hex.Substring(i * 8, 8)));

            return new Finding
            {
                Id = "file-hash-sha256",
                Category = "Provenance",
                ReviewPriority = ReviewPriority.None,
                Observation = $"SHA-256:  {grouped}",
                ObservationConfidence = new ConfidenceScore(99),
                // Full hex stored in SupportingFactors for programmatic access / copy-paste.
                SupportingFactors = [hex]
            };
        }
        catch (Exception ex)
        {
            return new Finding
            {
                Id = "file-hash-error",
                Category = "Provenance",
                ReviewPriority = ReviewPriority.Low,
                Observation = $"Could not compute SHA-256: {ex.Message}",
                ObservationConfidence = new ConfidenceScore(90)
            };
        }
    }
}
