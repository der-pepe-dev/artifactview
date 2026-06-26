using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ArtifactView.App.ViewModels;
using ArtifactView.App.Views;
using ArtifactView.Core.Models;

namespace ArtifactView.App;

public partial class MainWindow : Window
{
    private readonly global::ArtifactView.Application.Settings.AppSettingsStore _settingsStore;

    public MainWindow(ShellViewModel viewModel,
                      global::ArtifactView.Application.Settings.AppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        InitializeComponent();
        DataContext = viewModel;

        RestoreWindowState();

        // Restore keyboard focus to the grid after each folder load so the user
        // can immediately use cursor keys and Enter to navigate without clicking.
        viewModel.FolderLoaded += (_, _) => FocusGrid();

        viewModel.CompareRequested += (_, args) =>
        {
            var win = new CompareWindow(args.Left, args.Right);
            win.Owner = this;
            win.Show();
        };
    }

    // ── Window state persistence ─────────────────────────────────────────────

    private void RestoreWindowState()
    {
        var s = _settingsStore.Load();
        if (s.WindowWidth is > 200 && s.WindowHeight is > 200)
        {
            Left   = s.WindowLeft ?? Left;
            Top    = s.WindowTop  ?? Top;
            Width  = s.WindowWidth.Value;
            Height = s.WindowHeight.Value;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        if (s.WindowMaximized)
            WindowState = WindowState.Maximized;

        foreach (var col in MediaGrid.Columns)
        {
            if (col.Header is string header && !string.IsNullOrEmpty(header)
                && s.ColumnVisibility.TryGetValue(header, out var visible))
                col.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        var s = _settingsStore.Load();
        s.WindowLeft      = RestoreBounds.Left;
        s.WindowTop       = RestoreBounds.Top;
        s.WindowWidth     = RestoreBounds.Width;
        s.WindowHeight    = RestoreBounds.Height;
        s.WindowMaximized = WindowState == WindowState.Maximized;

        foreach (var col in MediaGrid.Columns)
        {
            if (col.Header is string header && !string.IsNullOrEmpty(header))
                s.ColumnVisibility[header] = col.Visibility == Visibility.Visible;
        }

        _settingsStore.Save(s);
    }

    // Drag-and-drop: accept folders or image files from Explorer.
    // Folders open directly; files open the containing folder and auto-select the file.
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (paths is not { Length: > 0 }) return;

        var target = paths[0];
        if (Directory.Exists(target))
        {
            _ = vm.LoadFolderAsync(target);
        }
        else if (File.Exists(target))
        {
            var folder = Path.GetDirectoryName(target);
            if (folder is null) return;

            _ = vm.LoadFolderAsync(folder).ContinueWith(_ =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var match = vm.Items.FirstOrDefault(
                        r => string.Equals(r.LogicalPath, target, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                        vm.SelectedItem = match;
                });
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        e.Handled = true;
    }

    // Moves keyboard focus into the grid's cell layer so cursor keys, Enter,
    // Home/End, and other navigation keys work immediately.
    private void FocusGrid()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (MediaGrid.Items.Count == 0) return;

            if (MediaGrid.SelectedItem is null)
                MediaGrid.SelectedIndex = 0;

            var item = MediaGrid.SelectedItem;
            if (item is null || MediaGrid.Columns.Count == 0)
            {
                MediaGrid.Focus();
                return;
            }

            MediaGrid.ScrollIntoView(item);
            MediaGrid.UpdateLayout();

            // Walk the visual tree to find the actual DataGridCell for column 0
            // and focus it directly.  Focus() on the DataGrid container alone
            // leaves focus on the column headers; focusing a realised cell puts
            // it into the data-row layer where cursor keys work immediately.
            var row = MediaGrid.ItemContainerGenerator.ContainerFromItem(item)
                      as DataGridRow;
            if (row is not null)
            {
                var presenter = FindVisualChild<DataGridCellsPresenter>(row);
                var cell = presenter?.ItemContainerGenerator
                               .ContainerFromIndex(0) as DataGridCell;
                if (cell is not null)
                {
                    cell.Focus();
                    return;
                }
            }

            // Fallback: set CurrentCell and focus the grid container.
            MediaGrid.CurrentCell = new DataGridCellInfo(item, MediaGrid.Columns[0]);
            MediaGrid.Focus();
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;
            var result = FindVisualChild<T>(child);
            if (result is not null)
                return result;
        }
        return null;
    }

    private void MediaGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only fire when the click hit an actual DataGridRow (not a header or empty space).
        var hit = e.OriginalSource as System.Windows.DependencyObject;
        while (hit is not null && hit is not System.Windows.Controls.DataGridRow)
            hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);

        if (hit is not System.Windows.Controls.DataGridRow || DataContext is not ShellViewModel vm)
            return;

