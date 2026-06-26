namespace ArtifactView.Application.Settings;

public sealed class AppSettings
{
    public string? LastFolderPath { get; set; }
    public PluginPolicy PluginPolicy { get; set; } = PluginPolicy.CoreOnly;
    public string CacheDirectory { get; set; } = DefaultCacheDirectory();
    public string PluginsDirectory { get; set; } = DefaultPluginsDirectory();

    // Window state persistence — restored on startup, saved on close.
    public double? WindowLeft   { get; set; }
    public double? WindowTop    { get; set; }
    public double? WindowWidth  { get; set; }
    public double? WindowHeight { get; set; }
    public bool    WindowMaximized { get; set; }

    // Per-column visibility keyed by header string. Missing keys default to visible.
    public Dictionary<string, bool> ColumnVisibility { get; set; } = new();

    private static string DefaultCacheDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ArtifactView",
            "cache");

    private static string DefaultPluginsDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "plugins");
}
