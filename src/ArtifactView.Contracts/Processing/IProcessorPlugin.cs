namespace ArtifactView.Contracts.Processing;

public interface IProcessorPlugin
{
    string Id { get; }
    string DisplayName { get; }
    bool IsEvidenceSafe { get; }
    bool Supports(IProcessorContext context);
    ValueTask<ProcessorResult> ProcessAsync(IProcessorContext context, CancellationToken cancellationToken);
}
