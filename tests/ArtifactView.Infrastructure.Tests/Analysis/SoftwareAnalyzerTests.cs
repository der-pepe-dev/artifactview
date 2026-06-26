using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class SoftwareAnalyzerTests
{
    [Test]
    [Arguments("Adobe Photoshop CC 2024")]
    [Arguments("Adobe Lightroom Classic 13.0")]
    [Arguments("GIMP 2.10.36")]
    [Arguments("Snapseed")]
    [Arguments("Instagram")]
    [Arguments("PicsArt")]
    [Arguments("WhatsApp")]
    public async Task KnownEditingTool_ReturnsEditingFinding(string software)
    {
        var findings = SoftwareAnalyzer.Analyze(software);
        await Assert.That(findings).HasSingleItem();
        await Assert.That(findings[0].Id).IsEqualTo("software-editing-tool");
        await Assert.That(findings[0].ReviewPriority).IsEqualTo(ReviewPriority.Medium);
        await Assert.That(findings[0].Observation).Contains(software);
    }

    [Test]
    [Arguments("Nikon COOLPIX P950")]
    [Arguments("16.7.2")]
    [Arguments("Samsung Galaxy S24")]
    public async Task UnknownSoftware_ReturnsPresentFinding(string software)
    {
        var findings = SoftwareAnalyzer.Analyze(software);
        await Assert.That(findings).HasSingleItem();
        await Assert.That(findings[0].Id).IsEqualTo("software-field-present");
        await Assert.That(findings[0].ReviewPriority).IsEqualTo(ReviewPriority.None);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task NullOrWhitespace_ReturnsEmpty(string? software)
    {
        await Assert.That(SoftwareAnalyzer.Analyze(software)).IsEmpty();
    }

    [Test]
    public async Task CaseInsensitiveMatch()
    {
        var findings = SoftwareAnalyzer.Analyze("ADOBE PHOTOSHOP CS6");
        await Assert.That(findings).HasSingleItem();
        await Assert.That(findings[0].Id).IsEqualTo("software-editing-tool");
    }
}