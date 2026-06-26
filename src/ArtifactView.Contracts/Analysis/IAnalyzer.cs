using ArtifactView.Core.Models;

namespace ArtifactView.Contracts.Analysis;

public interface IAnalyzer
{
    string Id { get; }
    string DisplayName { get; }
    int CostHint { get; }
    bool Supports(IAnalyzerContext context);
    ValueTask<IReadOnlyList<Finding>> AnalyzeAsync(IAnalyzerContext context, CancellationToken cancellationToken);
}
