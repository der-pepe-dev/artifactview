namespace ArtifactView.Contracts.Exporters;

public interface IExportContext
{
    string ItemId { get; }
    string DestinationPath { get; }
    IReadOnlyDictionary<string, string> Options { get; }
    IServiceProvider Services { get; }
}
