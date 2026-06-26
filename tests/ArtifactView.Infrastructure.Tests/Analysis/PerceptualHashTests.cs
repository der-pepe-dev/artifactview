using ArtifactView.Infrastructure.Analysis;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class PerceptualHashTests
{
    // Builds a 9×8 grayscale ramp: all pixels equal to their column index * 10.
    private static byte[] MakeRamp(int offset = 0)
    {
        var buf = new byte[72];
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 9; col++)
                buf[row * 9 + col] = (byte)Math.Min(255, col * 30 + offset);
        return buf;
    }

    [Fact]
    public void Hamming_distance_of_identical_hashes_is_zero()
    {
        var h = PerceptualHash.FromGrayscale9x8(MakeRamp());
        Assert.Equal(0, PerceptualHash.HammingDistance(h, h));
    }

    [Fact]
    public void Hamming_distance_is_symmetric()
    {
        var a = PerceptualHash.FromGrayscale9x8(MakeRamp(0));
        var b = PerceptualHash.FromGrayscale9x8(MakeRamp(5));
        Assert.Equal(PerceptualHash.HammingDistance(a, b),
                     PerceptualHash.HammingDistance(b, a));
    }

    [Fact]
    public void Uniform_image_produces_zero_hash()
    {
        // All pixels equal → no "left < right" comparisons → all bits 0.
        var uniform = new byte[72]; // all zeros
        var h = PerceptualHash.FromGrayscale9x8(uniform);
        Assert.Equal(0UL, h.Value);
    }

    [Fact]
    public void Strictly_decreasing_ramp_produces_all_ones_hash()
    {
        // pixel[col] > pixel[col+1] for every column → bit = 0 for each pair
        // (dHash bit = 1 when left < right)
        var buf = new byte[72];
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 9; col++)
                buf[row * 9 + col] = (byte)(255 - col * 28);
        var h = PerceptualHash.FromGrayscale9x8(buf);
        Assert.Equal(0UL, h.Value);
    }

    [Fact]
    public void Strictly_increasing_ramp_produces_all_ones_hash()
    {
        var h = PerceptualHash.FromGrayscale9x8(MakeRamp());
        // Every col < col+1 → bit = 1 for every position → all 64 bits set.
        Assert.Equal(ulong.MaxValue, h.Value);
    }

    [Fact]
    public void FromGrayscale9x8_throws_for_short_buffer()
    {
        Assert.Throws<ArgumentException>(() =>
            PerceptualHash.FromGrayscale9x8(new byte[71]));
    }

    [Fact]
    public void Hamming_distance_between_complementary_hashes_is_64()
    {
        var a = new PerceptualHash(0UL);
        var b = new PerceptualHash(ulong.MaxValue);
        Assert.Equal(64, PerceptualHash.HammingDistance(a, b));
    }

    [Fact]
    public void Near_duplicate_threshold_is_positive()
        => Assert.True(PerceptualHash.NearDuplicateThreshold > 0);
}

public sealed class NearDuplicateDetectorTests
{
    private static PerceptualHash H(ulong v) => new(v);

    [Fact]
    public void Returns_empty_for_no_inputs()
        => Assert.Empty(NearDuplicateDetector.Detect([]));

    [Fact]
    public void Returns_empty_for_single_file()
        => Assert.Empty(NearDuplicateDetector.Detect([("/a.jpg", H(0xFF))]));

    [Fact]
    public void Returns_empty_for_zero_hashes()
    {
        var inputs = new[]
        {
            ("/a.jpg", H(0)),
            ("/b.jpg", H(0)),
        };
        // Value=0 means "not computed" — excluded.
        Assert.Empty(NearDuplicateDetector.Detect(inputs));
    }

    [Fact]
    public void Detects_identical_hash_pair()
    {
        var h = H(0xABCDEF0123456789UL);
        var inputs = new[] { ("/a.jpg", h), ("/b.jpg", h) };
        var groups = NearDuplicateDetector.Detect(inputs);
        var group  = Assert.Single(groups);
        Assert.Equal(2, group.Members.Count);
    }

    [Fact]
    public void Respects_threshold_parameter()
    {
        // Hamming distance = 1 between these two hashes.
        var inputs = new[]
        {
            ("/a.jpg", H(0b0000_0001UL)),
            ("/b.jpg", H(0b0000_0011UL)),
        };
        Assert.Empty(NearDuplicateDetector.Detect(inputs, threshold: 0));
        Assert.Single(NearDuplicateDetector.Detect(inputs, threshold: 1));
    }

    [Fact]
    public void Transitivity_groups_three_similar_images()
    {
        // a≈b and b≈c → {a,b,c} in same group.
        // d(a,b)=1, d(b,c)=1, d(a,c)=2
        var inputs = new[]
        {
            ("/a.jpg", H(0b0000_0001UL)),
            ("/b.jpg", H(0b0000_0011UL)),
            ("/c.jpg", H(0b0000_0111UL)),
        };
        var groups = NearDuplicateDetector.Detect(inputs, threshold: 2);
        var group  = Assert.Single(groups);
        Assert.Equal(3, group.Members.Count);
    }

    [Fact]
    public void Different_images_not_grouped()
    {
        var inputs = new[]
        {
            ("/a.jpg", H(0x0000_0000_0000_00FFUL)),
            ("/b.jpg", H(0xFFFF_FFFF_FFFF_FFFFUL)),  // Hamming distance = 56
        };
        Assert.Empty(NearDuplicateDetector.Detect(inputs));
    }

    [Fact]
    public void MaxHammingDistance_returns_correct_value()
    {
        // d(a,b)=1, d(b,c)=1, d(a,c)=2
        var inputs = new[]
        {
            ("/a.jpg", H(0b0001UL)),
            ("/b.jpg", H(0b0011UL)),
            ("/c.jpg", H(0b0111UL)),
        };
        var groups = NearDuplicateDetector.Detect(inputs, threshold: 2);
        var group  = Assert.Single(groups);
        Assert.Equal(2, group.MaxHammingDistance());
    }
}
