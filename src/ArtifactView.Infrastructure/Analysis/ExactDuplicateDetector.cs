namespace ArtifactView.Infrastructure.Analysis;

// Finds files with identical SHA-256 hashes within a given set.
// Input: (path, hash) pairs — hash is the full lowercase hex string.
// Output: groups where every group contains 2+ paths with the same hash.
//
// Designed for session-level scan (one folder at a time).  Does not persist
// state or cross folder boundaries.
public static class ExactDuplicateDetector
{
    /// <param name="fileHashes">
    /// Sequence of (absolute-path, sha256-hex) pairs.  Entries with an empty
    /// or null hash are skipped.
    /// </param>
    public static IReadOnlyList<DuplicateGroup> Detect(
        IEnumerable<(string Path, string Hash)> fileHashes)
    {
        var byHash = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, hash) in fileHashes)
        {
            if (string.IsNullOrEmpty(hash)) continue;
            if (!byHash.TryGetValue(hash, out var paths))
                byHash[hash] = paths = [];
            paths.Add(path);
        }

        return byHash
            .Where(kv => kv.Value.Count >= 2)
            .Select(kv => new DuplicateGroup(kv.Key, kv.Value))
            .ToList();
    }
}

public sealed record DuplicateGroup(string Hash, IReadOnlyList<string> Paths);
