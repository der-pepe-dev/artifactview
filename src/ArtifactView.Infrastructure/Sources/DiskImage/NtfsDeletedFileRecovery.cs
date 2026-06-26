namespace ArtifactView.Infrastructure.Sources.DiskImage;

// Best-effort recovery of a deleted NTFS file's content by parsing its still-present
// MFT record's unnamed $DATA attribute and reading the data clusters raw from the volume.
// Recovered bytes are NOT guaranteed valid: the clusters may have been reallocated and
// overwritten since deletion. Callers should present results as best-effort, per the
// forensic "observation vs interpretation" rule.
public static class NtfsDeletedFileRecovery
{
    public sealed record DataAttribute(
        bool IsResident,
        byte[]? ResidentBytes,
        IReadOnlyList<(long Lcn, long Clusters)>? Runs, // Lcn == -1 => sparse gap (zeros)
        long RealSize);

    // Applies the NTFS update-sequence-array fixups to a 1024-byte MFT record in place,
    // restoring the last two bytes of each 512-byte sector. Required before reading any
    // field that may fall on a sector boundary (e.g. resident data spanning offset 510).
    public static void ApplyFixup(byte[] record)
    {
        if (record.Length < 0x30) return;
        int usaOffset = U16(record, 0x04);
        int usaCount  = U16(record, 0x06); // includes the USN entry
        if (usaCount < 1) return;

        for (int i = 1; i < usaCount; i++)
        {
            int sectorEnd = i * 512 - 2;
            int usaEntry  = usaOffset + i * 2;
            if (sectorEnd + 2 > record.Length || usaEntry + 2 > record.Length) break;
            record[sectorEnd]     = record[usaEntry];
            record[sectorEnd + 1] = record[usaEntry + 1];
        }
    }

    // Parses the unnamed $DATA attribute (type 0x80, name length 0). Returns null when
    // there is no recoverable data stream (none present, or compressed/encrypted).
    public static DataAttribute? ParseDataAttribute(byte[] record)
    {
        try
        {
            int ao = U16(record, 0x14); // first attribute offset
            while (ao + 8 <= record.Length)
            {
                uint type = U32(record, ao);
                if (type == 0xFFFFFFFF) break;

                int len = (int)U32(record, ao + 4);
                if (len <= 0 || ao + len > record.Length) break;

                if (type == 0x80 && record[ao + 9] == 0) // $DATA, unnamed (name length 0)
                {
                    bool nonResident = record[ao + 8] != 0;
                    if (!nonResident)
                    {
                        int contentLen = (int)U32(record, ao + 16);
                        int contentOff = U16(record, ao + 20);
                        int start = ao + contentOff;
                        if (contentLen < 0 || start + contentLen > record.Length) return null;
                        var bytes = new byte[contentLen];
                        Array.Copy(record, start, bytes, 0, contentLen);
                        return new DataAttribute(true, bytes, null, contentLen);
                    }

                    ushort flags = (ushort)U16(record, ao + 12);
                    if ((flags & 0x0001) != 0 || (flags & 0x4000) != 0)
                        return null; // compressed or encrypted — out of scope

                    long realSize = (long)U64(record, ao + 48);
                    int runsOffset = U16(record, ao + 32);
                    var runs = ParseRuns(record, ao + runsOffset, ao + len);
                    if (runs is null) return null;
                    return new DataAttribute(false, null, runs, realSize);
                }

                ao += len;
            }
        }
        catch { return null; }

        return null;
    }

    // Recovers the file content. rawVolume must be a stream positioned-by-Seek over the
    // same volume the LCNs reference; baseByte is added so an image-absolute stream works
    // (0 for a volume-relative stream).
    public static byte[]? Recover(System.IO.Stream rawVolume, long baseByte, int clusterSize, byte[] mftRecord, long maxBytes)
    {
        var data = ParseDataAttribute(mftRecord);
        if (data is null || clusterSize <= 0) return null;

        if (data.IsResident)
            return data.ResidentBytes is { Length: > 0 } b && b.Length <= maxBytes ? b : null;

        long real = data.RealSize;
        if (real <= 0 || real > maxBytes || data.Runs is null) return null;

        var output = new byte[(int)real];
        long pos = 0;
        foreach (var (lcn, clusters) in data.Runs)
        {
            if (pos >= real) break;
            long runBytes = clusters * (long)clusterSize;
            long toCopy = Math.Min(runBytes, real - pos);
            if (toCopy <= 0) continue;

            if (lcn < 0)
            {
                pos += toCopy; // sparse gap — leave zeros
                continue;
            }

            long offset = baseByte + lcn * (long)clusterSize;
            rawVolume.Seek(offset, System.IO.SeekOrigin.Begin);
            if (ReadFully(rawVolume, output, (int)pos, (int)toCopy) != toCopy)
                return null; // truncated/damaged — never surface bytes not in the image
            pos += toCopy;
        }

        return pos == real ? output : null;
    }

    // Parses NTFS data-run mapping pairs into (LCN, clusterCount) runs.
    private static List<(long Lcn, long Clusters)>? ParseRuns(byte[] rec, int start, int limit)
    {
        var runs = new List<(long, long)>();
        int p = start;
        long currentLcn = 0;
        while (p < limit && p < rec.Length)
        {
            byte header = rec[p++];
            if (header == 0) break;

            int lenSize = header & 0x0F;
            int offSize = (header >> 4) & 0x0F;
            if (lenSize == 0 || p + lenSize + offSize > rec.Length) return null;

            long runLength = (long)ReadUnsigned(rec, p, lenSize);
            p += lenSize;

            if (offSize == 0)
            {
                runs.Add((-1, runLength)); // sparse run
            }
            else
            {
                currentLcn += ReadSigned(rec, p, offSize);
                p += offSize;
                runs.Add((currentLcn, runLength));
            }
        }
        return runs;
    }

    private static int   U16(byte[] b, int o) => b[o] | (b[o + 1] << 8);
    private static uint  U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    private static ulong U64(byte[] b, int o)
    {
        ulong v = 0;
        for (int i = 7; i >= 0; i--) v = (v << 8) | b[o + i];
        return v;
    }

    private static ulong ReadUnsigned(byte[] b, int o, int count)
    {
        ulong v = 0;
        for (int i = count - 1; i >= 0; i--) v = (v << 8) | b[o + i];
        return v;
    }

    private static long ReadSigned(byte[] b, int o, int count)
    {
        long v = 0;
        for (int i = count - 1; i >= 0; i--) v = (v << 8) | b[o + i];
        // sign-extend from the top bit of the highest byte
        long signBit = 1L << (count * 8 - 1);
        if ((v & signBit) != 0) v -= 1L << (count * 8);
        return v;
    }

    private static int ReadFully(System.IO.Stream s, byte[] buf, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = s.Read(buf, offset + total, count - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
