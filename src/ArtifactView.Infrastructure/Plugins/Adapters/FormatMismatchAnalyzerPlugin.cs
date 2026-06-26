using ArtifactView.Contracts.Analysis;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.Infrastructure.Plugins.Adapters;

public sealed class FormatMismatchAnalyzerPlugin : IAnalyzer
{
    public string Id          => "core.analyzer.format-mismatch";
    public string DisplayName => "Format / extension mismatch analyzer";
    public int    CostHint    => 10;

    public bool Supports(IAnalyzerContext context) => File.Exists(context.ItemId);

    public ValueTask<IReadOnlyList<Finding>> AnalyzeAsync(
        IAnalyzerContext context, CancellationToken cancellationToken)
    {
        var finding = FormatMismatchAnalyzer.Analyze(context.ItemId);
        IReadOnlyList<Finding> findings = finding is not null ? [finding] : [];
        return ValueTask.FromResult(findings);
    }
}
