using System.Drawing.Imaging;
using ArtifactView.Contracts.Processing;
using ArtifactView.Infrastructure.Metadata;

namespace ArtifactView.Infrastructure.Plugins.Adapters;

// Produces a lo-fi reconstruction from the embedded EXIF IFD1 thumbnail.
//
// The EXIF thumbnail is decoded and re-encoded as a lossless PNG — not written
// as raw JPEG bytes. Exact bit-copy extraction (preserving the original JPEG)
// is handled separately by ReconstructionViewModel.SaveExifThumbnail().
//
// CLAUDE.md rule: all reconstructed/composited outputs must use a lossless
// export format. Only exact binary extractions may preserve the original lossy format.
public sealed class LoFiReconstructionProcessorPlugin : IProcessorPlugin
{
    private static readonly HashSet<string> s_supportedExts =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".tif", ".tiff" };

    private readonly ImageMetadataExtractor _extractor;

    public LoFiReconstructionProcessorPlugin(ImageMetadataExtractor extractor)
        => _extractor = extractor;

    public string Id             => "core.processor.lofi-exif-thumb";
    public string DisplayName    => "Lo-fi reconstruction from EXIF thumbnail";
    public bool   IsEvidenceSafe => true;

    public bool Supports(IProcessorContext context)
    {
        if (!File.Exists(context.ItemId)) return false;
        if (!s_supportedExts.Contains(Path.GetExtension(context.ItemId))) return false;
        try
        {
            var (_, summary) = _extractor.Extract(context.ItemId);
            return summary.HasThumbnail;
        }
        catch { return false; }
    }

    public ValueTask<ProcessorResult> ProcessAsync(
        IProcessorContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        byte[]? thumbBytes;
        try   { thumbBytes = ImageMetadataExtractor.ExtractThumbnailBytes(context.ItemId); }
        catch { thumbBytes = null; }

        if (thumbBytes is null || thumbBytes.Length == 0)
            return ValueTask.FromResult(new ProcessorResult
            {
                ResultKind = "failed",
                ArtifactId = "exif-thumbnail"
            });

        var baseName   = Path.GetFileNameWithoutExtension(context.ItemId);
        var outputDir  = Path.Combine(Path.GetTempPath(), "ArtifactView", "reconstructions");
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, $"{baseName}__lofi_reconstruction__exifthumb.png");

        // Decode the JPEG thumbnail, then re-encode as lossless PNG.
        using var ms     = new MemoryStream(thumbBytes);
        using var bitmap = System.Drawing.Image.FromStream(ms);
        bitmap.Save(outputPath, ImageFormat.Png);

        return ValueTask.FromResult(new ProcessorResult
        {
            ResultKind = "lofi-reconstruction",
            ArtifactId = "exif-thumbnail",
            OutputPath = outputPath
        });
    }
}
