using Xunit;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class SoftwareAnalyzerTests
{
    [Theory]
    [InlineData("Adobe Photoshop CC 2024")]
    [InlineData("Adobe Lightroom Classic 13.0")]
    [InlineData("GIMP 2.10.36")]
    [InlineData("Snapseed")]
    [InlineData("Instagram")]
    [InlineData("PicsArt")]
    [InlineData("WhatsApp")]
    public void KnownEditingTool_ReturnsEditingFinding(string software)
    {
        var findings = SoftwareAnalyzer.Analyze(software);
        Assert.Single(findings);
        Assert.Equal("software-editing-tool", findings[0].Id);
        Assert.Equal(ReviewPriority.Medium, findings[0].ReviewPriority);
        Assert.Contains(software, findings[0].Observation);
    }

    [Theory]
    [InlineData("Nikon COOLPIX P950")]
    [InlineData("16.7.2")]
    [InlineData("Samsung Galaxy S24")]
    public void UnknownSoftware_ReturnsPresentFinding(string software)
    {
        var findings = SoftwareAnalyzer.Analyze(software);
        Assert.Single(findings);
        Assert.Equal("software-field-present", findings[0].Id);
        Assert.Equal(ReviewPriority.None, findings[0].ReviewPriority);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrWhitespace_ReturnsEmpty(string? software)
    {
        Assert.Empty(SoftwareAnalyzer.Analyze(software));
    }

    [Fact]
    public void CaseInsensitiveMatch()
    {
        var findings = SoftwareAnalyzer.Analyze("ADOBE PHOTOSHOP CS6");
        Assert.Single(findings);
        Assert.Equal("software-editing-tool", findings[0].Id);
    }
}
