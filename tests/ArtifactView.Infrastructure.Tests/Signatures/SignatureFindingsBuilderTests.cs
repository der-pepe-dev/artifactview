using ArtifactView.Contracts.Signatures;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Signatures;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Signatures;

public sealed class SignatureFindingsBuilderTests
{
    private static SignatureMatchResult MakeMatch(
        string profileId, string name, MatchStrength strength,
        string[]? supportingFactors = null, string? note = null) =>
        new()
        {
            ProfileId         = profileId,
            ProfileName       = name,
            Strength          = strength,
            SupportingFactors = supportingFactors ?? [],
            Notes             = note
        };

    private static SignatureEngineResult EmptyResult() =>
        new([], null, []);

    private static SignatureEngineResult SingleResult(SignatureMatchResult m) =>
        new([m], m, []);

    // ── No matches ──────────────────────────────────────────────────────────

    [Fact]
    public void AddFindings_produces_no_findings_when_no_matches()
    {
        var findings = new List<Finding>();
        SignatureFindingsBuilder.AddFindings(EmptyResult(), findings);
        Assert.Empty(findings);
    }

    // ── Clean match (no conflict) ────────────────────────────────────────────

    [Fact]
    public void AddFindings_produces_one_finding_for_single_match()
    {
        var match   = MakeMatch("platform.iphone", "Apple iPhone / iPad camera", MatchStrength.Strong);
        var findings = new List<Finding>();
        SignatureFindingsBuilder.AddFindings(SingleResult(match), findings);

        Assert.Single(findings);
    }

    [Fact]
    public void AddFindings_finding_contains_profile_name_in_observation()
    {
        var match   = MakeMatch("platform.iphone", "Apple iPhone", MatchStrength.Strong);
        var findings = new List<Finding>();
        SignatureFindingsBuilder.AddFindings(SingleResult(match), findings);

        Assert.Contains("Apple iPhone", findings[0].Observation);
    }

    [Fact]
    public void AddFindings_finding_includes_notes_when_present()
    {
        var match   = MakeMatch("platform.iphone", "iPhone", MatchStrength.Strong,
                                note: "Consistent with iOS camera app output.");
        var findings = new List<Finding>();
        SignatureFindingsBuilder.AddFindings(SingleResult(match), findings);

        Assert.Contains("iOS camera app output", findings[0].Observation);
    }

    [Fact]
    public void AddFindings_clean_match_has_none_review_priority()
    {
        var match   = MakeMatch("platform.iphone", "iPhone", MatchStrength.Strong);
        var findings = new List<Finding>();
        SignatureFindingsBuilder.AddFindings(SingleResult(match), findings);

        Assert.Equal(ReviewPriority.None, findings[0].ReviewPriority);
    }

    [Fact]
    public void AddFindings_strong_match_has_confidence_85()
    {
        var match   = MakeMatch("platform.iphone", "iPhone", MatchStrength.Strong);
        var findings = new List<Finding>();
        SignatureFindingsBuilder.AddFindings(SingleResult(match), findings);

        Assert.Equal(85, findings[0].ObservationConfidence.Value);
    }

    [Fact]
    public void AddFindings_moderate_match_has_confidence_65()
    {
        var match   = MakeMatch("platform.samsung", "Samsung", MatchStrength.Moderate);
        var findings = new List<Finding>();
        SignatureFindingsBuilder.AddFindings(SingleResult(match), findings);

        Assert.Equal(65, findings[0].ObservationConfidence.Value);
    }

    [Fact]
    public void AddFindings_supporting_factors_forwarded()
    {
        var match   = MakeMatch("platform.iphone", "iPhone", MatchStrength.Strong,
                                supportingFactors: ["Camera model: \"iPhone 14\"", "GPS data present"]);
        var findings = new List<Finding>();
        SignatureFindingsBuilder.AddFindings(SingleResult(match), findings);

        Assert.Contains("Camera model: \"iPhone 14\"", findings[0].SupportingFactors);
    }

    [Fact]
    public void AddFindings_sets_provenance()
    {
        var match   = MakeMatch("platform.iphone", "iPhone", MatchStrength.Strong);
        var findings = new List<Finding>();
        SignatureFindingsBuilder.AddFindings(SingleResult(match), findings);

        Assert.Equal("core.sig.workflow-core", findings[0].Provenance);
    }

    // ── Conflict finding ─────────────────────────────────────────────────────

    [Fact]
    public void AddFindings_conflict_produces_medium_priority_finding()
    {
        var m1 = MakeMatch("platform.iphone",       "iPhone", MatchStrength.Strong);
        var m2 = MakeMatch("platform.google-pixel", "Pixel",  MatchStrength.Moderate);
        var conflict = new SignatureConflict("platform", [m1, m2]);
        var result   = new SignatureEngineResult([m1, m2], m1, [conflict]);

        var findings = new List<Finding>();
        SignatureFindingsBuilder.AddFindings(result, findings);

        var conflictFinding = findings.Single(f => f.ReviewPriority == ReviewPriority.Medium);
        Assert.NotNull(conflictFinding);
    }

    [Fact]
    public void AddFindings_conflict_observation_names_all_matches()
    {
        var m1 = MakeMatch("platform.iphone",       "iPhone", MatchStrength.Strong);
        var m2 = MakeMatch("platform.google-pixel", "Pixel",  MatchStrength.Moderate);
        var conflict = new SignatureConflict("platform", [m1, m2]);
        var result   = new SignatureEngineResult([m1, m2], m1, [conflict]);

        var findings = new List<Finding>();
        SignatureFindingsBuilder.AddFindings(result, findings);

        var obs = findings.Single(f => f.ReviewPriority == ReviewPriority.Medium).Observation;
        Assert.Contains("iPhone", obs);
        Assert.Contains("Pixel",  obs);
    }

    [Fact]
    public void AddFindings_conflict_has_interpretation()
    {
        var m1 = MakeMatch("platform.iphone",       "iPhone", MatchStrength.Strong);
        var m2 = MakeMatch("platform.google-pixel", "Pixel",  MatchStrength.Moderate);
        var conflict = new SignatureConflict("platform", [m1, m2]);
        var result   = new SignatureEngineResult([m1, m2], m1, [conflict]);

        var findings = new List<Finding>();
        SignatureFindingsBuilder.AddFindings(result, findings);

        var f = findings.Single(f => f.ReviewPriority == ReviewPriority.Medium);
        Assert.NotNull(f.Interpretation);
        Assert.NotEmpty(f.ConflictingFactors);
    }

    // ── Multiple categories ──────────────────────────────────────────────────

    [Fact]
    public void AddFindings_produces_one_finding_per_category()
    {
        var platform  = MakeMatch("platform.iphone",    "iPhone",    MatchStrength.Strong);
        var workflow  = MakeMatch("workflow.lightroom", "Lightroom", MatchStrength.Strong);
        var result    = new SignatureEngineResult([platform, workflow], platform, []);

        var findings = new List<Finding>();
        SignatureFindingsBuilder.AddFindings(result, findings);

        Assert.Equal(2, findings.Count);
    }
}
