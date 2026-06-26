using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArtifactView.Application.Plugins;
using ArtifactView.Application.Settings;
using ArtifactView.Infrastructure.Plugins;
using ArtifactView.Plugins.Abstractions;

namespace ArtifactView.App.ViewModels;

// Read-only view of discovered plugin manifests filtered by the current loading policy.
// Does not execute any plugin code — manifest inspection only.
// When a PluginRegistry is provided (wired from App startup), Refresh() re-loads
// the registry with the current policy so the display stays in sync with runtime state.
public sealed class PluginsViewModel : INotifyPropertyChanged
{
    private readonly PluginLoader _loader = new();
    private readonly PluginRegistry? _registry;

    public PluginsViewModel(PluginRegistry? registry = null)
    {
        _registry = registry;
    }

    public ObservableCollection<PluginManifest> Manifests { get; } = [];

    public bool HasPlugins => Manifests.Count > 0;

    public string Summary { get; private set; } = "No plugins folder configured.";

    public void Refresh(string? pluginsFolder, PluginPolicy policy = PluginPolicy.CoreOnly)
    {
        Manifests.Clear();

        if (string.IsNullOrEmpty(pluginsFolder) || !System.IO.Directory.Exists(pluginsFolder))
        {
            Summary = pluginsFolder is null
                ? "No plugins folder configured."
                : $"Plugins folder not found: {pluginsFolder}";
            OnPropertyChanged(nameof(HasPlugins));
            OnPropertyChanged(nameof(Summary));
            return;
        }

        // If a shared registry is available, reload it with the current policy so
        // runtime permission checks stay in sync with what the dialog displays.
        if (_registry is not null)
            _registry.Load(pluginsFolder, policy);

        var discovered = _loader.Discover(pluginsFolder);
        foreach (var m in discovered.Where(m => IsAllowedByPolicy(m, policy)))
            Manifests.Add(m);

        var blocked = discovered.Count - Manifests.Count;
        var blockedNote = blocked > 0 ? $" ({blocked} blocked by policy)" : string.Empty;
        Summary = Manifests.Count > 0
            ? $"{Manifests.Count} plugin(s) loaded from {pluginsFolder}{blockedNote}"
            : $"No plugins loaded from {pluginsFolder}{blockedNote}";

        OnPropertyChanged(nameof(HasPlugins));
        OnPropertyChanged(nameof(Summary));
    }

    // CoreOnly treats all folder-discovered plugins as third-party (core plugins are compiled in).
    private static bool IsAllowedByPolicy(PluginManifest manifest, PluginPolicy policy) =>
        policy switch
        {
            PluginPolicy.CoreOnly         => false,
            PluginPolicy.CoreAndOpenSource => manifest.IsOpenSource,
            PluginPolicy.CoreAndSigned    => manifest.SignatureInfo is not null,
            PluginPolicy.Full             => true,
            _                             => false
        };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
