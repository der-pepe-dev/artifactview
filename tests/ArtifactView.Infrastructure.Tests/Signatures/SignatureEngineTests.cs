using ArtifactView.Contracts.Signatures;
using ArtifactView.Infrastructure.Signatures;
using System.Threading.Tasks;

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

    [Test]
    public async Task Run_returns_empty_when_no_packs()
    {
        var engine = new SignatureEngine([]);
        var result = engine.Run(EmptyCtx());

        await Assert.That(result.AllMatches).IsEmpty();
        Assert.Null(result.TopMatch);
        await Assert.That(result.Conflicts).IsEmpty();
        await Assert.That(result.HasConflicts).IsFalse();
    }

    [Test]
    public async Task Run_filters_out_none_strength_matches()
    {
        var pack = new StubPack(MakeMatch("platform.x", "X", MatchStrength.None));
        var engine = new SignatureEngine([pack]);
        var result = engine.Run(EmptyCtx());

        await Assert.That(result.AllMatches).IsEmpty();
        Assert.Null(result.TopMatch);
    }

    // ── Single match ────────────────────────────────────────────────────────

    [Test]
    public async Task Run_returns_single_match_as_top()
    {
        var pack = new StubPack(MakeMatch("platform.iphone", "iPhone", MatchStrength.Strong));
        var engine = new SignatureEngine([pack]);
        var result = engine.Run(EmptyCtx());

        await Assert.That(result.AllMatches).HasSingleItem();
        Assert.NotNull(result.TopMatch);
        await Assert.That(result.TopMatch!.ProfileId).IsEqualTo("platform.iphone");
        await Assert.That(result.Conflicts).IsEmpty();
    }

    // ── Top match selection ──────────────────────────────────────────────────

    [Test]
    public async Task Run_top_match_is_highest_strength()
    {
        var pack = new StubPack(
            MakeMatch("workflow.instagram", "Instagram", MatchStrength.Moderate),
            MakeMatch("platform.iphone",    "iPhone",    MatchStrength.Strong));
        var engine = new SignatureEngine([pack]);
        var result = engine.Run(EmptyCtx());

        await Assert.That(result.TopMatch!.ProfileId).IsEqualTo("platform.iphone");
    }

    [Test]
    public async Task Run_top_match_breaks_ties_by_profile_id_alphabetically()
    {
        var pack = new StubPack(
            MakeMatch("workflow.zzz", "ZZZ", MatchStrength.Strong),
            MakeMatch("platform.aaa", "AAA", MatchStrength.Strong));
        var engine = new SignatureEngine([pack]);
        var result = engine.Run(EmptyCtx());

        await Assert.That(result.TopMatch!.ProfileId).IsEqualTo("platform.aaa");
    }

    // ── Conflict detection ──────────────────────────────────────────────────

    [Test]
    public async Task Run_no_conflict_when_only_one_moderate_match_per_category()
    {
        var pack = new StubPack(
            MakeMatch("platform.iphone",    "iPhone",    MatchStrength.Strong),
            MakeMatch("workflow.lightroom", "Lightroom", MatchStrength.Strong));
        var engine = new SignatureEngine([pack]);
        var result = engine.Run(EmptyCtx());

        await Assert.That(result.Conflicts).IsEmpty();
    }

    [Test]
    public async Task Run_conflict_when_two_moderate_matches_same_category()
    {
        var pack = new StubPack(
            MakeMatch("platform.iphone",       "iPhone",  MatchStrength.Strong),
            MakeMatch("platform.google-pixel", "Pixel",   MatchStrength.Moderate));
        var engine = new SignatureEngine([pack]);
        var result = engine.Run(EmptyCtx());

        await Assert.That(result.HasConflicts).IsTrue();
        await Assert.That(result.Conflicts).HasSingleItem();
        await Assert.That(result.Conflicts[0].Category).IsEqualTo("platform");
        await Assert.That(result.Conflicts[0].Matches.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Run_weak_match_does_not_trigger_conflict()
    {
        var pack = new StubPack(
            MakeMatch("platform.iphone",       "iPhone", MatchStrength.Strong),
            MakeMatch("platform.google-pixel", "Pixel",  MatchStrength.Weak));
        var engine = new SignatureEngine([pack]);
        var result = engine.Run(EmptyCtx());

        await Assert.That(result.Conflicts).IsEmpty();
    }

    // ── Pack fault isolation ─────────────────────────────────────────────────

    [Test]
    public async Task Run_continues_when_pack_throws()
    {
        var good   = new StubPack(MakeMatch("platform.iphone", "iPhone", MatchStrength.Strong));
        var engine = new SignatureEngine([new ThrowingPack(), good]);
        var result = engine.Run(EmptyCtx());

        await Assert.That(result.AllMatches).HasSingleItem();
        await Assert.That(result.TopMatch!.ProfileId).IsEqualTo("platform.iphone");
    }

    // ── Multiple packs ───────────────────────────────────────────────────────

    [Test]
    public async Task Run_aggregates_results_from_multiple_packs()
    {
        var pack1  = new StubPack(MakeMatch("platform.iphone",    "iPhone",    MatchStrength.Strong));
        var pack2  = new StubPack(MakeMatch("workflow.lightroom", "Lightroom", MatchStrength.Strong));
        var engine = new SignatureEngine([pack1, pack2]);
        var result = engine.Run(EmptyCtx());

        await Assert.That(result.AllMatches.Count).IsEqualTo(2);
    }
}