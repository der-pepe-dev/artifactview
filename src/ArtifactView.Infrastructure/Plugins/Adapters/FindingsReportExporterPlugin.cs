using ArtifactView.Contracts.Exporters;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Metadata;
using ArtifactView.Infrastructure.Reports;

namespace ArtifactView.Infrastructure.Plugins.Adapters;

public sealed class FindingsReportExporterPlugin : IExporter
{
    public string Id          => "core.exporter.findings-txt";
    public string DisplayName => "Findings text report";

    public bool SupportsFormat(string formatId) => true;

    public ValueTask<ExportResult> ExportAsync(IExportContext context, CancellationToken cancellationToken)
    {
        var row        = context.Services.GetService(typeof(MediaEntityRow))       as MediaEntityRow;
        var summary    = context.Services.GetService(typeof(ExifSummary))          as ExifSummary;
        var findings   = context.Services.GetService(typeof(IReadOnlyList<Finding>)) as IReadOnlyList<Finding> ?? [];
        var contribs   = context.Services.GetService(typeof(IReadOnlyList<EvidenceContributor>))
                             as IReadOnlyList<EvidenceContributor> ?? [];
        var rawMeta    = context.Services.GetService(typeof(IReadOnlyList<RawMetadataEntry>))
                             as IReadOnlyList<RawMetadataEntry>;
        var reconciled = context.Services.GetService(typeof(IReadOnlyList<ReconciledFieldValue>))
                             as IReadOnlyList<ReconciledFieldValue>;

        if (row is null)
        {
            row = new MediaEntityRow
            {
                SortOrder      = 2,
                DisplayName    = Path.GetFileName(context.ItemId),
                LogicalPath    = context.ItemId,
                PresenceState  = "Present",
                PrimarySourceType = "Live file"
            };
        }

        try
        {
            var report = FindingsReportExporter.Generate(row, summary, findings, contribs, rawMeta, reconciled);
            Directory.CreateDirectory(Path.GetDirectoryName(context.DestinationPath)!);
            File.WriteAllText(context.DestinationPath, report);
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
}
