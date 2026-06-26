using System.Text.Json;

namespace ArtifactView.Plugins.Abstractions;

public static class PluginManifestReader
{
    // Case-insensitive so plugin authors can use either camelCase or PascalCase keys.
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static PluginManifest Read(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PluginManifest>(json, s_options)
            ?? throw new InvalidOperationException($"Unable to read plugin manifest: {path}");
    }
}
