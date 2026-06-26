using ArtifactView.Contracts.Analysis;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.Infrastructure.Plugins.Adapters;

public sealed class FileHashAnalyzerPlugin : IAnalyzer
{
    public string Id          => "core.analyzer.file-hash";
    public string DisplayName => "File hash analyzer";
    public int    CostHint    => 50;

    public bool Supports(IAnalyzerContext context) => File.Exists(context.ItemId);

    public ValueTask<IReadOnlyList<Finding>> AnalyzeAsync(
        IAnalyzerContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<Finding> findings = [FileHashAnalyzer.Analyze(context.ItemId)];
        return ValueTask.FromResult(findings);
    }
}
