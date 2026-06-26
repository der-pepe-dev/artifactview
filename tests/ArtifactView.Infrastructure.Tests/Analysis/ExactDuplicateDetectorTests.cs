using ArtifactView.Infrastructure.Analysis;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class ExactDuplicateDetectorTests
{
    private static readonly string HashA = new('A', 64);
    private static readonly string HashB = new('B', 64);
    private static readonly string HashC = new('C', 64);

    [Test]
    public async Task Returns_empty_for_no_inputs()
        => await Assert.That(ExactDuplicateDetector.Detect([])).IsEmpty();

    [Test]
    public async Task Returns_empty_for_single_file()
        => await Assert.That(ExactDuplicateDetector.Detect([("/a/file.jpg", HashA)])).IsEmpty();

    [Test]
    public async Task Returns_empty_when_all_hashes_unique()
    {
        var inputs = new[]
        {
            ("/a/one.jpg", HashA),
            ("/a/two.jpg", HashB),
            ("/a/three.jpg", HashC)
        };
        await Assert.That(ExactDuplicateDetector.Detect(inputs)).IsEmpty();
    }

    [Test]
    public async Task Detects_exact_duplicate_pair()
    {
        var inputs = new[]
        {
            ("/a/orig.jpg", HashA),
            ("/a/copy.jpg", HashA)
        };
        var groups = ExactDuplicateDetector.Detect(inputs);
        var group  = await Assert.That(groups).HasSingleItem();
        await Assert.That(group.Hash).IsEqualTo(HashA);
        await Assert.That(group.Paths.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Detects_triplicates_as_one_group()
    {
        var inputs = new[]
        {
            ("/a/a.jpg", HashA),
            ("/a/b.jpg", HashA),
            ("/a/c.jpg", HashA)
        };
        var groups = ExactDuplicateDetector.Detect(inputs);
        var group  = await Assert.That(groups).HasSingleItem();
        await Assert.That(group.Paths.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Returns_separate_groups_for_distinct_hashes()
    {
        var inputs = new[]
        {
            ("/a/a.jpg", HashA),
            ("/a/b.jpg", HashA),
            ("/a/c.jpg", HashB),
            ("/a/d.jpg", HashB)
        };
        var groups = ExactDuplicateDetector.Detect(inputs);
        await Assert.That(groups.Count).IsEqualTo(2);
        await Assert.That(groups).Contains(g => g.Hash == HashA);
        await Assert.That(groups).Contains(g => g.Hash == HashB);
    }

    [Test]
    public async Task Skips_entries_with_empty_hash()
    {
        var inputs = new[]
        {
            ("/a/a.jpg", HashA),
            ("/a/b.jpg", string.Empty),  // no hash — skip
            ("/a/c.jpg", HashA)
        };
        var groups = ExactDuplicateDetector.Detect(inputs);
        var group  = await Assert.That(groups).HasSingleItem();
        await Assert.That(group.Paths.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Hash_comparison_is_case_insensitive()
    {
        var inputs = new[]
        {
            ("/a/a.jpg", HashA.ToLowerInvariant()),
            ("/a/b.jpg", HashA.ToUpperInvariant())
        };
        var groups = ExactDuplicateDetector.Detect(inputs);
        await Assert.That(groups).HasSingleItem();
    }

    [Test]
    public async Task Group_contains_all_matching_paths()
    {
        var inputs = new[]
        {
            ("/a/original.jpg", HashA),
            ("/a/copy1.jpg",    HashA),
            ("/a/copy2.jpg",    HashA),
            ("/a/other.jpg",    HashB)
        };
        var groups = ExactDuplicateDetector.Detect(inputs);
        var group  = await Assert.That(groups).HasSingleItem();
        await Assert.That(group.Paths).Contains("/a/original.jpg");
        await Assert.That(group.Paths).Contains("/a/copy1.jpg");
        await Assert.That(group.Paths).Contains("/a/copy2.jpg");
    }
}