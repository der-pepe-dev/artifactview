using System.Buffers.Binary;
using System.Text;

namespace ArtifactView.Infrastructure.Formats.Heic;

// Low-level ISO BMFF box reader.  All integers are big-endian.
internal static class HeicBoxReader
{
    public readonly record struct HeicBox(string Type, long DataStart, long DataEnd);

    // Reads all direct-child boxes within [start, end) from the stream.
    // Returns them in order; does not recurse into container boxes.
    public static List<HeicBox> ReadBoxes(Stream s, long start, long end)
    {
        var result = new List<HeicBox>();
        s.Position = start;

        // Pre-allocated buffers outside the loop (CA2014).
        byte[] hdrBuf = new byte[8];
        byte[] extBuf = new byte[8];

        while (s.Position + 8 <= end)
        {
            long boxStart = s.Position;

            if (s.ReadAtLeast(hdrBuf, 8, throwOnEndOfStream: false) < 8) break;

            var rawSize = BinaryPrimitives.ReadUInt32BigEndian(hdrBuf.AsSpan(0, 4));
            var type    = Encoding.Latin1.GetString(hdrBuf, 4, 4);

            long dataStart, boxEnd;

            if (rawSize == 1)
            {
                // Extended 64-bit size follows the type field.
                if (s.ReadAtLeast(extBuf, 8, throwOnEndOfStream: false) < 8) break;
                long large = (long)BinaryPrimitives.ReadUInt64BigEndian(extBuf);
                if (large < 16) break;
                dataStart = boxStart + 16;
                boxEnd    = boxStart + large;
            }
            else if (rawSize == 0)
            {
                // Box extends to end of container.
                dataStart = boxStart + 8;
                boxEnd    = end;
            }
            else
            {
                if (rawSize < 8) break;
                dataStart = boxStart + 8;
                boxEnd    = boxStart + (long)rawSize;
            }

            if (dataStart > boxEnd || boxEnd > end) break;

            result.Add(new HeicBox(type, dataStart, boxEnd));
            s.Position = boxEnd;
        }

        return result;
    }

    // Reads the 4-byte FullBox header (version + 3 flags bytes).
    // Returns version; advances stream by 4.
    public static (byte Version, uint Flags) ReadFullBoxHeader(Stream s)
    {
        Span<byte> buf = stackalloc byte[4];
        if (s.ReadAtLeast(buf, 4, throwOnEndOfStream: false) < 4) return (0, 0);
        var version = buf[0];
        uint flags  = (uint)(buf[1] << 16 | buf[2] << 8 | buf[3]);
        return (version, flags);
    }

    public static ushort ReadU16(Stream s)
    {
        Span<byte> b = stackalloc byte[2];
        s.ReadExactly(b);
        return BinaryPrimitives.ReadUInt16BigEndian(b);
    }

    public static uint ReadU32(Stream s)
    {
        Span<byte> b = stackalloc byte[4];
        s.ReadExactly(b);
        return BinaryPrimitives.ReadUInt32BigEndian(b);
    }

    // Reads a null-terminated string from the stream, stopping at maxEnd.
    public static string ReadNullString(Stream s, long maxEnd)
    {
        var sb = new StringBuilder();
        while (s.Position < maxEnd)
        {
            int b = s.ReadByte();
            if (b <= 0) break; // 0 = null terminator, -1 = EOF
            sb.Append((char)b);
        }
        return sb.ToString();
    }
}
