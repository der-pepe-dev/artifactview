using ArtifactView.Application.Settings;
using ArtifactView.Infrastructure.Plugins;
using ArtifactView.Plugins.Abstractions;

namespace ArtifactView.Application.Plugins;

// Discovers plugin manifests, applies the configured PluginPolicy filter,
// and maintains the set of permitted plugins for the current session.
// Plugin *execution* is deferred to a future processor pipeline; this registry
// only validates manifests and enforces policy.
public sealed class PluginRegistry
{
    private readonly PluginLoader _loader;
    private readonly PluginActivator _activator;
    private readonly List<PluginManifest> _permitted = [];

    public PluginRegistry(PluginLoader loader, PluginActivator? activator = null)
    {
        _loader    = loader;
        _activator = activator ?? new PluginActivator();
    }

    public IReadOnlyList<PluginManifest> Permitted => _permitted;

    public void Load(string pluginsFolder, PluginPolicy policy)
    {
        _permitted.Clear();

        var discovered = _loader.Discover(pluginsFolder);
        foreach (var manifest in discovered)
        {
            if (IsPermitted(manifest, policy))
                _permitted.Add(manifest);
            else
                System.Diagnostics.Debug.WriteLine(
                    $"[PluginRegistry] Plugin '{manifest.Id}' blocked by policy {policy}.");
        }
    }

    public bool IsRegistered(string pluginId) =>
        _permitted.Exists(m => m.Id == pluginId);

    // Instantiates the entry type of a permitted plugin as T.
    // Returns null if the plugin is not permitted, has no assembly info, or activation fails.
    public T? TryActivate<T>(string pluginId) where T : class
    {
        var manifest = _permitted.Find(m => m.Id == pluginId);
        if (manifest is null) return null;
        return _activator.TryActivate<T>(manifest);
    }

    private static bool IsPermitted(PluginManifest manifest, PluginPolicy policy) =>
        policy switch
        {
            PluginPolicy.CoreOnly          => false,
            PluginPolicy.CoreAndOpenSource => manifest.IsOpenSource,
            PluginPolicy.CoreAndSigned     => manifest.SignatureInfo is not null,
            PluginPolicy.Full              => true,
            _                              => false
        };
}
