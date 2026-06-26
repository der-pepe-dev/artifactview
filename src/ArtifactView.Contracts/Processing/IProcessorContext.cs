namespace ArtifactView.Contracts.Processing;

public interface IProcessorContext
{
    string ItemId { get; }
    IReadOnlyList<string> AvailableArtifacts { get; }
}
