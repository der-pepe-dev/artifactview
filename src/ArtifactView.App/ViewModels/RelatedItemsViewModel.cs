using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ArtifactView.App.Commands;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.App.ViewModels;

public sealed class RelatedItemRowViewModel
{
    public required string         DisplayName        { get; init; }
    public required string         RelationshipType   { get; init; }
    public required string         LogicalPath        { get; init; }
    public required string         PreferredDateText  { get; init; }
    public required string         FindingsText       { get; init; }
    public required MediaEntityRow Row                { get; init; }
}

public sealed class RelatedItemsViewModel : INotifyPropertyChanged
{
    public ObservableCollection<RelatedItemRowViewModel> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    public ICommand NavigateToItemCommand { get; }

    // Set by ShellViewModel so clicking an item changes the selection.
    public Action<MediaEntityRow>? NavigateTo { get; set; }

    public RelatedItemsViewModel()
    {
        NavigateToItemCommand = new RelayCommand(
            param => { if (param is RelatedItemRowViewModel vm) NavigateTo?.Invoke(vm.Row); },
            param => param is RelatedItemRowViewModel);
    }

    public void Load(MediaEntityRow? selected, IEnumerable<MediaEntityRow> allRows)
    {
        Items.Clear();

        if (selected is null || selected.IsDirectory)
        {
            OnPropertyChanged(nameof(HasItems));
            return;
        }

        var others = allRows
            .Where(r => !r.IsDirectory && !ReferenceEquals(r, selected))
            .ToList();

        var selectedHash = new PerceptualHash(selected.PerceptualHashValue);

        foreach (var other in others)
        {
            string? rel = null;

            if (!string.IsNullOrEmpty(selected.Sha256Hash)
                && string.Equals(selected.Sha256Hash, other.Sha256Hash, StringComparison.OrdinalIgnoreCase))
            {
                rel = "Exact Duplicate";
            }
            else if (selected.PerceptualHashValue != 0 && other.PerceptualHashValue != 0)
            {
                var dist = PerceptualHash.HammingDistance(
                    selectedHash, new PerceptualHash(other.PerceptualHashValue));
                if (dist <= PerceptualHash.NearDuplicateThreshold)
                    rel = $"Near Duplicate (dHash Δ{dist})";
            }

            if (rel is null && selected.BurstId > 0 && other.BurstId == selected.BurstId)
                rel = "Burst Sibling";

            if (rel is null) continue;

            Items.Add(new RelatedItemRowViewModel
            {
                DisplayName      = other.DisplayName,
                RelationshipType = rel,
                LogicalPath      = other.LogicalPath ?? string.Empty,
                PreferredDateText = other.PreferredDateText,
                FindingsText     = other.FindingsText,
                Row              = other
            });
        }

        // Sort: exact dups first, near-dups second, burst siblings last.
        var sorted = Items
            .OrderBy(r => r.RelationshipType.StartsWith("Exact") ? 0
                        : r.RelationshipType.StartsWith("Near")  ? 1
                        : 2)
            .ThenBy(r => r.DisplayName)
            .ToList();

        Items.Clear();
        foreach (var item in sorted)
            Items.Add(item);

        OnPropertyChanged(nameof(HasItems));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
