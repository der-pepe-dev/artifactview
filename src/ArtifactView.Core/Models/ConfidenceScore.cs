namespace ArtifactView.Core.Models;

public readonly record struct ConfidenceScore(int Value)
{
    public static ConfidenceScore Unknown => new(-1);

    public string Label =>
        Value switch
        {
            < 0 => "Unknown",
            <= 19 => "Very low",
            <= 39 => "Low",
            <= 59 => "Moderate",
            <= 79 => "High",
            _ => "Very high"
        };
}
