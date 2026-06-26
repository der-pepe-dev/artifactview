using ArtifactView.Core.Models;

namespace ArtifactView.Infrastructure.Analysis;

// Checks PNG file structure integrity using mandatory byte-level markers.
// Phase 2 basic: signature (8 bytes) + IEND chunk (last 12 bytes).
public static class PngIntegrityAnalyzer
{
    // PNG signature: %PNG\r\n\x1a\n
    private static ReadOnlySpan<byte> Signature =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // IEND chunk: 4-byte length (0) + "IEND" + CRC 0xAE426082
    private static ReadOnlySpan<byte> IendChunk =>
        [0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

    public static IReadOnlyList<Finding> Analyze(string path)
    {
        var results = new List<Finding>();

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            if (stream.Length < 20)
            {
                results.Add(new Finding
                {
                    Id = "png-too-small",
                    Category = "Integrity",
                    ReviewPriority = ReviewPriority.Critical,
                    Observation = $"File is {stream.Length} B — too small to be a valid PNG.",
                    ObservationConfidence = new ConfidenceScore(95),
                    Interpretation = "File may be empty, a placeholder, or corrupt.",
                    InterpretationConfidence = new ConfidenceScore(80)
                });
                return results;
            }

            // Signature check (first 8 bytes)
            Span<byte> sig = stackalloc byte[8];
            stream.ReadExactly(sig);
            if (!sig.SequenceEqual(Signature))
            {
                results.Add(new Finding
                {
                    Id = "png-invalid-signature",
                    Category = "Integrity",
                    ReviewPriority = ReviewPriority.High,
                    Observation = $"PNG signature absent — first bytes are {Convert.ToHexString(sig.ToArray())}.",
                    ObservationConfidence = new ConfidenceScore(99),
                    Interpretation = "File extension may be incorrect, or the header has been overwritten.",
                    InterpretationConfidence = new ConfidenceScore(75)
                });
                return results;
            }

            // IEND check: read up to 256 bytes from the end to distinguish
            // clean termination, appended data, and truncation.
            var tailSize = (int)Math.Min(stream.Length, 256);
            stream.Seek(-tailSize, SeekOrigin.End);
            Span<byte> tailBuf = stackalloc byte[256];
            var tail = tailBuf[..tailSize];
            stream.ReadExactly(tail);

            if (tailSize >= 12 && tail[^12..].SequenceEqual(IendChunk))
            {
                results.Add(new Finding
                {
                    Id = "png-structure-ok",
                    Category = "Integrity",
                    ReviewPriority = ReviewPriority.None,
                    Observation = "PNG structure intact: valid signature and IEND chunk present.",
                    ObservationConfidence = new ConfidenceScore(99)
                });
            }
            else
            {
                // Scan backward for any IEND chunk to distinguish appended data from truncation.
                var iendOffset = -1;
                for (var i = tailSize - 12; i >= 0; i--)
                {
                    if (tail[i..].StartsWith(IendChunk))
                    {
                        iendOffset = i;
                        break;
                    }
                }

                if (iendOffset >= 0)
                {
                    var appendedBytes = tailSize - iendOffset - 12;
                    results.Add(new Finding
                    {
                        Id = "png-appended-data",
                        Category = "Integrity",
                        ReviewPriority = ReviewPriority.Medium,
                        Observation =
                            $"IEND chunk found {appendedBytes} byte(s) before end of file — " +
                            $"data is appended after the image payload.",
                        ObservationConfidence = new ConfidenceScore(90),
                        Interpretation =
                            "Consistent with embedded metadata, steganographic payload, " +
                            "or a tool that writes data after the IEND chunk.",
                        InterpretationConfidence = new ConfidenceScore(60)
                    });
                }
                else
                {
                    results.Add(new Finding
                    {
                        Id = "png-missing-iend",
                        Category = "Integrity",
                        ReviewPriority = ReviewPriority.Medium,
                        Observation =
                            $"IEND chunk absent or not found in last {tailSize} byte(s) of file.",
                        ObservationConfidence = new ConfidenceScore(99),
                        Interpretation =
                            "Consistent with file truncation or data appended after the image payload.",
                        InterpretationConfidence = new ConfidenceScore(70)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            results.Add(new Finding
            {
                Id = "png-read-error",
                Category = "Integrity",
                ReviewPriority = ReviewPriority.High,
                Observation = $"Could not read file for integrity analysis: {ex.Message}",
                ObservationConfidence = new ConfidenceScore(90)
            });
        }

        return results;
    }
}
