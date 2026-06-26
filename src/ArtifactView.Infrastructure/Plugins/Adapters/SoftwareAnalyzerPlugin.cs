using ArtifactView.Contracts.Analysis;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using ArtifactView.Infrastructure.Metadata;

namespace ArtifactView.Infrastructure.Plugins.Adapters;

public sealed class SoftwareAnalyzerPlugin : IAnalyzer
{
    private readonly ImageMetadataExtractor _extractor;

    public SoftwareAnalyzerPlugin() : this(new ImageMetadataExtractor()) { }
    public SoftwareAnalyzerPlugin(ImageMetadataExtractor extractor) => _extractor = extractor;

    public string Id          => "core.analyzer.software";
    public string DisplayName => "Software / workflow analyzer";
    public int    CostHint    => 20;

    public bool Supports(IAnalyzerContext context) => File.Exists(context.ItemId);

    public ValueTask<IReadOnlyList<Finding>> AnalyzeAsync(
        IAnalyzerContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<Finding> findings;
        try
        {
            var (_, summary) = _extractor.Extract(context.ItemId);
            findings = SoftwareAnalyzer.Analyze(summary.SoftwareTag);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[SoftwareAnalyzerPlugin] Metadata read failed for '{context.ItemId}': {ex.Message}");
            findings = [];
        }
        return ValueTask.FromResult(findings);
    }
}
