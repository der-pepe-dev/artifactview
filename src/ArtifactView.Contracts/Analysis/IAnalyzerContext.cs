namespace ArtifactView.Contracts.Analysis;

public interface IAnalyzerContext
{
    string ItemId { get; }
    IReadOnlyList<string> Capabilities { get; }
    IServiceProvider Services { get; }
}
