namespace ArtifactView.Contracts.Formats;

public interface IFormatDetector
{
    ValueTask<FormatDetectionResult?> DetectAsync(Stream stream, CancellationToken cancellationToken);
}
