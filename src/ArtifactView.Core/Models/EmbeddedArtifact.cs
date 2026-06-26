namespace ArtifactView.Core.Models;

public sealed class EmbeddedArtifact
{
    public required Guid Id { get; init; }
    public required EmbeddedArtifactType Type { get; init; }
    public required string DisplayName { get; init; }
    public long? Offset { get; init; }
    public long? Length { get; init; }
    public string? MimeType { get; init; }
    public string? BlobKey { get; init; }
    public ConfidenceScore ParseConfidence { get; init; } = ConfidenceScore.Unknown;
    public DecodeStatus DecodeStatus { get; init; } = DecodeStatus.NotAttempted;

    // Source metadata namespace or detection method (e.g. "Exif IFD1", "XmpBase64", "MPF").
    public string? SourceNamespace { get; init; }

    // SHA-256 hex of the extracted payload bytes, when computed.
    public string? Hash { get; init; }

    public int? WidthPixels  { get; init; }
    public int? HeightPixels { get; init; }

    // Filesystem path where this artifact was last exported, if any.
    public string? ExportPath { get; init; }

    // Pre-decoded payload bytes for artifacts stored inline (e.g. base64
    // in XMP).  When set, extraction uses this directly instead of seeking
    // to Offset/Length in the source file.
    public byte[]? Data { get; init; }

    public bool IsExtractable => Data is { Length: > 0 } || (Offset is not null && Length is > 0);
}
