namespace ArtifactView.Infrastructure.Analysis;

// Difference-hash (dHash) value for an image.
//
// dHash algorithm:
//  1. Scale image to 9×8 grayscale.
//  2. For each of the 8 rows, compare 8 adjacent pixel pairs (9 columns → 8 bits).
//  3. Concatenate the 64 bits into a ulong.
//
// Two images with Hamming distance ≤ 10 are considered near-duplicates.
// Distance 0 = visually identical (same content, possibly different metadata).
// Distance 1–5 = likely the same photo with minor edits (crop, brightness).
// Distance 6–10 = similar scene / resized / re-encoded.
// Distance > 15 = different images.
public readonly record struct PerceptualHash(ulong Value)
{
    // Empirically calibrated threshold for "near-duplicate":
    // covers JPEG recompression, slight brightness/contrast adjustments, and minor crops.
    public const int NearDuplicateThreshold = 10;

    public static int HammingDistance(PerceptualHash a, PerceptualHash b)
    {
        var xor = a.Value ^ b.Value;
        return System.Numerics.BitOperations.PopCount(xor);
    }

    public override string ToString() => $"{Value:X16}";

    /// <summary>
    /// Computes a dHash from a pre-reduced 9×8 grayscale byte array (row-major).
    /// The caller is responsible for scaling and grayscale conversion.
    /// </summary>
    public static PerceptualHash FromGrayscale9x8(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length < 72)
            throw new ArgumentException("Expected 9×8 = 72 grayscale bytes.", nameof(pixels));

        ulong hash = 0;
        for (int row = 0; row < 8; row++)
        {
            int rowBase = row * 9;
            for (int col = 0; col < 8; col++)
            {
                if (pixels[rowBase + col] < pixels[rowBase + col + 1])
                    hash |= 1UL << (row * 8 + col);
            }
        }
        return new PerceptualHash(hash);
    }
}
