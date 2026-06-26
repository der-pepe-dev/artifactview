namespace ArtifactView.Application.Jobs;

// Follows the priority ordering defined in copilot-instructions:
// selected item first, then visible grid, then background maintenance.
public enum JobPriority
{
    SelectedItemRender = 0,
    SelectedItemMetadata = 1,
    SelectedItemQuickFindings = 2,
    VisibleGridRows = 3,
    NearbyFilmstrip = 4,
    LocalFolderCorrelation = 5,
    MaintenanceAndGlobalIndexing = 6
}
