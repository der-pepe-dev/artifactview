using ArtifactView.Contracts.Analysis;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using ArtifactView.Infrastructure.Metadata;

namespace ArtifactView.Infrastructure.Plugins.Adapters;

public sealed class ThumbnailMismatchAnalyzerPlugin : IAnalyzer
{
    private readonly ImageMetadataExtractor _extractor;

    public ThumbnailMismatchAnalyzerPlugin(ImageMetadataExtractor extractor)
        => _extractor = extractor;

    public string Id          => "core.analyzer.thumbnail-mismatch";
    public string DisplayName => "Thumbnail vs. main image analyzer";
    public int    CostHint    => 25;

    public bool Supports(IAnalyzerContext context) => File.Exists(context.ItemId);

    public ValueTask<IReadOnlyList<Finding>> AnalyzeAsync(
        IAnalyzerContext context, CancellationToken cancellationToken)
    {
        ExifSummary summary;
        try { (_, summary) = _extractor.Extract(context.ItemId); }
        catch { return ValueTask.FromResult<IReadOnlyList<Finding>>([]); }

        if (!summary.HasThumbnail)
            return ValueTask.FromResult<IReadOnlyList<Finding>>([]);

        IReadOnlyList<Finding> findings = ThumbnailMismatchAnalyzer.Analyze(
            summary.Width,  summary.Height,
            summary.ThumbnailWidth, summary.ThumbnailHeight);

        return ValueTask.FromResult(findings);
    }
}
