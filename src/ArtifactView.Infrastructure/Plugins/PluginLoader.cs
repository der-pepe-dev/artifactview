using ArtifactView.Plugins.Abstractions;

namespace ArtifactView.Infrastructure.Plugins;

public sealed class PluginLoader
{
    public IReadOnlyList<PluginManifest> Discover(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return [];

        var results = new List<PluginManifest>();
        foreach (var file in Directory.EnumerateFiles(folderPath, "plugin.json", SearchOption.AllDirectories))
        {
            try
            {
                var manifest = PluginManifestReader.Read(file);
                manifest.ManifestDirectory = Path.GetDirectoryName(file);
                results.Add(manifest);
            }
            catch (Exception ex)
            {
                // One bad manifest must not crash discovery for all others.
                System.Diagnostics.Debug.WriteLine(
                    $"[PluginLoader] Skipping bad manifest '{file}': {ex.GetType().Name}: {ex.Message}");
            }
        }
        return results;
    }
}
