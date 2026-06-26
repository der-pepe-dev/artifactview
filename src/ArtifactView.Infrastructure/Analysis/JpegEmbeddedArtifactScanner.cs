using ArtifactView.Core.Models;

namespace ArtifactView.Infrastructure.Analysis;

// Scans JPEG files for embedded artifacts beyond the main image:
//   • Trailing data after EOI (secondary JPEGs, MP4 motion video, unknown payloads)
//   • XMP-declared depth maps (Google GDepth), gain maps (GainMap), motion photos
//
// Detection is signature-based and read-only — no bytes are modified.
// Each discovered artifact is returned as an EmbeddedArtifact with its
// byte offset and length so the Reconstruction tab can extract it exactly.
public static class JpegEmbeddedArtifactScanner
{

    /// <summary>
    /// Scans a JPEG file for embedded artifacts.  Returns an empty list for
    /// non-JPEG files, unreadable files, or files with no embedded extras.
    /// </summary>
    public static IReadOnlyList<EmbeddedArtifact> Scan(string path)
    {
        var results = new List<EmbeddedArtifact>();

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 4)
                return results;

            // Verify JPEG SOI
            Span<byte> header = stackalloc byte[3];
            fs.ReadExactly(header);
            if (header[0] != 0xFF || header[1] != 0xD8 || header[2] != 0xFF)
                return results;

            // ── Scan XMP for declarations (tells us *what* is embedded) ──
            var xmpTypes = ScanXmpDeclarations(path);

            // ── Scan MPF offset table for secondary images ───────────────
            var mpfEntries = ScanMpfOffsets(fs);

            // ── Find EOI and scan trailing data (tells us *where* it is) ─
            var eoiPos = FindMainImageEoi(fs);

            // ── MPF-referenced secondary images ──────────────────────────
            // MPF entries point to secondary JPEGs within the file. These
            // are often depth maps (Samsung, Sony), gain maps, or other
            // camera-specific auxiliary data.
            foreach (var (offset, size) in mpfEntries)
            {
                if (offset <= 0 || offset >= fs.Length) continue;

                // Determine length: use the declared size, or (if 0) compute
                // from the next entry/EOI/file end.
                var length = size > 0 ? size : fs.Length - offset;

                var type = xmpTypes.HasFlag(XmpDeclaredTypes.DepthMap)
                    ? EmbeddedArtifactType.DepthMap
                    : xmpTypes.HasFlag(XmpDeclaredTypes.GainMap)
                        ? EmbeddedArtifactType.GainMap
                        : EmbeddedArtifactType.SecondaryImage;

                var name = type switch
                {
                    EmbeddedArtifactType.DepthMap     => "Depth map (MPF)",
                    EmbeddedArtifactType.GainMap      => "HDR gain map (MPF)",
                    _                                 => "Secondary image (MPF)"
                };

                results.Add(new EmbeddedArtifact
                {
                    Id          = Guid.NewGuid(),
                    Type        = type,
                    DisplayName = name,
                    Offset      = offset,
                    Length      = length,
                    MimeType    = "image/jpeg",
                    ParseConfidence = new ConfidenceScore(88)
                });

                // Consume the XMP flag so trailing-data detection doesn't duplicate.
                xmpTypes &= ~TypeToFlag(type);
            }

            // ── Trailing data detection ──────────────────────────────────
            var hasTrailing = eoiPos >= 0 && eoiPos < fs.Length - 2;
            var trailingStart  = hasTrailing ? eoiPos + 2 : 0L;
            var trailingLength = hasTrailing ? fs.Length - trailingStart : 0L;

