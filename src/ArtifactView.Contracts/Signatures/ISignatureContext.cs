namespace ArtifactView.Contracts.Signatures;

public interface ISignatureContext
{
    string? SoftwareTag { get; }
    string? CameraModel { get; }
    int? ImageWidth { get; }
    int? ImageHeight { get; }
    string? DetectedMimeType { get; }
    string? FileName { get; }
    bool HasGpsData { get; }
    IReadOnlyList<string> Capabilities { get; }
}
