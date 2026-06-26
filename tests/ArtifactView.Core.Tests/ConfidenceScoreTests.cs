using ArtifactView.Core.Models;
using System.Threading.Tasks;

namespace ArtifactView.Core.Tests;

public sealed class ConfidenceScoreTests
{
    [Test]
    [Arguments(-1, "Unknown")]
    [Arguments(10, "Very low")]
    [Arguments(35, "Low")]
    [Arguments(50, "Moderate")]
    [Arguments(70, "High")]
    [Arguments(95, "Very high")]
    public async Task Returns_expected_label(int value, string expected)
    {
        var score = new ConfidenceScore(value);
        await Assert.That(score.Label).IsEqualTo(expected);
    }
}