using ArtifactView.Contracts.Analysis;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using ArtifactView.Infrastructure.Metadata;

namespace ArtifactView.Infrastructure.Plugins.Adapters;

public sealed class TimezoneInferenceAnalyzerPlugin : IAnalyzer
{
    private readonly ImageMetadataExtractor _extractor;

    public TimezoneInferenceAnalyzerPlugin(ImageMetadataExtractor extractor)
        => _extractor = extractor;

    public string Id          => "core.analyzer.timezone-inference";
    public string DisplayName => "Timezone inference from GPS coordinates";
    public int    CostHint    => 15;

    public bool Supports(IAnalyzerContext context) => File.Exists(context.ItemId);

    public ValueTask<IReadOnlyList<Finding>> AnalyzeAsync(
        IAnalyzerContext context, CancellationToken cancellationToken)
    {
        ExifSummary summary;
        try { (_, summary) = _extractor.Extract(context.ItemId); }
        catch { return ValueTask.FromResult<IReadOnlyList<Finding>>([]); }

        if (summary.GpsLongitude is null || summary.GpsDateTimeUtc is null)
            return ValueTask.FromResult<IReadOnlyList<Finding>>([]);

        IReadOnlyList<Finding> findings = TimezoneInferenceAnalyzer.Analyze(
            summary.GpsLongitude, summary.CaptureDate, summary.GpsDateTimeUtc);

        return ValueTask.FromResult(findings);
    }
}
