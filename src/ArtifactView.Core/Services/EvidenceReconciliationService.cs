using ArtifactView.Core.Models;

namespace ArtifactView.Core.Services;

public sealed class EvidenceReconciliationService
{
    public ReconciledFieldValue Reconcile(string fieldName, IReadOnlyList<FieldCandidate> candidates)
    {
        var preferred = candidates
            .OrderByDescending(x => x.Confidence.Value)
            .ThenBy(x => x.SourceType)
            .FirstOrDefault();

        var distinctValues = candidates
            .Where(x => x.RawValue is not null)
            .Select(x => x.NormalizedValue ?? x.RawValue)
            .Distinct()
            .Count();

        var status = candidates.Count switch
        {
            0 => MergeStatus.Ambiguous,
            1 => MergeStatus.Resolved,
            _ => distinctValues > 1 ? MergeStatus.Conflicted : MergeStatus.Merged
        };

        return new ReconciledFieldValue
        {
            FieldName      = fieldName,
            PreferredValue = preferred?.NormalizedValue ?? preferred?.RawValue,
            SourceUsed     = preferred?.SourceType,
            Status         = status,
            Confidence     = preferred?.Confidence ?? ConfidenceScore.Unknown,
            Candidates     = candidates
        };
    }
}
