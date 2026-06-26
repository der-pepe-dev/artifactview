namespace ArtifactView.Core.Models;

// A single raw metadata tag-value pair read from an image file.
// DirectoryName preserves the source context (e.g. "Exif IFD0", "GPS") for provenance.
public sealed record RawMetadataEntry(string DirectoryName, string TagName, string? Value);
