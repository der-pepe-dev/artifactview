using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArtifactView.Core.Models;

namespace ArtifactView.Application.ViewModels;

// UI wrapper around a Finding that adds expand/collapse state.
// Forwards all Finding properties so XAML binds directly to this type.
public sealed class FindingRowViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;

    public FindingRowViewModel(Finding finding)
    {
        Finding = finding;
    }

    public Finding Finding { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    // ── Forwarded display properties ────────────────────────────────────────

    public string         Category                 => Finding.Category;
    public string         Observation              => Finding.Observation;
    public string?        Interpretation           => Finding.Interpretation;
    public ReviewPriority ReviewPriority           => Finding.ReviewPriority;
    public ConfidenceScore ObservationConfidence   => Finding.ObservationConfidence;
    public ConfidenceScore InterpretationConfidence => Finding.InterpretationConfidence;
    public IReadOnlyList<string> SupportingFactors => Finding.SupportingFactors;
    public IReadOnlyList<string> ConflictingFactors => Finding.ConflictingFactors;
    public string?        Provenance               => Finding.Provenance;

    // ── Detail-section visibility helpers ───────────────────────────────────

    // True when the expanded section has anything to show beyond the collapsed view.
    public bool HasDetails =>
        Finding.InterpretationConfidence.Value >= 0 ||
        Finding.SupportingFactors.Count  > 0 ||
        Finding.ConflictingFactors.Count > 0 ||
        Finding.Provenance is not null;

    public bool HasInterpretationConfidence => Finding.InterpretationConfidence.Value >= 0;
    public bool HasSupportingFactors        => Finding.SupportingFactors.Count > 0;
    public bool HasConflictingFactors       => Finding.ConflictingFactors.Count > 0;
    public bool HasProvenance               => Finding.Provenance is not null;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
