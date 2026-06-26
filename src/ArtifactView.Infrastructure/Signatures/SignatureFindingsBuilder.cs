using ArtifactView.Contracts.Signatures;
using ArtifactView.Core.Models;

namespace ArtifactView.Infrastructure.Signatures;

// Converts a SignatureEngineResult into Finding objects for the Findings panel.
// One finding per distinct category (platform / workflow / etc.):
//   - no conflict: informational "consistent with" finding
//   - conflict: Medium-priority finding listing competing matches
public static class SignatureFindingsBuilder
{
    private const string Category = "Workflow Recognition";

    public static void AddFindings(SignatureEngineResult result, List<Finding> findings)
    {
        if (result.AllMatches.Count == 0) return;

        // Partition matches by category prefix.
        var byCategory = result.AllMatches
            .Where(r => r.Strength > MatchStrength.None)
            .GroupBy(r => CategoryPrefix(r.ProfileId))
            .ToList();

        foreach (var group in byCategory)
        {
            var conflictForGroup = result.Conflicts
                .FirstOrDefault(c => c.Category == group.Key);

            if (conflictForGroup is not null)
                findings.Add(BuildConflictFinding(conflictForGroup));
            else
                findings.Add(BuildMatchFinding(group.Key, group.ToList()));
        }
    }

    private static Finding BuildMatchFinding(
        string categoryKey, IReadOnlyList<SignatureMatchResult> matches)
    {
        // Use strongest match as the headline; rest are supporting.
        var top = matches
            .OrderByDescending(m => (int)m.Strength)
            .ThenBy(m => m.ProfileId)
            .First();

        var allFactors = matches
            .SelectMany(m => m.SupportingFactors)
            .Distinct()
            .ToList();

        var obs = top.Notes is not null
            ? $"Consistent with {top.ProfileName}. {top.Notes}"
            : $"Consistent with {top.ProfileName}.";

        return new Finding
        {
            Id                   = $"sig-match.{top.ProfileId}",
            Category             = Category,
            ReviewPriority       = ReviewPriority.None,
            Observation          = obs,
            ObservationConfidence = StrengthToConfidence(top.Strength),
            SupportingFactors    = allFactors,
            Provenance           = "core.sig.workflow-core"
        };
    }

    private static Finding BuildConflictFinding(SignatureConflict conflict)
    {
        var names = string.Join(" vs ", conflict.Matches
            .OrderByDescending(m => (int)m.Strength)
            .Select(m => m.ProfileName));

        var factors = conflict.Matches
            .SelectMany(m => m.SupportingFactors)
            .Distinct()
            .ToList();

        return new Finding
        {
            Id                       = $"sig-conflict.{conflict.Category}",
            Category                 = Category,
            ReviewPriority           = ReviewPriority.Medium,
            Observation              = $"Conflicting {conflict.Category} signatures: {names}.",
            ObservationConfidence    = new ConfidenceScore(90),
            Interpretation           = "Multiple competing signatures match. " +
                                       "Possible re-export, cross-app processing, " +
                                       "or metadata modification.",
            InterpretationConfidence = new ConfidenceScore(55),
            SupportingFactors        = factors,
            ConflictingFactors       = conflict.Matches
                .Select(m => $"{m.ProfileName} ({m.Strength})")
                .ToList(),
            Provenance               = "core.sig.workflow-core"
        };
    }

    private static ConfidenceScore StrengthToConfidence(MatchStrength strength) => strength switch
    {
        MatchStrength.Strong   => new ConfidenceScore(85),
        MatchStrength.Moderate => new ConfidenceScore(65),
        MatchStrength.Weak     => new ConfidenceScore(40),
        _                      => ConfidenceScore.Unknown
    };

    private static string CategoryPrefix(string profileId)
    {
        var dot = profileId.IndexOf('.');
        return dot > 0 ? profileId[..dot] : profileId;
    }
}
