using System.Collections.Generic;
using System.IO;
using ArtifactView.Contracts.Signatures;
using ArtifactView.Infrastructure.Metadata;

namespace ArtifactView.Infrastructure.Signatures;

// Adapts ExifSummary + file path into ISignatureContext for the signature engine.
public sealed class ExifSignatureContext : ISignatureContext
{
    private readonly ExifSummary _summary;

    public ExifSignatureContext(ExifSummary summary, string filePath)
    {
        _summary = summary;
        FileName = Path.GetFileName(filePath);
    }

    public string? SoftwareTag    => _summary.SoftwareTag;
    public string? CameraModel    => _summary.CameraModel;
    public int?    ImageWidth     => _summary.Width;
    public int?    ImageHeight    => _summary.Height;
    public string? DetectedMimeType => null;
    public string? FileName       { get; }
    public bool    HasGpsData     => !string.IsNullOrEmpty(_summary.GpsText);
    public IReadOnlyList<string> Capabilities => Array.Empty<string>();
}
