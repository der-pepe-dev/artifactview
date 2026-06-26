using System.Text.Json.Serialization;

namespace ArtifactView.Plugins.Abstractions;

public sealed class PluginManifest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Author { get; init; }
    public required string License { get; init; }
    public bool IsOpenSource { get; init; }
    public string? Homepage { get; init; }
    public string[] Capabilities { get; init; } = [];
    public bool RequiresNativeCode { get; init; }
    public string? SignatureInfo { get; init; }
    public PluginCategory Category { get; init; } = PluginCategory.Unknown;

    // Entry point for assembly-based plugins. Both must be set to enable activation.
    public string? AssemblyName { get; init; }
    public string? EntryTypeName { get; init; }

    // Set by PluginLoader after discovery; not present in plugin.json.
    [JsonIgnore]
    public string? ManifestDirectory { get; set; }
}
