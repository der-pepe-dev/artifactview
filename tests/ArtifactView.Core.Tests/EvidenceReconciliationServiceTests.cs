using ArtifactView.Core.Models;
using ArtifactView.Core.Services;
using System.Threading.Tasks;

namespace ArtifactView.Core.Tests;

public sealed class EvidenceReconciliationServiceTests
{
    private readonly EvidenceReconciliationService _sut = new();

    [Test]
    public async Task Returns_ambiguous_when_no_candidates()
    {
        var result = _sut.Reconcile("CameraModel", []);

        await Assert.That(result.Status).IsEqualTo(MergeStatus.Ambiguous);
        Assert.Null(result.PreferredValue);
    }

    [Test]
    public async Task Returns_resolved_for_single_candidate()
    {
        var candidates = new[]
        {
            new FieldCandidate
            {
                FieldName = "CameraModel",
                SourceType = "ExifMetadata",
                RawValue = "Canon EOS R5",
                Confidence = new ConfidenceScore(80)
            }
        };

        var result = _sut.Reconcile("CameraModel", candidates);

        await Assert.That(result.Status).IsEqualTo(MergeStatus.Resolved);
        await Assert.That(result.PreferredValue).IsEqualTo("Canon EOS R5");
    }

    [Test]
    public async Task Returns_merged_when_multiple_candidates_agree_on_value()
    {
        var candidates = new[]
        {
            new FieldCandidate
            {
                FieldName  = "CameraModel",
                SourceType = "ExifMetadata",
                RawValue   = "Canon EOS R5",
                Confidence = new ConfidenceScore(80)
            },
            new FieldCandidate
            {
                FieldName  = "CameraModel",
                SourceType = "SidecarFile",
                RawValue   = "Canon EOS R5",
                Confidence = new ConfidenceScore(50)
            }
        };

        var result = _sut.Reconcile("CameraModel", candidates);

        await Assert.That(result.Status).IsEqualTo(MergeStatus.Merged);
        await Assert.That(result.PreferredValue).IsEqualTo("Canon EOS R5");
        await Assert.That(result.Candidates.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Returns_conflicted_when_candidates_have_different_values()
    {
        var candidates = new[]
        {
            new FieldCandidate
            {
                FieldName  = "CameraModel",
                SourceType = "ExifMetadata",
                RawValue   = "Canon EOS R5",
                Confidence = new ConfidenceScore(80)
            },
            new FieldCandidate
            {
                FieldName  = "CameraModel",
                SourceType = "SidecarFile",
                RawValue   = "Canon EOS R5 Mark II",
                Confidence = new ConfidenceScore(50)
            }
        };

        var result = _sut.Reconcile("CameraModel", candidates);

        await Assert.That(result.Status).IsEqualTo(MergeStatus.Conflicted);
        // Highest-confidence candidate is still preferred even under conflict.
        await Assert.That(result.PreferredValue).IsEqualTo("Canon EOS R5");
    }

    [Test]
    public async Task Preserves_all_candidates_in_result()
    {
        var candidates = new[]
        {
            new FieldCandidate { FieldName = "GPS", SourceType = "ExifMetadata", RawValue = "51.5,0.1", Confidence = new ConfidenceScore(90) },
            new FieldCandidate { FieldName = "GPS", SourceType = "SidecarFile",  RawValue = "51.4,0.2", Confidence = new ConfidenceScore(60) }
        };

        var result = _sut.Reconcile("GPS", candidates);

        // Raw evidence from all sources must be accessible.
        await Assert.That(result.Candidates.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SourceUsed_set_to_preferred_candidate_source_type()
    {
        var candidates = new[]
        {
            new FieldCandidate
            {
                FieldName  = "Software",
                SourceType = "ExifMetadata",
                RawValue   = "Lightroom 6",
                Confidence = new ConfidenceScore(90)
            },
            new FieldCandidate
            {
                FieldName  = "Software",
                SourceType = "XmpMetadata",
                RawValue   = "Lightroom 6",
                Confidence = new ConfidenceScore(70)
            }
        };

        var result = _sut.Reconcile("Software", candidates);

        await Assert.That(result.SourceUsed).IsEqualTo("ExifMetadata");
    }

    [Test]
    public void SourceUsed_null_when_no_candidates()
    {
        var result = _sut.Reconcile("Software", []);

        Assert.Null(result.SourceUsed);
    }
}