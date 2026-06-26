using System.Text.Json;
using System.Text.Json.Serialization;
using ArtifactView.Contracts.Exporters;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Metadata;

namespace ArtifactView.Infrastructure.Plugins.Adapters;

public sealed class FindingsReportJsonExporterPlugin : IExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented               = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        Converters                  = { new JsonStringEnumConverter() }
    };

    public string Id          => "core.exporter.findings-json";
    public string DisplayName => "Findings JSON export";

    public bool SupportsFormat(string formatId) => true;

    public ValueTask<ExportResult> ExportAsync(IExportContext context, CancellationToken cancellationToken)
    {
        var row      = context.Services.GetService(typeof(MediaEntityRow)) as MediaEntityRow;
        var summary  = context.Services.GetService(typeof(ExifSummary))    as ExifSummary;
        var findings = context.Services.GetService(typeof(IReadOnlyList<Finding>)) as IReadOnlyList<Finding> ?? [];
        var contribs = context.Services.GetService(typeof(IReadOnlyList<EvidenceContributor>))
                           as IReadOnlyList<EvidenceContributor> ?? [];
        var reconciled = context.Services.GetService(typeof(IReadOnlyList<ReconciledFieldValue>))
                             as IReadOnlyList<ReconciledFieldValue>;

        var payload = new FindingsJsonPayload
        {
            GeneratedUtc    = DateTime.UtcNow,
            ItemId          = context.ItemId,
            DisplayName     = row?.DisplayName ?? Path.GetFileName(context.ItemId),
            LogicalPath     = row?.LogicalPath ?? context.ItemId,
            PresenceState   = row?.PresenceState,
            CameraModel     = summary?.CameraModel,
            CaptureDate     = summary?.CaptureDate,
            Contributors    = contribs.Select(c => new ContributorRecord(c.SourceKind, c.Description, c.Confidence)).ToList(),
            ReconciledFields = reconciled?.Select(r => new ReconciledFieldRecord(
                r.FieldName, r.PreferredValue, r.SourceUsed, r.Status.ToString(), r.Confidence)).ToList(),
            Findings        = findings.Select(f => new FindingRecord(
                f.Id, f.Category, f.Observation, f.Interpretation,
                f.ObservationConfidence.Value, f.InterpretationConfidence.Value,
                f.ReviewPriority.ToString(), f.Provenance,
                f.SupportingFactors.Count  > 0 ? f.SupportingFactors  : null,
                f.ConflictingFactors.Count > 0 ? f.ConflictingFactors : null)).ToList()
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(context.DestinationPath)!);
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            File.WriteAllText(context.DestinationPath, json);
            return ValueTask.FromResult(new ExportResult
            {
                Success      = true,
                OutputPath   = context.DestinationPath,
                BytesWritten = new FileInfo(context.DestinationPath).Length
            });
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(new ExportResult
            {
                Success      = false,
                ErrorMessage = ex.Message
            });
        }
    }

    private sealed record FindingsJsonPayload
    {
        public DateTime GeneratedUtc      { get; init; }
        public required string ItemId     { get; init; }
        public required string DisplayName { get; init; }
        public required string LogicalPath { get; init; }
        public string? PresenceState      { get; init; }
        public string? CameraModel        { get; init; }
        public DateTime? CaptureDate      { get; init; }
        public List<ContributorRecord>     Contributors     { get; init; } = [];
        public List<ReconciledFieldRecord>? ReconciledFields { get; init; }
        public List<FindingRecord>          Findings         { get; init; } = [];
    }

    private sealed record ContributorRecord(string SourceKind, string Description, double Confidence);

    private sealed record ReconciledFieldRecord(
        string FieldName, string? PreferredValue, string? SourceUsed, string Status, ConfidenceScore Confidence);

    private sealed record FindingRecord(
        string Id, string Category, string Observation, string? Interpretation,
        int ObservationConfidence, int InterpretationConfidence,
        string ReviewPriority, string? Provenance,
        IReadOnlyList<string>? SupportingFactors,
        IReadOnlyList<string>? ConflictingFactors);
}
