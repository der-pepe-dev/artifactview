using System.Text;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Metadata;

namespace ArtifactView.Infrastructure.Reports;

// Generates a plain-text findings report for a single media item.
// The report uses confidence-based language and clearly labels every
// observation and interpretation with its provenance and confidence score.
//
// No claims of certainty — all forensic language follows the project's
// "consistent with / likely / possible" guidelines.
public static class FindingsReportExporter
{
    public static string Generate(
        MediaEntityRow row,
        ExifSummary? summary,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<EvidenceContributor> contributors,
        IReadOnlyList<RawMetadataEntry>? rawMetadata = null,
        IReadOnlyList<ReconciledFieldValue>? reconciledFields = null)
    {
        var sb = new StringBuilder(4096);

        sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║               ArtifactView — Findings Report               ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
        sb.AppendLine();
        sb.AppendLine($"  Generated:  {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        // ── File Identity ────────────────────────────────────────────────
        WriteSection(sb, "File Identity");
        WriteField(sb, "Name",       row.DisplayName);
        WriteField(sb, "Path",       row.LogicalPath);
        WriteField(sb, "Presence",   row.PresenceState);
        WriteField(sb, "Size",       row.FileSizeText);

        if (summary is not null)
        {
            if (summary.Width.HasValue && summary.Height.HasValue)
                WriteField(sb, "Dimensions", $"{summary.Width}×{summary.Height}");
            if (summary.CameraModel is not null)
                WriteField(sb, "Camera",     summary.CameraModel);
            if (summary.GpsText is not null)
                WriteField(sb, "GPS",        summary.GpsText);
            if (summary.SoftwareTag is not null)
                WriteField(sb, "Software",   summary.SoftwareTag);
            if (summary.CaptureDate.HasValue)
                WriteField(sb, "DateTimeOriginal", summary.CaptureDate.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            if (summary.DateTimeDigitized.HasValue)
                WriteField(sb, "DateTimeDigitized", summary.DateTimeDigitized.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            if (summary.DateTimeModified.HasValue)
                WriteField(sb, "DateTime (IFD0)", summary.DateTimeModified.Value.ToString("yyyy-MM-dd HH:mm:ss"));

            if (summary.HasThumbnail)
            {
                var thumbSize = summary.ThumbnailByteCount is > 0
                    ? $", {summary.ThumbnailByteCount.Value:N0} bytes"
                    : string.Empty;
                WriteField(sb, "EXIF Thumbnail", $"{summary.ThumbnailWidth}×{summary.ThumbnailHeight}{thumbSize}");
            }
        }
        sb.AppendLine();

        // ── Findings ─────────────────────────────────────────────────────
        WriteSection(sb, $"Findings ({findings.Count})");
        if (findings.Count == 0)
        {
            sb.AppendLine("  No findings recorded.");
        }
        else
        {
            foreach (var f in findings)
            {
                var priority = f.ReviewPriority switch
                {
                    ReviewPriority.None     => "   ",
                    ReviewPriority.Low      => " ! ",
                    ReviewPriority.Medium   => " !! ",
                    ReviewPriority.High     => "!!!",
                    ReviewPriority.Critical => "***",
                    _                       => "   "
                };
                sb.AppendLine($"  [{priority}] {f.Category}");
                sb.AppendLine($"        Observation:  {f.Observation}");
                sb.AppendLine($"        Confidence:   {f.ObservationConfidence.Label} ({f.ObservationConfidence.Value})");

                if (f.Interpretation is not null)
                {
                    sb.AppendLine($"        Interpretation: {f.Interpretation}");
                    sb.AppendLine($"        Interp. conf.: {f.InterpretationConfidence.Label} ({f.InterpretationConfidence.Value})");
                }

                if (f.SupportingFactors.Count > 0)
                    sb.AppendLine($"        Supporting: {string.Join("; ", f.SupportingFactors)}");
                if (f.ConflictingFactors.Count > 0)
                    sb.AppendLine($"        Conflicting: {string.Join("; ", f.ConflictingFactors)}");

                sb.AppendLine();
            }
        }

        // ── Reconciled Values ─────────────────────────────────────────────
        if (reconciledFields is { Count: > 0 })
        {
            WriteSection(sb, $"Reconciled Values ({reconciledFields.Count} fields)");
            foreach (var rf in reconciledFields)
            {
                var statusIcon = rf.Status switch
                {
                    MergeStatus.Resolved   => "\u2713",
                    MergeStatus.Merged     => "\u2248",
                    MergeStatus.Ambiguous  => "?",
                    MergeStatus.Conflicted => "\u26a0",
                    _                      => " "
                };
                sb.AppendLine($"  [{statusIcon}] {rf.FieldName}: {rf.PreferredValue}  ({rf.Status}, {rf.Confidence.Label})");

                if (rf.Candidates.Count > 1)
                {
                    foreach (var c in rf.Candidates)
                        sb.AppendLine($"        {c.SourceType}: {c.RawValue}  (confidence: {c.Confidence.Label})");
                }
                sb.AppendLine();
            }
        }

        // ── Evidence Sources ─────────────────────────────────────────────
        WriteSection(sb, $"Evidence Sources ({contributors.Count})");
        foreach (var c in contributors)
        {
            sb.AppendLine($"  • {c.SourceKind}  (confidence: {c.Confidence:P0})");
            sb.AppendLine($"    {c.Description}");
            if (c.SourcePath is not null)
                sb.AppendLine($"    Path: {c.SourcePath}");
            sb.AppendLine();
        }

        // ── Raw Metadata (optional, for full reports) ────────────────────
        if (rawMetadata is { Count: > 0 })
        {
            WriteSection(sb, $"Raw Metadata ({rawMetadata.Count} tags)");
            var maxDir = Math.Min(30, rawMetadata.Max(e => e.DirectoryName?.Length ?? 0));
            var maxTag = Math.Min(35, rawMetadata.Max(e => e.TagName?.Length ?? 0));

            foreach (var entry in rawMetadata)
            {
                var dir = (entry.DirectoryName ?? "").PadRight(maxDir);
                var tag = (entry.TagName ?? "").PadRight(maxTag);
                sb.AppendLine($"  {dir}  {tag}  {entry.Value}");
            }
            sb.AppendLine();
        }

        // ── Disclaimer ───────────────────────────────────────────────────
        sb.AppendLine("──────────────────────────────────────────────────────────────");
        sb.AppendLine("  This report contains observations and interpretations that");
        sb.AppendLine("  are confidence-based, not absolute.  Findings indicate what");
        sb.AppendLine("  the evidence is consistent with — they do not constitute");
        sb.AppendLine("  definitive proof of modification, tampering, or authenticity.");
        sb.AppendLine("──────────────────────────────────────────────────────────────");

        return sb.ToString();
    }

    private static void WriteSection(StringBuilder sb, string title)
    {
        sb.AppendLine($"── {title} ──────────────────────────────────────");
    }

    private static void WriteField(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            sb.AppendLine($"  {label,-20} {value}");
    }
}
