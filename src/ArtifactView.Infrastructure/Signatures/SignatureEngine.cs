using ArtifactView.Contracts.Signatures;

namespace ArtifactView.Infrastructure.Signatures;

// Runs all registered ISignatureRulePack instances and collects results.
// Conflict detection: when 2+ Moderate+ matches share the same category
// prefix (first segment of ProfileId, e.g. "platform" or "workflow"),
// a conflict is recorded.
public sealed class SignatureEngine
{
    private readonly IReadOnlyList<ISignatureRulePack> _packs;

    public SignatureEngine(IReadOnlyList<ISignatureRulePack> packs)
    {
        _packs = packs;
    }

    public SignatureEngineResult Run(ISignatureContext context)
    {
        var all = new List<SignatureMatchResult>();

        foreach (var pack in _packs)
        {
            try { all.AddRange(pack.Match(context)); }
            catch { /* pack failure must not break others */ }
        }

        // Keep only results with actual strength.
        var matches = all.Where(r => r.Strength > MatchStrength.None).ToList();

        // Detect conflicts: 2+ Moderate+ matches in same category prefix.
        var conflicts = matches
            .Where(r => r.Strength >= MatchStrength.Moderate)
            .GroupBy(r => CategoryPrefix(r.ProfileId))
            .Where(g => g.Count() > 1)
            .Select(g => new SignatureConflict(g.Key, g.ToList()))
            .ToList();

        // Top match: highest strength, then stable order.
        var top = matches
            .OrderByDescending(r => (int)r.Strength)
            .ThenBy(r => r.ProfileId)
            .FirstOrDefault();

        return new SignatureEngineResult(matches, top, conflicts);
    }

    private static string CategoryPrefix(string profileId)
    {
        var dot = profileId.IndexOf('.');
        return dot > 0 ? profileId[..dot] : profileId;
    }
}

public sealed record SignatureEngineResult(
    IReadOnlyList<SignatureMatchResult> AllMatches,
    SignatureMatchResult?               TopMatch,
    IReadOnlyList<SignatureConflict>    Conflicts)
{
    public bool HasConflicts => Conflicts.Count > 0;
}

public sealed record SignatureConflict(
    string                             Category,
    IReadOnlyList<SignatureMatchResult> Matches);
