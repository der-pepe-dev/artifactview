using ArtifactView.Core.Models;
using Xunit;

namespace ArtifactView.Core.Tests;

public sealed class ConfidenceScoreTests
{
    [Theory]
    [InlineData(-1, "Unknown")]
    [InlineData(10, "Very low")]
    [InlineData(35, "Low")]
    [InlineData(50, "Moderate")]
    [InlineData(70, "High")]
    [InlineData(95, "Very high")]
    public void Returns_expected_label(int value, string expected)
    {
        var score = new ConfidenceScore(value);
        Assert.Equal(expected, score.Label);
    }
}
