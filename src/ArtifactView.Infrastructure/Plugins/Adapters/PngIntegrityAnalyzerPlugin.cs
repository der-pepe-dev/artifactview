using ArtifactView.Contracts.Analysis;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.Infrastructure.Plugins.Adapters;

public sealed class PngIntegrityAnalyzerPlugin : IAnalyzer
{
    public string Id          => "core.analyzer.png-integrity";
    public string DisplayName => "PNG integrity analyzer";
    public int    CostHint    => 30;

    public bool Supports(IAnalyzerContext context) =>
        Path.GetExtension(context.ItemId).Equals(".png", StringComparison.OrdinalIgnoreCase);

    public ValueTask<IReadOnlyList<Finding>> AnalyzeAsync(
        IAnalyzerContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<Finding> findings = PngIntegrityAnalyzer.Analyze(context.ItemId);
        return ValueTask.FromResult(findings);
    }
}
