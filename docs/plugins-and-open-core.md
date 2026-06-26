# Plugins and open core

Extensibility and product-tier rules. Read when touching plugin discovery/loading,
trust policy, or decisions about what belongs in open core vs premium.

## Plugin system

Plugins must be discoverable without being automatically trusted. At startup: discover
manifests first, apply trust policy, then load only approved plugins. Do not execute
plugin code just to inspect it.

Users must be able to choose: core only, open-source plugins only, signed plugins
only, or full mode. Closed/proprietary plugins must remain optional.

Plugin categories: source plugins, format handlers, analyzers,
processors/reconstruction plugins, exporters, signature/rule packs.

## Open core vs premium

The open core must remain genuinely useful — never an empty shell for upsells.

Open core includes: fast viewer, metadata browsing, basic findings, ghost overlay
basics, exact artifact extraction, basic reconstruction, basic integrity checks, and
the plugin SDK.

Premium/closed extensions may include: advanced source parsers, mobile backup support,
app DB correlation, advanced recognition/signature packs, enterprise
workflows/reporting, advanced reconstruction/recovery modules.
