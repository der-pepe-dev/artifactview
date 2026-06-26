using System.Diagnostics;
using System.Runtime.Loader;
using ArtifactView.Plugins.Abstractions;

namespace ArtifactView.Infrastructure.Plugins;

// Loads the assembly declared in a permitted PluginManifest and instantiates
// the entry type.  Uses AssemblyLoadContext.Default so types in the loaded
// assembly are compatible with interface types already loaded by the host.
// Returns null — never throws — on any failure.
public sealed class PluginActivator
{
    public T? TryActivate<T>(PluginManifest manifest) where T : class
    {
        if (string.IsNullOrEmpty(manifest.AssemblyName) || string.IsNullOrEmpty(manifest.EntryTypeName))
        {
            Debug.WriteLine($"[PluginActivator] '{manifest.Id}': AssemblyName/EntryTypeName not set");
            return null;
        }

        if (string.IsNullOrEmpty(manifest.ManifestDirectory))
        {
            Debug.WriteLine($"[PluginActivator] '{manifest.Id}': ManifestDirectory not set");
            return null;
        }

        var assemblyPath = Path.GetFullPath(Path.Combine(manifest.ManifestDirectory, manifest.AssemblyName));
        if (!File.Exists(assemblyPath))
        {
            Debug.WriteLine($"[PluginActivator] '{manifest.Id}': assembly not found at {assemblyPath}");
            return null;
        }

        try
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            var type = assembly.GetType(manifest.EntryTypeName);
            if (type is null)
            {
                Debug.WriteLine($"[PluginActivator] '{manifest.Id}': type '{manifest.EntryTypeName}' not found");
                return null;
            }
            if (!typeof(T).IsAssignableFrom(type))
            {
                Debug.WriteLine($"[PluginActivator] '{manifest.Id}': '{manifest.EntryTypeName}' does not implement {typeof(T).Name}");
                return null;
            }
            return (T?)Activator.CreateInstance(type);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginActivator] '{manifest.Id}': activation failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
