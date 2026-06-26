using ArtifactView.Contracts.Analysis;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using ArtifactView.Infrastructure.Metadata;

namespace ArtifactView.Infrastructure.Plugins.Adapters;

public sealed class TimestampConsistencyAnalyzerPlugin : IAnalyzer
{
    private readonly ImageMetadataExtractor _extractor;

    public TimestampConsistencyAnalyzerPlugin() : this(new ImageMetadataExtractor()) { }
    public TimestampConsistencyAnalyzerPlugin(ImageMetadataExtractor extractor) => _extractor = extractor;

    public string Id          => "core.analyzer.timestamp";
    public string DisplayName => "Timestamp consistency analyzer";
    public int    CostHint    => 20;

    public bool Supports(IAnalyzerContext context) => File.Exists(context.ItemId);

    public ValueTask<IReadOnlyList<Finding>> AnalyzeAsync(
        IAnalyzerContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<Finding> findings;
        try
        {
            var (_, summary) = _extractor.Extract(context.ItemId);
            findings = TimestampConsistencyAnalyzer.Analyze(
                context.ItemId,
                summary.CaptureDate,
                summary.DateTimeDigitized,
                summary.DateTimeModified);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[TimestampConsistencyAnalyzerPlugin] Metadata read failed for '{context.ItemId}': {ex.Message}");
            findings = [];
        }
        return ValueTask.FromResult(findings);
    }
}
