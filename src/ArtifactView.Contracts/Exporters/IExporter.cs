namespace ArtifactView.Contracts.Exporters;

public interface IExporter
{
    string Id { get; }
    string DisplayName { get; }
    bool SupportsFormat(string formatId);
    ValueTask<ExportResult> ExportAsync(IExportContext context, CancellationToken cancellationToken);
}
