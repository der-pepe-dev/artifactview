namespace ArtifactView.Infrastructure.Analysis;

// Finds near-duplicate images using perceptual hash (dHash) comparison.
//
// Two files are considered near-duplicates when their Hamming distance is
// at or below PerceptualHash.NearDuplicateThreshold (10 bits).
//
// Algorithm: O(n²) pairwise comparison — suitable for folder-scale scans
// (hundreds of files).  For library-scale use, a BK-tree or VP-tree would
// be more efficient but is not needed here.
public static class NearDuplicateDetector
{
    /// <param name="fileHashes">
    /// (absolutePath, perceptualHash) pairs.  Entries with Value=0 are treated
    /// as "no hash available" and excluded from comparison.
    /// </param>
    /// <param name="threshold">
    /// Maximum Hamming distance to consider two files near-duplicates.
    /// Defaults to <see cref="PerceptualHash.NearDuplicateThreshold"/>.
    /// </param>
    public static IReadOnlyList<NearDuplicateGroup> Detect(
        IEnumerable<(string Path, PerceptualHash Hash)> fileHashes,
        int threshold = PerceptualHash.NearDuplicateThreshold)
    {
        var items = fileHashes
            .Where(x => x.Hash.Value != 0)
            .ToList();

        if (items.Count < 2)
            return [];

        // Union-Find for grouping transitively close images.
        var parent = Enumerable.Range(0, items.Count).ToArray();

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]]; // path compression
                x = parent[x];
            }
            return x;
        }

        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        for (int i = 0; i < items.Count; i++)
        {
            for (int j = i + 1; j < items.Count; j++)
            {
                if (PerceptualHash.HammingDistance(items[i].Hash, items[j].Hash) <= threshold)
                    Union(i, j);
            }
        }

        // Collect groups where root has 2+ members.
        var byRoot = new Dictionary<int, List<(string Path, PerceptualHash Hash)>>();
        for (int i = 0; i < items.Count; i++)
        {
            var root = Find(i);
            if (!byRoot.TryGetValue(root, out var list))
                byRoot[root] = list = [];
            list.Add(items[i]);
        }

        return byRoot.Values
            .Where(g => g.Count >= 2)
            .Select(g => new NearDuplicateGroup(g))
            .ToList();
    }
}

/// <summary>
/// A group of files that are perceptually similar (within dHash Hamming threshold).
/// </summary>
public sealed class NearDuplicateGroup(IReadOnlyList<(string Path, PerceptualHash Hash)> members)
{
    public IReadOnlyList<(string Path, PerceptualHash Hash)> Members { get; } = members;

    public int MaxHammingDistance()
    {
        int max = 0;
        for (int i = 0; i < Members.Count; i++)
            for (int j = i + 1; j < Members.Count; j++)
            {
                var d = PerceptualHash.HammingDistance(Members[i].Hash, Members[j].Hash);
                if (d > max) max = d;
            }
        return max;
    }
}
