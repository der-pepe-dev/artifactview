using ArtifactView.Core.Models;
using ArtifactView.Core.Services;
using Xunit;

namespace ArtifactView.Core.Tests;

public sealed class EvidenceReconciliationServiceTests
{
    private readonly EvidenceReconciliationService _sut = new();

    [Fact]
    public void Returns_ambiguous_when_no_candidates()
    {
        var result = _sut.Reconcile("CameraModel", []);

        Assert.Equal(MergeStatus.Ambiguous, result.Status);
        Assert.Null(result.PreferredValue);
    }

    [Fact]
    public void Returns_resolved_for_single_candidate()
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

        Assert.Equal(MergeStatus.Resolved, result.Status);
        Assert.Equal("Canon EOS R5", result.PreferredValue);
    }

    [Fact]
    public void Returns_merged_when_multiple_candidates_agree_on_value()
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

        Assert.Equal(MergeStatus.Merged, result.Status);
        Assert.Equal("Canon EOS R5", result.PreferredValue);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void Returns_conflicted_when_candidates_have_different_values()
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

        Assert.Equal(MergeStatus.Conflicted, result.Status);
        // Highest-confidence candidate is still preferred even under conflict.
        Assert.Equal("Canon EOS R5", result.PreferredValue);
    }

    [Fact]
    public void Preserves_all_candidates_in_result()
    {
        var candidates = new[]
        {
            new FieldCandidate { FieldName = "GPS", SourceType = "ExifMetadata", RawValue = "51.5,0.1", Confidence = new ConfidenceScore(90) },
            new FieldCandidate { FieldName = "GPS", SourceType = "SidecarFile",  RawValue = "51.4,0.2", Confidence = new ConfidenceScore(60) }
        };

        var result = _sut.Reconcile("GPS", candidates);

        // Raw evidence from all sources must be accessible.
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void SourceUsed_set_to_preferred_candidate_source_type()
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

        Assert.Equal("ExifMetadata", result.SourceUsed);
    }

    [Fact]
    public void SourceUsed_null_when_no_candidates()
    {
        var result = _sut.Reconcile("Software", []);

        Assert.Null(result.SourceUsed);
    }
}
