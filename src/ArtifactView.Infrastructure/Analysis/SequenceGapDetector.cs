using System.Text.RegularExpressions;

namespace ArtifactView.Infrastructure.Analysis;

// Detects gaps in camera sequence numbers within a folder.
//
// Camera firmware assigns sequential numbers to filenames (IMG_0001, DSC_1234,
// GOPR0456, etc.).  A gap in the sequence — numbers that are absent while
// adjacent numbers are present — may indicate deleted photos.
//
// Strategy:
//  1. Strip extension; extract the last contiguous digit run from each basename
//     as the sequence number.  Everything before it is the "prefix" key.
//  2. Group files by (prefix + digit-width) to avoid conflating IMG_9999 with
//     IMG_0001 after a rollover.
//  3. Sort each group by sequence number and look for non-unit steps.
//  4. Gaps > MaxReportedMissing are capped to avoid flooding the findings list.
public static class SequenceGapDetector
{
    // Don't report a gap if only one frame is missing — could just be a failed
    // shot or intentional skip.  Gaps of 2+ are more noteworthy.
    private const int MinGapSize      = 2;
    private const int MaxReportedMissing = 99;

    // Regex: last contiguous digit run before end-of-string.
    private static readonly Regex s_trailingDigits =
        new(@"^(.*?)(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <param name="files">
    /// Sequence of (absolutePath, displayName) pairs representing the folder contents.
    /// Files without a trailing digit run in their basename are silently skipped.
    /// </param>
    public static IReadOnlyList<SequenceGap> Detect(
        IEnumerable<(string Path, string DisplayName)> files)
    {
        // Build groups keyed by (prefix, digit-width).
        // Digit-width distinguishes IMG_0001 from IMG_1 — different camera series.
        var groups = new Dictionary<(string Prefix, int Width), List<(int Seq, string Path)>>();

        foreach (var (path, name) in files)
        {
            var baseName = Path.GetFileNameWithoutExtension(name);
            var m = s_trailingDigits.Match(baseName);
            if (!m.Success) continue;

            var prefix = m.Groups[1].Value;
            var seqStr = m.Groups[2].Value;
            if (!int.TryParse(seqStr, out var seq)) continue;

            var key = (prefix, seqStr.Length);
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = [];
            list.Add((seq, path));
        }

        var gaps = new List<SequenceGap>();

        foreach (var kv in groups)
        {
            var sorted = kv.Value
                .OrderBy(e => e.Seq)
                .ToList();

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var cur  = sorted[i];
                var next = sorted[i + 1];
                var missing = next.Seq - cur.Seq - 1;

                if (missing >= MinGapSize)
                {
                    gaps.Add(new SequenceGap(
                        Prefix:       kv.Key.Prefix,
                        LastBefore:   cur.Seq,
                        FirstAfter:   next.Seq,
                        MissingCount: Math.Min(missing, MaxReportedMissing),
                        PathBefore:   cur.Path,
                        PathAfter:    next.Path));
                }
            }
        }

        // Sort by path-before so results are stable across identical inputs.
        return [.. gaps.OrderBy(g => g.PathBefore, StringComparer.OrdinalIgnoreCase)];
    }
}

/// <summary>A gap in the numeric sequence between two adjacent files in a folder.</summary>
/// <param name="Prefix">Filename prefix before the sequence number (e.g. "IMG_").</param>
/// <param name="LastBefore">Last sequence number present before the gap.</param>
/// <param name="FirstAfter">First sequence number present after the gap.</param>
/// <param name="MissingCount">Number of frames absent (capped at 99).</param>
/// <param name="PathBefore">Absolute path of the file immediately before the gap.</param>
/// <param name="PathAfter">Absolute path of the file immediately after the gap.</param>
public sealed record SequenceGap(
    string Prefix,
    int    LastBefore,
    int    FirstAfter,
    int    MissingCount,
    string PathBefore,
    string PathAfter);
