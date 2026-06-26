using System.Buffers.Binary;
using ArtifactView.Infrastructure.ThumbCache;
using Xunit;

namespace ArtifactView.Infrastructure.Tests;

public sealed class ThumbcacheReaderTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string TempFile(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.db");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    // Builds a minimal thumbcache file header (version 20 = Vista/7).
    private static byte[] BuildFileHeader(uint version = 20)
    {
        var header = new byte[24];
        "CMMM"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), version);
        return header;
    }

    // Builds a version-20 (Vista/7) cache entry with the given payload.
    private static byte[] BuildEntryV20(ulong hash, byte[] payload)
    {
        var headerSize = 32; // entry header before payload
        var entrySize  = headerSize + payload.Length;
        var entry = new byte[entrySize];

        "CMMM"u8.CopyTo(entry);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), (uint)entrySize);
        BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(8), hash);
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(16), payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(20), headerSize);
        payload.CopyTo(entry.AsSpan(headerSize));
        return entry;
    }

    [Fact]
    public void ReadVersion_ValidFile_ReturnsVersion()
    {
        var path = TempFile(BuildFileHeader(20));
        Assert.Equal(20u, ThumbcacheReader.ReadVersion(path));
    }

    [Fact]
    public void ReadVersion_InvalidSignature_ReturnsNull()
    {
        var path = TempFile([0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);
        Assert.Null(ThumbcacheReader.ReadVersion(path));
    }

    [Fact]
    public void ReadEntries_SingleEntry_ReturnsOne()
    {
        var payload = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // fake JPEG
        var file = BuildFileHeader(20).Concat(BuildEntryV20(0x1234, payload)).ToArray();
        var path = TempFile(file);

        var entries = ThumbcacheReader.ReadEntries(path);
        Assert.Single(entries);
        Assert.Equal(0x1234ul, entries[0].Hash);
        Assert.Equal(payload.Length, entries[0].DataSize);
    }

    [Fact]
    public void ReadEntries_EmptyPayload_Skipped()
    {
        var file = BuildFileHeader(20).Concat(BuildEntryV20(0xAAAA, [])).ToArray();
        var path = TempFile(file);

        var entries = ThumbcacheReader.ReadEntries(path);
        Assert.Empty(entries);
    }

    [Fact]
    public void ExtractPayload_ReturnsExactBytes()
    {
        var payload = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var file = BuildFileHeader(20).Concat(BuildEntryV20(0x5678, payload)).ToArray();
        var path = TempFile(file);

        var entries = ThumbcacheReader.ReadEntries(path);
        Assert.Single(entries);

        var extracted = ThumbcacheReader.ExtractPayload(path, entries[0]);
        Assert.Equal(payload, extracted);
    }

    [Fact]
    public void ReadEntries_MultipleEntries_ReturnsAll()
    {
        var p1 = new byte[] { 0x01, 0x02 };
        var p2 = new byte[] { 0x03, 0x04, 0x05 };
        var file = BuildFileHeader(20)
            .Concat(BuildEntryV20(1, p1))
            .Concat(BuildEntryV20(2, p2))
            .ToArray();
        var path = TempFile(file);

        var entries = ThumbcacheReader.ReadEntries(path);
        Assert.Equal(2, entries.Count);
        Assert.Equal(1ul, entries[0].Hash);
        Assert.Equal(2ul, entries[1].Hash);
    }

    // ── Win8+ format (version 21): filename embedded in each entry ───────────

    private static byte[] BuildEntryV21(ulong hash, string name, byte[] payload)
    {
        var headerSize = 88; // minimal Win8+ header before payload
        var entrySize  = headerSize + payload.Length;
        var entry = new byte[entrySize];

        "CMMM"u8.CopyTo(entry);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), (uint)entrySize);
        BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(8), hash);
        var nameBytes = System.Text.Encoding.Unicode.GetBytes(name);
        nameBytes.AsSpan(0, Math.Min(nameBytes.Length, 62)).CopyTo(entry.AsSpan(16));
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(80), payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(84), headerSize);
        payload.CopyTo(entry.AsSpan(headerSize));
        return entry;
    }

    [Fact]
    public void ReadEntries_Win8Plus_ParsesFilenameAndPayload()
    {
        var payload = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var file = BuildFileHeader(21).Concat(BuildEntryV21(0xABCDul, "photo.jpg", payload)).ToArray();
        var path = TempFile(file);

        var entries = ThumbcacheReader.ReadEntries(path);
        Assert.Single(entries);
        Assert.Equal(0xABCDul, entries[0].Hash);
        Assert.Equal("photo.jpg", entries[0].CacheFileName);
        Assert.Equal(payload.Length, entries[0].DataSize);

        var extracted = ThumbcacheReader.ExtractPayload(path, entries[0]);
        Assert.Equal(payload, extracted);
    }

    // ── Win10 / Win11: 32-byte file header ───────────────────────────────────

    private static byte[] BuildFileHeaderV30(uint version = 30)
    {
        var header = new byte[32];
        "CMMM"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), version);
        return header;
    }

    [Fact]
    public void ReadEntries_Win10_ParsesEntryAfterWiderHeader()
    {
        var payload = new byte[] { 0xAA, 0xBB };
        var file = BuildFileHeaderV30(30).Concat(BuildEntryV21(0x1111ul, "img.jpg", payload)).ToArray();
        var path = TempFile(file);

        var entries = ThumbcacheReader.ReadEntries(path);
        Assert.Single(entries);
        Assert.Equal(0x1111ul, entries[0].Hash);
        Assert.Equal(payload.Length, entries[0].DataSize);
    }

    [Fact]
    public void ReadEntries_Win11_ParsesEntryAfterWiderHeader()
    {
        var payload = new byte[] { 0xCC, 0xDD, 0xEE };
        var file = BuildFileHeaderV30(32).Concat(BuildEntryV21(0x2222ul, "snap.jpg", payload)).ToArray();
        var path = TempFile(file);

        var entries = ThumbcacheReader.ReadEntries(path);
        Assert.Single(entries);
        Assert.Equal(0x2222ul, entries[0].Hash);
        Assert.Equal("snap.jpg", entries[0].CacheFileName);
        Assert.Equal(payload.Length, entries[0].DataSize);

        var extracted = ThumbcacheReader.ExtractPayload(path, entries[0]);
        Assert.Equal(payload, extracted);
    }
}