        if (vm.SelectedItem is { IsDirectory: true } dirRow)
        {
            vm.NavigateCommand.Execute(dirRow);
            e.Handled = true;
        }
        else if (vm.SelectedItem is { IsDirectory: false } mediaRow &&
                 File.Exists(mediaRow.LogicalPath))
        {
            OpenFullscreen(vm, mediaRow.LogicalPath);
            e.Handled = true;
        }
    }

    // PreviewKeyDown tunnels from the root *down* to the focused element, so we see
    // Enter before the DataGrid's own handler fires (which would advance selection).
    // Only intercept when the grid actually has keyboard focus.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key != Key.Return || DataContext is not ShellViewModel vm)
            return;

        if (!MediaGrid.IsKeyboardFocusWithin)
            return;

        if (vm.SelectedItem is { IsDirectory: true } dirRow)
        {
            vm.NavigateCommand.Execute(dirRow);
            e.Handled = true;
        }
        else if (vm.SelectedItem is { IsDirectory: false } mediaRow &&
                 File.Exists(mediaRow.LogicalPath))
        {
            OpenFullscreen(vm, mediaRow.LogicalPath);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Backspace: navigate to the parent folder.
        // Skipped when the search box has keyboard focus so text editing is unaffected.
        if (e.Key == Key.Back &&
            !SearchBox.IsKeyboardFocused &&
            DataContext is ShellViewModel vm &&
            vm.NavigateUpCommand.CanExecute(null))
        {
            vm.NavigateUpCommand.Execute(null);
            e.Handled = true;
        }

        // Home / End: jump to first or last item in the grid.
        // WPF DataGrid maps Home/End to first/last *column* of the current row;
        // for a file browser the expected behaviour is first/last *row*.
        if ((e.Key == Key.Home || e.Key == Key.End) &&
            MediaGrid.IsKeyboardFocusWithin &&
            MediaGrid.Items.Count > 0)
        {
            var targetIndex = e.Key == Key.Home ? 0 : MediaGrid.Items.Count - 1;
            MediaGrid.SelectedIndex = targetIndex;
            MediaGrid.ScrollIntoView(MediaGrid.Items[targetIndex]);
            if (MediaGrid.Columns.Count > 0)
                MediaGrid.CurrentCell = new DataGridCellInfo(
                    MediaGrid.Items[targetIndex]!, MediaGrid.Columns[0]);
            e.Handled = true;
        }

        // Ctrl+F: jump to the search box.
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }

        // Escape: clear search and return focus to the grid.
        if (e.Key == Key.Escape && SearchBox.IsKeyboardFocused)
        {
            if (DataContext is ShellViewModel svm)
                svm.ClearSearchCommand.Execute(null);
            FocusGrid();
            e.Handled = true;
        }
    }

    private void OpenFullscreen(ShellViewModel vm, string targetPath)
    {
        // Include both live files and ghost entries — ghost rows are
        // navigable in fullscreen using their cached Thumbs.db thumbnail.
        var rows = vm.Items
            .Where(r => !r.IsDirectory)
            .ToList();

        if (rows.Count == 0)
            return;

        var index = rows.FindIndex(r => string.Equals(
            r.LogicalPath, targetPath, StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = 0;

        // Reuse the already-decoded BitmapSource from the viewer to avoid re-loading
        // the first image — the fullscreen window is visible instantly.
        var viewer = new FullscreenViewerWindow(rows, index, vm.Viewer.Source);
        viewer.Owner = this;
        viewer.ShowDialog();

        // Sync the main grid selection to whatever the user navigated to
        // in fullscreen, so the detail tabs show the right item.
        if (viewer.CurrentIndex >= 0 && viewer.CurrentIndex < rows.Count)
        {
            var navigatedRow = rows[viewer.CurrentIndex];
            if (vm.SelectedItem != navigatedRow)
                vm.SelectedItem = navigatedRow;
        }

        // Restore keyboard focus to the grid so cursor keys work immediately.
        FocusGrid();
    }

    // Intercepts column header clicks so SortOrder (group key) stays as primary sort.
    // This keeps ".." first, then directories, then media files under any column sort.
    // Cycles: unsorted → Ascending → Descending → back to unsorted (group-only order).
    private void MediaGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;

        var newDir = e.Column.SortDirection switch
        {
            null                         => (ListSortDirection?)ListSortDirection.Ascending,
            ListSortDirection.Ascending  => ListSortDirection.Descending,
            ListSortDirection.Descending => null,
            _                            => ListSortDirection.Ascending
        };

        e.Column.SortDirection = newDir;
        foreach (var col in MediaGrid.Columns.Where(c => c != e.Column))
            col.SortDirection = null;

        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(MediaGrid.ItemsSource);
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new System.ComponentModel.SortDescription(
            nameof(ArtifactView.Core.Models.MediaEntityRow.SortOrder),
            System.ComponentModel.ListSortDirection.Ascending));

        if (newDir.HasValue && e.Column.SortMemberPath is { Length: > 0 } path)
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(path, newDir.Value));
    }

    // Scrolls the filmstrip to keep the selected item visible whenever selection changes.
    private void FilmstripListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is not null)
            lb.ScrollIntoView(lb.SelectedItem);
    }

    // Populates the column-header context menu with a checkable item per column
    // so the user can show/hide columns.  Rebuilt each time the menu opens to
    // reflect current visibility state.
    private void ColumnHeaderMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.Items.Clear();

        foreach (var col in MediaGrid.Columns)
        {
            // Skip the icon column (no meaningful header text).
            if (col.Header is not string header || string.IsNullOrEmpty(header))
                continue;

            var captured = col;
            var item = new MenuItem
            {
                Header     = header,
                IsCheckable = true,
                IsChecked  = col.Visibility == Visibility.Visible
            };
            item.Checked   += (_, _) => captured.Visibility = Visibility.Visible;
            item.Unchecked += (_, _) => captured.Visibility = Visibility.Collapsed;
            menu.Items.Add(item);
        }
    }
}


