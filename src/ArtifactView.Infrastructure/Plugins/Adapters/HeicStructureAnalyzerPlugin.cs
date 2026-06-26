using ArtifactView.Contracts.Analysis;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.Infrastructure.Plugins.Adapters;

public sealed class HeicStructureAnalyzerPlugin : IAnalyzer
{
    public string Id          => "core.analyzer.heic-structure";
    public string DisplayName => "HEIC structure analyzer";
    public int    CostHint    => 20;

    public bool Supports(IAnalyzerContext context)
    {
        var ext = Path.GetExtension(context.ItemId);
        return ext.Equals(".heic", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".heif", StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<IReadOnlyList<Finding>> AnalyzeAsync(
        IAnalyzerContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<Finding> findings;
        try
        {
            using var stream = File.OpenRead(context.ItemId);
            findings = HeicStructureAnalyzer.Analyze(stream);
        }
        catch
        {
            findings = [];
        }

        return ValueTask.FromResult(findings);
    }
}
