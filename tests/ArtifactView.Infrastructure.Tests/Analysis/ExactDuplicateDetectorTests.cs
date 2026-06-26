using ArtifactView.Infrastructure.Analysis;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class ExactDuplicateDetectorTests
{
    private static readonly string HashA = new('A', 64);
    private static readonly string HashB = new('B', 64);
    private static readonly string HashC = new('C', 64);

    [Fact]
    public void Returns_empty_for_no_inputs()
        => Assert.Empty(ExactDuplicateDetector.Detect([]));

    [Fact]
    public void Returns_empty_for_single_file()
        => Assert.Empty(ExactDuplicateDetector.Detect([("/a/file.jpg", HashA)]));

    [Fact]
    public void Returns_empty_when_all_hashes_unique()
    {
        var inputs = new[]
        {
            ("/a/one.jpg", HashA),
            ("/a/two.jpg", HashB),
            ("/a/three.jpg", HashC)
        };
        Assert.Empty(ExactDuplicateDetector.Detect(inputs));
    }

    [Fact]
    public void Detects_exact_duplicate_pair()
    {
        var inputs = new[]
        {
            ("/a/orig.jpg", HashA),
            ("/a/copy.jpg", HashA)
        };
        var groups = ExactDuplicateDetector.Detect(inputs);
        var group  = Assert.Single(groups);
        Assert.Equal(HashA, group.Hash);
        Assert.Equal(2, group.Paths.Count);
    }

    [Fact]
    public void Detects_triplicates_as_one_group()
    {
        var inputs = new[]
        {
            ("/a/a.jpg", HashA),
            ("/a/b.jpg", HashA),
            ("/a/c.jpg", HashA)
        };
        var groups = ExactDuplicateDetector.Detect(inputs);
        var group  = Assert.Single(groups);
        Assert.Equal(3, group.Paths.Count);
    }

    [Fact]
    public void Returns_separate_groups_for_distinct_hashes()
    {
        var inputs = new[]
        {
            ("/a/a.jpg", HashA),
            ("/a/b.jpg", HashA),
            ("/a/c.jpg", HashB),
            ("/a/d.jpg", HashB)
        };
        var groups = ExactDuplicateDetector.Detect(inputs);
        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Hash == HashA);
        Assert.Contains(groups, g => g.Hash == HashB);
    }

    [Fact]
    public void Skips_entries_with_empty_hash()
    {
        var inputs = new[]
        {
            ("/a/a.jpg", HashA),
            ("/a/b.jpg", string.Empty),  // no hash — skip
            ("/a/c.jpg", HashA)
        };
        var groups = ExactDuplicateDetector.Detect(inputs);
        var group  = Assert.Single(groups);
        Assert.Equal(2, group.Paths.Count);
    }

    [Fact]
    public void Hash_comparison_is_case_insensitive()
    {
        var inputs = new[]
        {
            ("/a/a.jpg", HashA.ToLowerInvariant()),
            ("/a/b.jpg", HashA.ToUpperInvariant())
        };
        var groups = ExactDuplicateDetector.Detect(inputs);
        Assert.Single(groups);
    }

    [Fact]
    public void Group_contains_all_matching_paths()
    {
        var inputs = new[]
        {
            ("/a/original.jpg", HashA),
            ("/a/copy1.jpg",    HashA),
            ("/a/copy2.jpg",    HashA),
            ("/a/other.jpg",    HashB)
        };
        var groups = ExactDuplicateDetector.Detect(inputs);
        var group  = Assert.Single(groups);
        Assert.Contains("/a/original.jpg", group.Paths);
        Assert.Contains("/a/copy1.jpg",    group.Paths);
        Assert.Contains("/a/copy2.jpg",    group.Paths);
    }
}
