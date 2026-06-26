namespace ArtifactView.Application.Settings;

// Controls which plugin categories are permitted to load.
// Matches the trust levels defined in the plugin architecture rules.
public enum PluginPolicy
{
    CoreOnly = 0,
    CoreAndOpenSource = 1,
    CoreAndSigned = 2,
    Full = 3
}
