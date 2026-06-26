namespace ArtifactView.Infrastructure.Sources.AppDb;

// Runs all registered IAppDbReaders against a folder and aggregates results.
public sealed class AppDbCorrelator
{
    private readonly IReadOnlyList<IAppDbReader> _readers;

    public AppDbCorrelator(IEnumerable<IAppDbReader>? readers = null)
    {
        _readers = readers?.ToList() ?? BuildDefaults();
    }

    public IReadOnlyList<AppDbCorrelationEntry> Correlate(
        string folderPath,
        IReadOnlyCollection<string> mediaFilenames)
    {
        var results = new List<AppDbCorrelationEntry>();
        foreach (var reader in _readers)
        {
            try
            {
                if (!reader.Detect(folderPath)) continue;
                results.AddRange(reader.Correlate(folderPath, mediaFilenames));
            }
            catch { }
        }
        return results;
    }

    private static List<IAppDbReader> BuildDefaults() =>
    [
        new WhatsAppDbReader(),
        new TelegramDbReader(),
        new SignalDbReader()
    ];
}
