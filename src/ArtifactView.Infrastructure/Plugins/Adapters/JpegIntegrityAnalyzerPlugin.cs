using ArtifactView.Contracts.Analysis;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.Infrastructure.Plugins.Adapters;

public sealed class JpegIntegrityAnalyzerPlugin : IAnalyzer
{
    public string Id          => "core.analyzer.jpeg-integrity";
    public string DisplayName => "JPEG integrity analyzer";
    public int    CostHint    => 30;

    public bool Supports(IAnalyzerContext context)
    {
        var ext = Path.GetExtension(context.ItemId);
        return ext.Equals(".jpg",  StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<IReadOnlyList<Finding>> AnalyzeAsync(
        IAnalyzerContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<Finding> findings = JpegIntegrityAnalyzer.Analyze(context.ItemId);
        return ValueTask.FromResult(findings);
    }
}
