using ArtifactView.Contracts.Signatures;
using ArtifactView.Infrastructure.Signatures;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Signatures;

public sealed class SignatureEngineTests
{
    // ── Stub helpers ────────────────────────────────────────────────────────

    private static SignatureMatchResult MakeMatch(string profileId, string name, MatchStrength strength) =>
        new() { ProfileId = profileId, ProfileName = name, Strength = strength };

    private sealed class StubPack : ISignatureRulePack
    {
        private readonly IReadOnlyList<SignatureMatchResult> _results;

        public StubPack(params SignatureMatchResult[] results) => _results = results;

        public string Id          => "stub";
        public string DisplayName => "Stub";
        public string Version     => "1.0";

        public IReadOnlyList<SignatureMatchResult> Match(ISignatureContext _) => _results;
    }

    private sealed class ThrowingPack : ISignatureRulePack
    {
        public string Id          => "thrower";
        public string DisplayName => "Thrower";
        public string Version     => "1.0";

        public IReadOnlyList<SignatureMatchResult> Match(ISignatureContext _) =>
            throw new InvalidOperationException("pack error");
    }

    private sealed class NullContext : ISignatureContext
    {
        public string? SoftwareTag      => null;
        public string? CameraModel      => null;
        public int?    ImageWidth       => null;
        public int?    ImageHeight      => null;
        public string? DetectedMimeType => null;
        public string? FileName         => null;
        public bool    HasGpsData       => false;
        public IReadOnlyList<string> Capabilities => [];
    }

    private static ISignatureContext EmptyCtx() => new NullContext();

    // ── No matches ──────────────────────────────────────────────────────────

    [Fact]
    public void Run_returns_empty_when_no_packs()
    {
        var engine = new SignatureEngine([]);
        var result = engine.Run(EmptyCtx());

        Assert.Empty(result.AllMatches);
        Assert.Null(result.TopMatch);
        Assert.Empty(result.Conflicts);
        Assert.False(result.HasConflicts);
    }

    [Fact]
    public void Run_filters_out_none_strength_matches()
    {
        var pack = new StubPack(MakeMatch("platform.x", "X", MatchStrength.None));
        var engine = new SignatureEngine([pack]);
        var result = engine.Run(EmptyCtx());

        Assert.Empty(result.AllMatches);
        Assert.Null(result.TopMatch);
    }

    // ── Single match ────────────────────────────────────────────────────────

    [Fact]
    public void Run_returns_single_match_as_top()
    {
        var pack = new StubPack(MakeMatch("platform.iphone", "iPhone", MatchStrength.Strong));
        var engine = new SignatureEngine([pack]);
        var result = engine.Run(EmptyCtx());

        Assert.Single(result.AllMatches);
        Assert.NotNull(result.TopMatch);
        Assert.Equal("platform.iphone", result.TopMatch!.ProfileId);
        Assert.Empty(result.Conflicts);
    }

    // ── Top match selection ──────────────────────────────────────────────────

    [Fact]
    public void Run_top_match_is_highest_strength()
    {
        var pack = new StubPack(
            MakeMatch("workflow.instagram", "Instagram", MatchStrength.Moderate),
            MakeMatch("platform.iphone",    "iPhone",    MatchStrength.Strong));
        var engine = new SignatureEngine([pack]);
        var result = engine.Run(EmptyCtx());

        Assert.Equal("platform.iphone", result.TopMatch!.ProfileId);
    }

    [Fact]
    public void Run_top_match_breaks_ties_by_profile_id_alphabetically()
    {
        var pack = new StubPack(
            MakeMatch("workflow.zzz", "ZZZ", MatchStrength.Strong),
            MakeMatch("platform.aaa", "AAA", MatchStrength.Strong));
        var engine = new SignatureEngine([pack]);
        var result = engine.Run(EmptyCtx());

        Assert.Equal("platform.aaa", result.TopMatch!.ProfileId);
    }

    // ── Conflict detection ──────────────────────────────────────────────────

    [Fact]
    public void Run_no_conflict_when_only_one_moderate_match_per_category()
    {
        var pack = new StubPack(
            MakeMatch("platform.iphone",    "iPhone",    MatchStrength.Strong),
            MakeMatch("workflow.lightroom", "Lightroom", MatchStrength.Strong));
        var engine = new SignatureEngine([pack]);
        var result = engine.Run(EmptyCtx());

        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public void Run_conflict_when_two_moderate_matches_same_category()
    {
        var pack = new StubPack(
            MakeMatch("platform.iphone",       "iPhone",  MatchStrength.Strong),
            MakeMatch("platform.google-pixel", "Pixel",   MatchStrength.Moderate));
        var engine = new SignatureEngine([pack]);
        var result = engine.Run(EmptyCtx());

        Assert.True(result.HasConflicts);
        Assert.Single(result.Conflicts);
        Assert.Equal("platform", result.Conflicts[0].Category);
        Assert.Equal(2, result.Conflicts[0].Matches.Count);
    }

    [Fact]
    public void Run_weak_match_does_not_trigger_conflict()
    {
        var pack = new StubPack(
            MakeMatch("platform.iphone",       "iPhone", MatchStrength.Strong),
            MakeMatch("platform.google-pixel", "Pixel",  MatchStrength.Weak));
        var engine = new SignatureEngine([pack]);
        var result = engine.Run(EmptyCtx());

        Assert.Empty(result.Conflicts);
    }

    // ── Pack fault isolation ─────────────────────────────────────────────────

    [Fact]
    public void Run_continues_when_pack_throws()
    {
        var good   = new StubPack(MakeMatch("platform.iphone", "iPhone", MatchStrength.Strong));
        var engine = new SignatureEngine([new ThrowingPack(), good]);
        var result = engine.Run(EmptyCtx());

        Assert.Single(result.AllMatches);
        Assert.Equal("platform.iphone", result.TopMatch!.ProfileId);
    }

    // ── Multiple packs ───────────────────────────────────────────────────────

    [Fact]
    public void Run_aggregates_results_from_multiple_packs()
    {
        var pack1  = new StubPack(MakeMatch("platform.iphone",    "iPhone",    MatchStrength.Strong));
        var pack2  = new StubPack(MakeMatch("workflow.lightroom", "Lightroom", MatchStrength.Strong));
        var engine = new SignatureEngine([pack1, pack2]);
        var result = engine.Run(EmptyCtx());

        Assert.Equal(2, result.AllMatches.Count);
    }
}
