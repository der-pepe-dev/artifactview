namespace ArtifactView.Infrastructure.Sources.AppDb;

// A single correlation result: a media file linked to an entry in a known app database.
public sealed record AppDbCorrelationEntry(
    // The media file this correlation applies to (display name, used for matching).
    string MediaFilename,
    // Which app database was matched.
    string AppName,
    // Human-readable description of what was found.
    string Summary,
    // Confidence level: None / Low / Medium / High.
    AppDbCorrelationConfidence Confidence
);

public enum AppDbCorrelationConfidence { Low, Medium, High }