            if (hasTrailing && trailingLength >= 4
                && !mpfEntries.Any(e => e.Offset == trailingStart))
            {
                fs.Position = trailingStart;
                Span<byte> trailHeader = stackalloc byte[12];
                var read = fs.Read(trailHeader);

                if (read >= 4)
                {
                    // Identify the trailing payload and correlate with XMP declarations.
                    if (trailHeader[0] == 0xFF && trailHeader[1] == 0xD8 && trailHeader[2] == 0xFF)
                    {
                        var type = xmpTypes.HasFlag(XmpDeclaredTypes.DepthMap)  ? EmbeddedArtifactType.DepthMap
                                 : xmpTypes.HasFlag(XmpDeclaredTypes.GainMap)   ? EmbeddedArtifactType.GainMap
                                 : xmpTypes.HasFlag(XmpDeclaredTypes.SecondaryImage) ? EmbeddedArtifactType.SecondaryImage
                                 : EmbeddedArtifactType.SecondaryImage;

                        var name = type switch
                        {
                            EmbeddedArtifactType.DepthMap       => "Depth map",
                            EmbeddedArtifactType.GainMap        => "HDR gain map",
                            EmbeddedArtifactType.SecondaryImage => "Secondary JPEG image",
                            _ => "Trailing JPEG image"
                        };

                        results.Add(new EmbeddedArtifact
                        {
                            Id          = Guid.NewGuid(),
                            Type        = type,
                            DisplayName = name,
                            Offset      = trailingStart,
                            Length      = trailingLength,
                            MimeType    = "image/jpeg",
                            ParseConfidence = new ConfidenceScore(
                                type == EmbeddedArtifactType.SecondaryImage ? 80 : 90)
                        });

                        var remaining = xmpTypes & ~TypeToFlag(type);
                        AddXmpOnlyDeclarations(remaining, results);
                    }
                    else if (read >= 8 && trailHeader[4] == 0x66 && trailHeader[5] == 0x74 &&
                             trailHeader[6] == 0x79 && trailHeader[7] == 0x70)
                    {
                        results.Add(new EmbeddedArtifact
                        {
                            Id          = Guid.NewGuid(),
                            Type        = EmbeddedArtifactType.MotionPhotoVideo,
                            DisplayName = "Embedded motion photo video",
                            Offset      = trailingStart,
                            Length      = trailingLength,
                            MimeType    = "video/mp4",
                            ParseConfidence = new ConfidenceScore(90)
                        });

                        var remaining = xmpTypes & ~XmpDeclaredTypes.MotionPhoto;
                        AddXmpOnlyDeclarations(remaining, results);
                    }
                    else
                    {
                        results.Add(new EmbeddedArtifact
                        {
                            Id          = Guid.NewGuid(),
                            Type        = EmbeddedArtifactType.Unknown,
                            DisplayName = $"Unknown trailing data ({trailingLength:N0} bytes)",
                            Offset      = trailingStart,
                            Length      = trailingLength,
                            ParseConfidence = new ConfidenceScore(70)
                        });

                        AddXmpOnlyDeclarations(xmpTypes, results);
                    }
                }
                else
                {
                    AddXmpOnlyDeclarations(xmpTypes, results);
                }
            }
            else
            {
                AddXmpOnlyDeclarations(xmpTypes, results);
            }
        }
        catch
        {
            // Non-fatal: artifact scanning is best-effort.
        }

        // ── XMP base64-encoded images (GDepth:Data, GImage:Data) ─────────
        // Always runs regardless of trailing data or MPF — some cameras store
        // depth/secondary images as base64 inside XMP only.
        ExtractXmpBase64Images(path, results);

        // Remove any non-extractable "declared" placeholders that now have
        // a real extractable artifact for the same type.
        results.RemoveAll(a => !a.IsExtractable &&
            results.Exists(b => b.Type == a.Type && b.IsExtractable));

        return results;
    }

    /// <summary>
    /// Extracts the raw bytes of an embedded artifact.  For base64-encoded
    /// XMP images the decoded bytes are already in <see cref="EmbeddedArtifact.Data"/>;
    /// for file-offset artifacts we seek and read.
    /// </summary>
    public static byte[]? ExtractPayload(string path, EmbeddedArtifact artifact)
    {
        // Pre-decoded data (base64 from XMP) — return directly.
        if (artifact.Data is { Length: > 0 })
            return artifact.Data;

        if (artifact.Offset is null || artifact.Length is null || artifact.Length <= 0)
            return null;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (artifact.Offset.Value + artifact.Length.Value > fs.Length)
                return null;

            fs.Position = artifact.Offset.Value;
            var buffer = new byte[artifact.Length.Value];
            var read   = fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return read == buffer.Length ? buffer : null;
        }
        catch
        {
            return null;
        }
    }

    // Extracts base64-encoded images from XMP properties.  Google's GDepth
    // and GImage namespaces store depth maps and secondary images as base64
    // in the "Data" property.  We decode them and attach as artifacts with
    // pre-decoded Data bytes (no file offset needed).
    private static void ExtractXmpBase64Images(string path, List<EmbeddedArtifact> results)
    {
        try
        {
            // First try the fast path: MetadataExtractor's XmpMeta.
            var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(path);

            const string NsGDepth = "http://ns.google.com/photos/1.0/depthmap/";
            const string NsGImage = "http://ns.google.com/photos/1.0/image/";

            byte[]? depthData = null;
            byte[]? imageData = null;

            // Try every XmpDirectory — extended XMP creates additional instances.
            foreach (var xmpDir in directories.OfType<MetadataExtractor.Formats.Xmp.XmpDirectory>())
            {
                var xmpMeta = xmpDir.XmpMeta;
                if (xmpMeta is null) continue;

                depthData ??= XmpBase64(xmpMeta, NsGDepth, "Data");
                imageData ??= XmpBase64(xmpMeta, NsGImage, "Data");

                if (depthData is not null && imageData is not null)
                    break;
            }

            // If MetadataExtractor didn't reassemble extended XMP, read it manually.
            if (depthData is null && imageData is null)
            {
                var extXmp = ReadExtendedXmp(path);
                if (extXmp is not null)
                {
                    depthData = ExtractBase64FromXml(extXmp, "GDepth:Data");
                    imageData = ExtractBase64FromXml(extXmp, "GImage:Data");
                }
            }

            if (depthData is { Length: > 0 } &&
                !results.Exists(a => a.Type == EmbeddedArtifactType.DepthMap && a.IsExtractable))
            {
                results.Add(new EmbeddedArtifact
                {
                    Id          = Guid.NewGuid(),
                    Type        = EmbeddedArtifactType.DepthMap,
                    DisplayName = "Depth map (XMP base64)",
                    MimeType    = DetectMime(depthData),
                    Data        = depthData,
                    Length      = depthData.Length,
                    ParseConfidence = new ConfidenceScore(92)
                });
            }

            if (imageData is { Length: > 0 } &&
                !results.Exists(a => a.Type == EmbeddedArtifactType.SecondaryImage && a.IsExtractable))
            {
                results.Add(new EmbeddedArtifact
                {
                    Id          = Guid.NewGuid(),
                    Type        = EmbeddedArtifactType.SecondaryImage,
                    DisplayName = "Secondary image (XMP base64)",
                    MimeType    = DetectMime(imageData),
                    Data        = imageData,
                    Length      = imageData.Length,
                    ParseConfidence = new ConfidenceScore(90)
                });
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[JpegEmbeddedArtifactScanner] XMP base64 scan failed for '{path}': {ex.GetType().Name}: {ex.Message}"); }
    }

    // Reads and reassembles extended XMP from JPEG APP1 segments.
    // Extended XMP stores large data (like base64 depth maps) across
    // multiple APP1 segments with the "http://ns.adobe.com/xmp/extension/" header.
    //
    // Segment layout:
    //   "http://ns.adobe.com/xmp/extension/\0" (35 bytes)
    //   MD5 hash (32 bytes ASCII hex)
    //   Total data length (4 bytes big-endian)
    //   This chunk's offset (4 bytes big-endian)
    //   Chunk data bytes
    private static string? ReadExtendedXmp(string path)
    {
        const int ExtHeaderLen = 35; // "http://ns.adobe.com/xmp/extension/\0"
        const int MdHashLen   = 32;
        const int MetaLen     = MdHashLen + 4 + 4; // hash + total len + offset

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> marker = stackalloc byte[2];
            Span<byte> lenBuf = stackalloc byte[2];

            // Skip SOI
            fs.Position = 2;

            var chunks = new SortedDictionary<int, byte[]>();
            int totalLen = 0;

            while (fs.Position < fs.Length - 4)
            {
                if (fs.ReadByte() != 0xFF) break;
                int m = fs.ReadByte();
                if (m < 0) break;

                // Standalone markers
                if (m == 0x01 || (m >= 0xD0 && m <= 0xD9))
                    continue;

                if (fs.Read(lenBuf) < 2) break;
                int segLen = (lenBuf[0] << 8) | lenBuf[1];
                if (segLen < 2) break;

                var segStart = fs.Position;
                var dataLen  = segLen - 2;

                // SOS — stop scanning
                if (m == 0xDA) break;

                // APP1 = 0xE1
                if (m == 0xE1 && dataLen > ExtHeaderLen + MetaLen)
                {
                    var headerBytes = new byte[ExtHeaderLen];
                    if (fs.Read(headerBytes, 0, ExtHeaderLen) == ExtHeaderLen)
                    {
                        // Check for "http://ns.adobe.com/xmp/extension/\0"
                        if (headerBytes[0] == 0x68 && // 'h'
                            headerBytes[4] == 0x3A && // ':'
                            headerBytes[34] == 0x00 &&
                            System.Text.Encoding.ASCII.GetString(headerBytes, 0, 34) ==
                                "http://ns.adobe.com/xmp/extension/")
                        {
                            var meta = new byte[MetaLen];
                            if (fs.Read(meta, 0, MetaLen) == MetaLen)
                            {
                                totalLen = (meta[MdHashLen] << 24) | (meta[MdHashLen + 1] << 16) |
                                           (meta[MdHashLen + 2] << 8) | meta[MdHashLen + 3];
                                int offset = (meta[MdHashLen + 4] << 24) | (meta[MdHashLen + 5] << 16) |
                                             (meta[MdHashLen + 6] << 8) | meta[MdHashLen + 7];

                                int chunkLen = dataLen - ExtHeaderLen - MetaLen;
                                if (chunkLen > 0)
                                {
                                    var chunk = new byte[chunkLen];
                                    var bytesRead = fs.ReadAtLeast(chunk, chunkLen, throwOnEndOfStream: false);
                                    if (bytesRead == chunkLen)
                                        chunks[offset] = chunk;
                                }
                            }
                        }
                    }
                }

                fs.Position = segStart + segLen - 2;
            }

            if (chunks.Count == 0) return null;

            // Reassemble
            using var ms = new MemoryStream(totalLen > 0 ? totalLen : 256 * 1024);
            foreach (var chunk in chunks.Values)
                ms.Write(chunk);

            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[JpegEmbeddedArtifactScanner] Extended XMP read failed for '{path}': {ex.GetType().Name}: {ex.Message}"); return null; }
    }

    // Extracts base64 content from a raw XMP XML string by searching for a
    // specific property element.  This handles the case where the extended
    // XMP XML can't be parsed by XmpCore (e.g. due to size or fragment issues).
    private static byte[]? ExtractBase64FromXml(string xml, string propName)
    {
        // Look for <propName>base64...</propName> or propName="base64..."
        // Property element form: <GDepth:Data>...base64...</GDepth:Data>
        var startTag = $"<{propName}>";
        var endTag   = $"</{propName}>";

        var i = xml.IndexOf(startTag, StringComparison.Ordinal);
        if (i < 0)
        {
            // Attribute form: GDepth:Data="...base64..."
            var attrPrefix = $"{propName}=\"";
            i = xml.IndexOf(attrPrefix, StringComparison.Ordinal);
            if (i < 0) return null;
            i += attrPrefix.Length;
            var end = xml.IndexOf('"', i);
            if (end <= i) return null;

            try { return Convert.FromBase64String(xml[i..end]); }
            catch { return null; }
        }

        i += startTag.Length;
        var j = xml.IndexOf(endTag, i, StringComparison.Ordinal);
        if (j <= i) return null;

        try { return Convert.FromBase64String(xml[i..j]); }
        catch { return null; }
    }

    private static byte[]? XmpBase64(XmpCore.IXmpMeta xmp, string ns, string name)
    {
        try
        {
            if (!xmp.DoesPropertyExist(ns, name)) return null;
            var b64 = xmp.GetPropertyString(ns, name);
            if (string.IsNullOrEmpty(b64)) return null;
            return Convert.FromBase64String(b64);
        }
        catch { return null; }
    }

    private static string DetectMime(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "image/jpeg";
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return "image/png";
        return "application/octet-stream";
    }

    // Scans JPEG APP2 segments for MPF (Multi-Picture Format) offset tables.
    // Many cameras (Samsung, Sony, Google Pixel) store secondary images —
    // depth maps, gain maps, wide-angle captures — as MPF entries.  The MPF
    // index contains absolute file offsets to each secondary JPEG.
    //
    // MPF structure (simplified):
    //   APP2 marker FF E2, length, "MPF\0" (4 bytes)
    //   Endianness tag (II or MM), fixed 0x002A, offset to IFD0
    //   IFD0: tag 0xB001 = MP Entry count, tag 0xB002 = MP Entry table
    //   Each MP Entry: 4 bytes attribute, 4 bytes size, 4 bytes offset, 2+2 bytes dep
    private static List<(long Offset, long Size)> ScanMpfOffsets(FileStream fs)
    {
        var entries = new List<(long Offset, long Size)>();
        try
        {
            fs.Position = 2; // skip SOI
            Span<byte> buf = stackalloc byte[4];

            while (fs.Position < fs.Length - 4)
            {
                if (fs.ReadByte() != 0xFF) break;
                int marker = fs.ReadByte();
                if (marker < 0) break;

                // Skip standalone markers
                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9))
                    continue;

                if (fs.Read(buf[..2]) < 2) break;
                int segLen = (buf[0] << 8) | buf[1];
                if (segLen < 2) break;

                long segStart = fs.Position;

                // APP2 = 0xE2.  Check for "MPF\0" signature.
                if (marker == 0xE2 && segLen >= 8)
                {
                    if (fs.Read(buf) >= 4 &&
                        buf[0] == 0x4D && buf[1] == 0x50 &&
                        buf[2] == 0x46 && buf[3] == 0x00)
                    {
                        // MPF header starts right after "MPF\0".
                        var mpfBase = fs.Position;
                        ParseMpfEntries(fs, mpfBase, entries);
                    }
                }

                // SOS — stop scanning APP segments.
                if (marker == 0xDA) break;

                fs.Position = segStart + segLen - 2;
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[JpegEmbeddedArtifactScanner] MPF scan failed: {ex.GetType().Name}: {ex.Message}"); }

        return entries;
    }

    private static void ParseMpfEntries(FileStream fs, long mpfBase, List<(long Offset, long Size)> entries)
    {
        Span<byte> buf = stackalloc byte[16];

        // Read endianness: "II" = little-endian, "MM" = big-endian.
        if (fs.Read(buf[..4]) < 4) return;
        bool littleEndian = buf[0] == 0x49 && buf[1] == 0x49;

        int Read16(Span<byte> b, int off) => littleEndian
            ? b[off] | (b[off + 1] << 8)
            : (b[off] << 8) | b[off + 1];

        int Read32(Span<byte> b, int off) => littleEndian
            ? b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24)
            : (b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3];

        // Offset to IFD0 (relative to mpfBase).
        var ifdOffset = Read32(buf, 2);
        fs.Position = mpfBase + ifdOffset;

        // IFD0: tag count (2 bytes), then N×12-byte tag entries.
        if (fs.Read(buf[..2]) < 2) return;
        int tagCount = Read16(buf, 0);

        int mpEntryCount  = 0;
        long mpEntryOffset = 0;

        for (int i = 0; i < tagCount && i < 20; i++)
        {
            if (fs.Read(buf[..12]) < 12) return;
            int tag = Read16(buf, 0);

            if (tag == 0xB001) // MPEntry version / number of images
                mpEntryCount = Read32(buf, 8);
            else if (tag == 0xB002) // MP Entry offset (relative to mpfBase)
                mpEntryOffset = Read32(buf, 8);
        }

        if (mpEntryCount < 2 || mpEntryOffset <= 0) return;

        fs.Position = mpfBase + mpEntryOffset;

        // Each MP Entry is 16 bytes: 4 attr, 4 size, 4 offset, 2 dep1, 2 dep2.
        // Entry 0 is the main image (offset 0) — skip it.
        for (int i = 0; i < mpEntryCount && i < 16; i++)
        {
            if (fs.Read(buf[..16]) < 16) return;

            var size   = (long)(uint)Read32(buf, 4);
            var offset = (long)(uint)Read32(buf, 8);

            // Entry 0 is the main image; secondary entries have non-zero offsets.
            // Offsets are relative to the start of the file (absolute).
            if (i > 0 && offset > 0)
                entries.Add((offset, size));
        }
    }

    // Finds the position of the FF D9 (EOI) that ends the main JPEG image.
    //
    // Walks the JPEG marker structure forward rather than doing a naive backward
    // scan.  Handles both baseline (one SOS) and progressive (multiple SOS) JPEG:
    //   1. Skip SOI (already verified by the caller).
    //   2. Parse each marker/segment length and skip the body.
    //   3. After each SOS, enter entropy-data mode and scan byte-by-byte:
    //        FF 00         — byte-stuffed FF, not a marker, continue
    //        FF D0–D7      — restart marker, continue
    //        FF D9         — true EOI, return its position
    //        FF <anything> — end of this entropy block; exit entropy mode and
    //                         parse the marker as a normal segment (e.g. another
    //                         SOS in a progressive JPEG) before re-entering entropy
    //                         mode.  Prevents false EOI hits on progressive files.
    private static long FindMainImageEoi(FileStream fs)
    {
        fs.Position = 2;
        Span<byte> buf = stackalloc byte[2];
        bool inEntropy = false;

        while (fs.Position < fs.Length - 1)
        {
            if (inEntropy)
            {
                int eb = fs.ReadByte();
                if (eb < 0) return -1;
                if (eb != 0xFF) continue;

                int en = fs.ReadByte();
                if (en < 0) return -1;

                if (en == 0x00) continue;                     // byte-stuffed
                if (en >= 0xD0 && en <= 0xD7) continue;       // restart marker
                if (en == 0xD9) return fs.Position - 2;       // true EOI

                // Non-stuffed, non-restart, non-EOI: end of this entropy block.
                // Switch back to marker mode and parse the segment normally so
                // progressive JPEGs (multiple SOS blocks) are handled correctly.
                inEntropy = false;

                // Standalone markers have no length field.
                if (en == 0x01 || (en >= 0xD0 && en <= 0xD8))
                    continue;

                if (fs.Read(buf) < 2) return -1;
                int segLen2 = (buf[0] << 8) | buf[1];
                if (segLen2 < 2) return -1;
                fs.Position += segLen2 - 2;
                if (en == 0xDA) inEntropy = true; // another SOS — progressive scan
                continue;
            }

            // Marker mode: expect FF xx.
            if (fs.ReadByte() != 0xFF)
                return -1;

            int marker;
            do { marker = fs.ReadByte(); }
            while (marker == 0xFF && fs.Position < fs.Length);

            if (marker < 0) return -1;
            if (marker == 0xD9) return fs.Position - 2; // EOI
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue;

            if (marker == 0xDA) // SOS
            {
                if (fs.Read(buf) < 2) return -1;
                int sosLen = (buf[0] << 8) | buf[1];
                fs.Position += sosLen - 2;
                inEntropy = true;
                continue;
            }

            if (fs.Read(buf) < 2) return -1;
            int segLen = (buf[0] << 8) | buf[1];
            if (segLen < 2) return -1;
            fs.Position += segLen - 2;
        }

        return -1;
    }

    // Checks XMP metadata for Google GDepth / GImage / GainMap namespace declarations.
    // Returns flags indicating *what* types were declared — the caller correlates
    // these with the trailing binary data to produce extractable artifacts.
    [Flags]
    private enum XmpDeclaredTypes
    {
        None         = 0,
        DepthMap     = 1 << 0,
        GainMap      = 1 << 1,
        SecondaryImage = 1 << 2,
        MotionPhoto  = 1 << 3
    }

    private static XmpDeclaredTypes ScanXmpDeclarations(string path)
    {
        var flags = XmpDeclaredTypes.None;

        try
        {
            var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(path);
            var xmpDir = directories
                .OfType<MetadataExtractor.Formats.Xmp.XmpDirectory>()
                .FirstOrDefault();

            var xmpMeta = xmpDir?.XmpMeta;
            if (xmpMeta is null) return flags;

            // Targeted namespace checks — fast and reliable.
            const string NsGDepth  = "http://ns.google.com/photos/1.0/depthmap/";
            const string NsGImage  = "http://ns.google.com/photos/1.0/image/";
            const string NsGCamera = "http://ns.google.com/photos/1.0/camera/";
            const string NsHdrgm   = "http://ns.adobe.com/hdr-gain-map/1.0/";

            // Check by property existence in known namespaces.
            if (XmpHas(xmpMeta, NsGDepth, "GDepth:Format") ||
                XmpHas(xmpMeta, NsGDepth, "Format"))
                flags |= XmpDeclaredTypes.DepthMap;

            if (XmpHas(xmpMeta, NsGImage, "GImage:Mime") ||
                XmpHas(xmpMeta, NsGImage, "Mime"))
                flags |= XmpDeclaredTypes.SecondaryImage;

            if (XmpHas(xmpMeta, NsHdrgm, "Version"))
                flags |= XmpDeclaredTypes.GainMap;

            if (XmpHas(xmpMeta, NsGCamera, "GCamera:MotionPhoto") ||
                XmpHas(xmpMeta, NsGCamera, "MotionPhoto"))
                flags |= XmpDeclaredTypes.MotionPhoto;

            // Fallback: walk all properties for non-standard namespaces
            // (Samsung, Huawei, etc.).
            if (flags == XmpDeclaredTypes.None)
            {
                foreach (var prop in xmpMeta.Properties)
                {
                    var p = prop.Path;
                    if (p is null) continue;

                    if (p.Contains("Depth", StringComparison.OrdinalIgnoreCase))
                        flags |= XmpDeclaredTypes.DepthMap;
                    else if (p.Contains("GImage", StringComparison.OrdinalIgnoreCase))
                        flags |= XmpDeclaredTypes.SecondaryImage;
                    else if (p.Contains("GainMap", StringComparison.OrdinalIgnoreCase) ||
                             p.Contains("hdrgm", StringComparison.OrdinalIgnoreCase))
                        flags |= XmpDeclaredTypes.GainMap;
                    else if (p.Contains("MotionPhoto", StringComparison.OrdinalIgnoreCase) ||
                             p.Contains("MicroVideo", StringComparison.OrdinalIgnoreCase))
                        flags |= XmpDeclaredTypes.MotionPhoto;
                }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[JpegEmbeddedArtifactScanner] XMP declaration scan failed for '{path}': {ex.GetType().Name}: {ex.Message}"); }

        return flags;
    }

    private static bool XmpHas(XmpCore.IXmpMeta xmp, string ns, string name)
    {
        try { return xmp.DoesPropertyExist(ns, name); }
        catch { return false; }
    }

    // Converts an EmbeddedArtifactType to the corresponding XmpDeclaredTypes flag.
    private static XmpDeclaredTypes TypeToFlag(EmbeddedArtifactType type) => type switch
    {
        EmbeddedArtifactType.DepthMap         => XmpDeclaredTypes.DepthMap,
        EmbeddedArtifactType.GainMap          => XmpDeclaredTypes.GainMap,
        EmbeddedArtifactType.SecondaryImage   => XmpDeclaredTypes.SecondaryImage,
        EmbeddedArtifactType.MotionPhotoVideo => XmpDeclaredTypes.MotionPhoto,
        _ => XmpDeclaredTypes.None
    };

    // Emits XMP-only declaration artifacts for types that weren't matched to
    // trailing binary data or base64 extraction.  Skips types that already
    // have an extractable artifact in results.
    private static void AddXmpOnlyDeclarations(XmpDeclaredTypes flags, List<EmbeddedArtifact> results)
    {
        if (flags.HasFlag(XmpDeclaredTypes.DepthMap) &&
            !results.Exists(a => a.Type == EmbeddedArtifactType.DepthMap && a.IsExtractable))
            results.Add(new EmbeddedArtifact
            {
                Id          = Guid.NewGuid(),
                Type        = EmbeddedArtifactType.DepthMap,
                DisplayName = "Depth map (XMP declared, not yet extracted)",
                MimeType    = "image/jpeg",
                ParseConfidence = new ConfidenceScore(70)
            });

        if (flags.HasFlag(XmpDeclaredTypes.GainMap) &&
            !results.Exists(a => a.Type == EmbeddedArtifactType.GainMap && a.IsExtractable))
            results.Add(new EmbeddedArtifact
            {
                Id          = Guid.NewGuid(),
                Type        = EmbeddedArtifactType.GainMap,
                DisplayName = "HDR gain map (XMP declared, not yet extracted)",
                MimeType    = "image/jpeg",
                ParseConfidence = new ConfidenceScore(70)
            });

        if (flags.HasFlag(XmpDeclaredTypes.SecondaryImage) &&
            !results.Exists(a => a.Type == EmbeddedArtifactType.SecondaryImage && a.IsExtractable))
            results.Add(new EmbeddedArtifact
            {
                Id          = Guid.NewGuid(),
                Type        = EmbeddedArtifactType.SecondaryImage,
                DisplayName = "Secondary image (XMP declared)",
                MimeType    = "image/jpeg",
                ParseConfidence = new ConfidenceScore(65)
            });

        if (flags.HasFlag(XmpDeclaredTypes.MotionPhoto) &&
            !results.Exists(a => a.Type == EmbeddedArtifactType.MotionPhotoVideo && a.IsExtractable))
            results.Add(new EmbeddedArtifact
            {
                Id          = Guid.NewGuid(),
                Type        = EmbeddedArtifactType.MotionPhotoVideo,
                DisplayName = "Motion photo video (XMP declared, not located)",
                MimeType    = "video/mp4",
                ParseConfidence = new ConfidenceScore(65)
            });
    }
}
