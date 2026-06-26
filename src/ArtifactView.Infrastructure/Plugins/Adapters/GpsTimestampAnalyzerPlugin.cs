using ArtifactView.Contracts.Analysis;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using ArtifactView.Infrastructure.Metadata;

namespace ArtifactView.Infrastructure.Plugins.Adapters;

public sealed class GpsTimestampAnalyzerPlugin : IAnalyzer
{
    private readonly ImageMetadataExtractor _extractor;

    public GpsTimestampAnalyzerPlugin(ImageMetadataExtractor extractor)
        => _extractor = extractor;

    public string Id          => "core.analyzer.gps-timestamp";
    public string DisplayName => "GPS timestamp consistency analyzer";
    public int    CostHint    => 20;

    public bool Supports(IAnalyzerContext context) => File.Exists(context.ItemId);

    public ValueTask<IReadOnlyList<Finding>> AnalyzeAsync(
        IAnalyzerContext context, CancellationToken cancellationToken)
    {
        ExifSummary summary;
        try { (_, summary) = _extractor.Extract(context.ItemId); }
        catch { return ValueTask.FromResult<IReadOnlyList<Finding>>([]); }

        if (summary.GpsDateTimeUtc is null)
            return ValueTask.FromResult<IReadOnlyList<Finding>>([]);

        IReadOnlyList<Finding> findings = GpsTimestampAnalyzer.Analyze(
            summary.CaptureDate, summary.GpsDateTimeUtc);

        return ValueTask.FromResult(findings);
    }
}
