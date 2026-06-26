using ArtifactView.Core.Models;

namespace ArtifactView.Infrastructure.Analysis;

// Checks JPEG file structure integrity by inspecting mandatory byte-level markers.
// Phase 2: SOI + EOI presence, appended-data detection, segment header walk,
// and APP segment inventory (JFIF / EXIF / XMP / ICC / IPTC identification).
public static class JpegIntegrityAnalyzer
{
    private const byte Ff        = 0xFF;
    private const byte MarkerSoi = 0xD8;
    private const byte MarkerEoi = 0xD9;
    private const byte MarkerSos = 0xDA; // Start of Scan (entropy data follows — stop walk here)

    public static IReadOnlyList<Finding> Analyze(string path)
    {
        var results = new List<Finding>();

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            if (stream.Length < 4)
            {
                results.Add(new Finding
                {
                    Id = "jpeg-too-small",
                    Category = "Integrity",
                    ReviewPriority = ReviewPriority.Critical,
                    Observation = $"File is {stream.Length} B — too small to contain a valid JPEG.",
                    ObservationConfidence = new ConfidenceScore(95),
                    Interpretation = "File is likely empty, a placeholder, or corrupt.",
                    InterpretationConfidence = new ConfidenceScore(80)
                });
                return results;
            }

            // SOI marker (first two bytes must be FF D8)
            Span<byte> header = stackalloc byte[2];
            stream.ReadExactly(header);

            if (header[0] != Ff || header[1] != MarkerSoi)
            {
                results.Add(new Finding
                {
                    Id = "jpeg-missing-soi",
                    Category = "Integrity",
                    ReviewPriority = ReviewPriority.High,
                    Observation = $"SOI marker (FF D8) absent — first bytes are {header[0]:X2} {header[1]:X2}.",
                    ObservationConfidence = new ConfidenceScore(99),
                    Interpretation = "File extension may be incorrect, or the header has been overwritten.",
                    InterpretationConfidence = new ConfidenceScore(75)
                });
                return results;
            }

            // Segment walk: parse the header region for truncated/malformed segments.
            // Stream is at position 2 (past SOI) — the walk re-seeks internally.
            WalkSegments(stream, results);

            // EOI check: read up to 256 bytes from the end to distinguish
            // clean termination, appended data, and truncation.
            // Reading a tail rather than just the last 2 bytes lets us detect
            // data written after the real EOI — a common artefact of editing tools,
            // metadata strippers, and steganographic payloads.
            var tailSize = (int)Math.Min(stream.Length, 256);
            stream.Seek(-tailSize, SeekOrigin.End);
            Span<byte> tailBuf = stackalloc byte[256];
            var tail = tailBuf[..tailSize];
            stream.ReadExactly(tail);

            if (tail[^2] == Ff && tail[^1] == MarkerEoi)
            {
                results.Add(new Finding
                {
                    Id = "jpeg-structure-ok",
                    Category = "Integrity",
                    ReviewPriority = ReviewPriority.None,
                    Observation = "JPEG delimiters intact: SOI (FF D8) and EOI (FF D9) both present.",
                    ObservationConfidence = new ConfidenceScore(99)
                });
            }
            else
            {
                // Scan backward for any EOI to distinguish appended data from truncation.
                var eoiOffset = -1;
                for (var i = tailSize - 2; i >= 0; i--)
                {
                    if (tail[i] == Ff && tail[i + 1] == MarkerEoi)
                    {
                        eoiOffset = i;
                        break;
                    }
                }

                if (eoiOffset >= 0)
                {
                    var appendedBytes = tailSize - eoiOffset - 2;
                    results.Add(new Finding
                    {
                        Id = "jpeg-appended-data",
                        Category = "Integrity",
                        ReviewPriority = ReviewPriority.Medium,
                        Observation =
                            $"EOI marker (FF D9) found {appendedBytes} byte(s) before end of file — " +
                            $"data is appended after the image payload.",
                        ObservationConfidence = new ConfidenceScore(90),
                        Interpretation =
                            "Consistent with embedded metadata, steganographic payload, " +
                            "or a tool that writes data after the EOI marker.",
                        InterpretationConfidence = new ConfidenceScore(60)
                    });
                }
                else
                {
                    results.Add(new Finding
                    {
                        Id = "jpeg-missing-eoi",
                        Category = "Integrity",
                        ReviewPriority = ReviewPriority.Medium,
                        Observation =
                            $"EOI marker (FF D9) absent from last {tailSize} byte(s) of file.",
                        ObservationConfidence = new ConfidenceScore(95),
                        Interpretation =
                            "Consistent with file truncation or data heavily appended after EOI.",
                        InterpretationConfidence = new ConfidenceScore(65)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            results.Add(new Finding
            {
                Id = "jpeg-read-error",
                Category = "Integrity",
                ReviewPriority = ReviewPriority.High,
                Observation = $"Could not read file for integrity analysis: {ex.Message}",
                ObservationConfidence = new ConfidenceScore(90)
            });
        }

        return results;
    }

    // Walks the JPEG segment header (from SOI to the first SOS) and adds
    // findings for structural problems and APP segment inventory.
    // Stops after 64 KB — all header markers appear well before the entropy data.
    private static void WalkSegments(Stream stream, List<Finding> results)
    {
        stream.Position = 2; // past SOI
        var limit   = Math.Min(stream.Length, 65_536L);
        var sofSeen = false;
        var sosSeen = false;
        var app     = new AppStats();
        Span<byte> lenBuf  = stackalloc byte[2];
        Span<byte> peekBuf = stackalloc byte[30]; // reused for each APPn peek

        while (stream.Position < limit - 1)
        {
            var b1 = stream.ReadByte();
            if (b1 < 0) break;
            if (b1 != Ff) continue; // lost sync — resync on next 0xFF

            // Skip padding 0xFF bytes (legal before a marker)
            int b2;
            do { b2 = stream.ReadByte(); } while (b2 == Ff);
            if (b2 < 0) break;

            var m = (byte)b2;

            if (m == 0x00)  continue; // stuffed byte (escape), not a marker
            if (m == MarkerSoi || m == MarkerEoi) break;
            if (m >= 0xD0 && m <= 0xD7) continue; // RST0–RST7 (no length)
            if (m == 0x01)  continue; // TEM (no length)

            if (m == MarkerSos) { sosSeen = true; break; }

            // Track Start-of-Frame: C0–C3 cover the four common DCT/lossless types.
            if (m >= 0xC0 && m <= 0xC3) sofSeen = true;

            // All remaining markers carry a 2-byte segment length.
            if (stream.Position + 2 > stream.Length)
            {
                results.Add(new Finding
                {
                    Id = "jpeg-truncated-segment",
                    Category = "Integrity",
                    ReviewPriority = ReviewPriority.High,
                    Observation =
                        $"Segment FF {m:X2} at file offset {stream.Position - 2:N0} is missing its " +
                        $"length field — file ends before the segment is complete.",
                    ObservationConfidence = new ConfidenceScore(99),
                    Interpretation = "Consistent with file truncation or an incomplete write.",
                    InterpretationConfidence = new ConfidenceScore(85)
                });
                break;
            }

            stream.ReadExactly(lenBuf);
            var segLen  = (lenBuf[0] << 8) | lenBuf[1];
            var dataLen = segLen - 2;

            if (segLen < 2)
            {
                results.Add(new Finding
                {
                    Id = "jpeg-malformed-segment-length",
                    Category = "Integrity",
                    ReviewPriority = ReviewPriority.High,
                    Observation =
                        $"Segment FF {m:X2} at file offset {stream.Position - 4:N0} has length " +
                        $"{segLen}, which is below the 2-byte minimum.",
                    ObservationConfidence = new ConfidenceScore(95),
                    Interpretation =
                        "Consistent with a corrupt segment header or data overwritten with incorrect values.",
                    InterpretationConfidence = new ConfidenceScore(80)
                });
                break;
            }

            if (stream.Position + dataLen > stream.Length)
            {
                results.Add(new Finding
                {
                    Id = "jpeg-truncated-segment",
                    Category = "Integrity",
                    ReviewPriority = ReviewPriority.High,
                    Observation =
                        $"Segment FF {m:X2} at file offset {stream.Position - 4:N0} claims {segLen} bytes " +
                        $"but only {stream.Length - stream.Position + 2:N0} bytes remain.",
                    ObservationConfidence = new ConfidenceScore(99),
                    Interpretation = "Consistent with file truncation or an incomplete write.",
                    InterpretationConfidence = new ConfidenceScore(85)
                });
                break;
            }

            // For APPn segments (FF E0–FF EF): peek at the first 30 bytes to
            // identify the content type (JFIF, EXIF, XMP, ICC, IPTC…).
            if (m is >= 0xE0 and <= 0xEF && dataLen > 0)
            {
                var peekLen = Math.Min(dataLen, 30);
                var peeked  = peekBuf[..peekLen];
                stream.ReadExactly(peeked);
                app.Tally(m, peeked);
                stream.Seek(dataLen - peekLen, SeekOrigin.Current);
            }
            else
            {
                stream.Seek(dataLen, SeekOrigin.Current);
            }
        }

        // SOS present but no SOF found — image cannot be decoded by standard parsers.
        if (sosSeen && !sofSeen)
        {
            results.Add(new Finding
            {
                Id = "jpeg-no-sof-before-sos",
                Category = "Integrity",
                ReviewPriority = ReviewPriority.Medium,
                Observation =
                    "Start-of-Scan (SOS FF DA) found but no Start-of-Frame (SOF FF C0–C3) precedes it.",
                ObservationConfidence = new ConfidenceScore(95),
                Interpretation =
                    "File structure is non-standard. The image may not be decodable by strict parsers.",
                InterpretationConfidence = new ConfidenceScore(70)
            });
        }

        // ── APP segment inventory findings ────────────────────────────────────

        // Two genuine EXIF blocks in one file is non-standard and typically
        // indicates metadata was rewritten by an editing tool.
        if (app.ExifCount >= 2)
        {
            results.Add(new Finding
            {
                Id = "jpeg-duplicate-exif",
                Category = "Metadata",
                ReviewPriority = ReviewPriority.Medium,
                Observation =
                    $"EXIF APP1 segment (\"Exif\\0\\0\" header) appears {app.ExifCount} times.",
                ObservationConfidence = new ConfidenceScore(99),
                Interpretation =
                    "Consistent with EXIF metadata being rewritten by an editing tool, " +
                    "or a non-conformant encoder writing multiple EXIF blocks.",
                InterpretationConfidence = new ConfidenceScore(70)
            });
        }

        // Build a human-readable inventory of identified metadata layers.
        var found = new List<string>(5);
        if (app.JfifCount > 0) found.Add("JFIF");
        if (app.ExifCount > 0) found.Add(app.ExifCount > 1 ? $"EXIF \u00d7{app.ExifCount}" : "EXIF");
        if (app.XmpCount  > 0) found.Add(app.XmpCount  > 1 ? $"XMP \u00d7{app.XmpCount}"  : "XMP");
        if (app.IccCount  > 0) found.Add("ICC profile");
        if (app.IptcCount > 0) found.Add("IPTC/Photoshop");

        if (found.Count > 0)
        {
            results.Add(new Finding
            {
                Id = "jpeg-metadata-layers",
                Category = "Metadata",
                ReviewPriority = ReviewPriority.None,
                Observation = $"Metadata layers identified: {string.Join(", ", found)}.",
                ObservationConfidence = new ConfidenceScore(90)
            });
        }
        else
        {
            // No JFIF, EXIF, or XMP — unusual for any camera or processed photo.
            results.Add(new Finding
            {
                Id = "jpeg-no-app-metadata",
                Category = "Metadata",
                ReviewPriority = ReviewPriority.Low,
                Observation = "No JFIF, EXIF, or XMP segments found in the header.",
                ObservationConfidence = new ConfidenceScore(90),
                Interpretation =
                    "Consistent with metadata having been stripped, or a non-standard encoder.",
                InterpretationConfidence = new ConfidenceScore(65)
            });
        }
    }

    // Tracks which APPn content types were identified during the segment walk.
    private struct AppStats
    {
        public int JfifCount;
        public int ExifCount;
        public int XmpCount;
        public int IccCount;
        public int IptcCount;

        public void Tally(byte marker, ReadOnlySpan<byte> peek)
        {
            if (marker == 0xE0 && peek.StartsWith("JFIF\0"u8))         { JfifCount++; return; }
            if (marker == 0xE1 && peek.StartsWith("Exif\0\0"u8))       { ExifCount++; return; }
            if (marker == 0xE1 && peek.StartsWith("http://"u8))         { XmpCount++;  return; }
            if (marker == 0xE2 && peek.StartsWith("ICC_PROFILE\0"u8))   { IccCount++;  return; }
            if (marker == 0xED && peek.StartsWith("Photoshop 3.0\0"u8)) { IptcCount++; return; }
        }
    }
}
