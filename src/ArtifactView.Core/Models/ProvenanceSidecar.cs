namespace ArtifactView.Core.Models;

// Written alongside every exported reconstruction or extracted artifact.
// Documents the evidence chain so the output can never be mistaken for an
// unmodified original or confused with a different source.
public sealed class ProvenanceSidecar
{
    public required string ExportedAt           { get; init; }   // ISO 8601 UTC
    public required string SourceFile           { get; init; }   // path or "<ghost>"
    public required string SourcePresence       { get; init; }   // "present" | "ghost" | "cache-only" | "unknown"
    public required string ExtractionSource     { get; init; }   // "exif-thumbnail" | "thumbs-db" | "thumbcache" | "zb-thumbnail" | "embedded-artifact"
    public required string ExtractionMethod     { get; init; }   // "bit-copy" | "lo-fi-reconstruction" | "composite"
    public required string ReconstructionCategory { get; init; } // "exact-artifact-extraction" | "lo-fi-reconstruction" | "composite-reconstruction"
    public required string OutputFormat         { get; init; }   // MIME type of output file
    public required long   ByteCount            { get; init; }
    public string?         Warning              { get; init; }   // non-null when output is NOT the original
    public IReadOnlyList<string> Contributors   { get; init; } = [];
    public string?         Notes                { get; init; }
}
