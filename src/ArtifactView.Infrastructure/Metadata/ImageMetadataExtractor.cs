using ArtifactView.Core.Models;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.Xmp;
using System.Globalization;

namespace ArtifactView.Infrastructure.Metadata;

// Reads all raw metadata tags from an image file using MetadataExtractor.
// Returns both the flat tag list (for the Metadata tab) and a key-field summary
// (for quick grid enrichment and findings analysis).
// Does not add 'using MetadataExtractor' to avoid ambiguity with System.IO.Directory.
public sealed class ImageMetadataExtractor
{
    public (IReadOnlyList<RawMetadataEntry> Entries, ExifSummary Summary) Extract(string path)
    {
        var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(path);

        var entries = new List<RawMetadataEntry>();
        foreach (var dir in directories)
        {
            // XmpDirectory stores properties in XmpMeta, not in Tags.
            // Tags only contains a single "XMP Value Count" entry.
            if (dir is XmpDirectory xmpDir)
            {
                EnumerateXmpProperties(xmpDir, entries);
                continue;
            }

            foreach (var tag in dir.Tags)
                entries.Add(new RawMetadataEntry(dir.Name, tag.Name, tag.Description));
        }

        var summary = BuildSummary(directories);
        return (entries, summary);
    }

    // Enumerates all XMP properties via the IXmpMeta interface and adds
    // them as RawMetadataEntry items so they appear in the Metadata tab.
    private static void EnumerateXmpProperties(XmpDirectory xmpDir, List<RawMetadataEntry> entries)
    {
        var xmpMeta = xmpDir.XmpMeta;
        if (xmpMeta is null) return;

        try
        {
            foreach (var prop in xmpMeta.Properties)
            {
                // Skip schema-only nodes (no value, just namespace declarations).
                if (string.IsNullOrEmpty(prop.Value)) continue;

                var ns   = prop.Namespace ?? string.Empty;
                var path = prop.Path      ?? string.Empty;

                // Derive a readable directory name from the namespace.
                var dirName = ns switch
                {
                    _ when ns.Contains("depthmap", StringComparison.OrdinalIgnoreCase) => "XMP GDepth",
                    _ when ns.Contains("google.com/photos/1.0/image", StringComparison.OrdinalIgnoreCase) => "XMP GImage",
                    _ when ns.Contains("google.com/photos/1.0/camera", StringComparison.OrdinalIgnoreCase) => "XMP GCamera",
                    _ when ns.Contains("hdr-gain-map", StringComparison.OrdinalIgnoreCase) => "XMP HDR Gain Map",
                    _ when ns.Contains("xap/1.0", StringComparison.OrdinalIgnoreCase) => "XMP Core",
                    _ when ns.Contains("dc/elements", StringComparison.OrdinalIgnoreCase) => "XMP Dublin Core",
                    _ when ns.Contains("photoshop", StringComparison.OrdinalIgnoreCase) => "XMP Photoshop",
                    _ when ns.Contains("tiff", StringComparison.OrdinalIgnoreCase) => "XMP TIFF",
                    _ when ns.Contains("exif", StringComparison.OrdinalIgnoreCase) => "XMP EXIF",
                    _ => "XMP"
                };

                entries.Add(new RawMetadataEntry(dirName, path, prop.Value));
            }
        }
        catch
        {
            // Best-effort: some XMP structures may throw during enumeration.
        }
    }

    // Returns the raw embedded EXIF thumbnail bytes without decoding or
    // re-encoding.  The result is a bit-exact copy of the JPEG (or other
    // format) stored in IFD1.  Returns null when no thumbnail is present.
    public static byte[]? ExtractThumbnailBytes(string path)
    {
        var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(path);
        var thumbDir    = directories.OfType<ExifThumbnailDirectory>().FirstOrDefault();
        if (thumbDir is null)
            return null;

        // AdjustedThumbnailOffset gives the absolute file position of the
        // thumbnail payload.  TagThumbnailLength holds the byte count.
        var offset = thumbDir.AdjustedThumbnailOffset;
        var lengthObj = thumbDir.GetObject(ExifThumbnailDirectory.TagThumbnailLength);
        if (offset is null || lengthObj is null)
            return null;

        int length;
        try   { length = Convert.ToInt32(lengthObj); }
        catch { return null; }
        if (length <= 0)
            return null;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (offset.Value + length > fs.Length)
            return null;

        fs.Position = offset.Value;
        var buffer  = new byte[length];
        var read    = fs.ReadAtLeast(buffer, length, throwOnEndOfStream: false);
        return read == length ? buffer : null;
    }

    private static ExifSummary BuildSummary(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var exifSub  = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var exif0    = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var jpeg     = directories.OfType<JpegDirectory>().FirstOrDefault();
        var gpsDir   = directories.OfType<GpsDirectory>().FirstOrDefault();
        var png      = directories.OfType<PngDirectory>().FirstOrDefault();
        var thumbDir = directories.OfType<ExifThumbnailDirectory>().FirstOrDefault();

        // Dimensions: read raw stored objects and convert to int.
        // GetDescription() must NOT be used here — MetadataExtractor 2.8.1 appends
        // " pixels" to EXIF dimension tags (e.g. "4032 pixels"), which breaks parsing.
        int? width = null, height = null;

        if (exifSub is not null)
        {
            width  = Dim(exifSub, ExifSubIfdDirectory.TagExifImageWidth);
            height = Dim(exifSub, ExifSubIfdDirectory.TagExifImageHeight);
        }
        // JPEG SOF dimensions — present on every valid JPEG, even without EXIF.
        if (width is null && jpeg is not null)
        {
            width  = Dim(jpeg, JpegDirectory.TagImageWidth);
            height = Dim(jpeg, JpegDirectory.TagImageHeight);
        }
        // PNG IHDR dimensions.
        if (width is null && png is not null)
        {
            width  = Dim(png, PngDirectory.TagImageWidth);
            height = Dim(png, PngDirectory.TagImageHeight);
        }

        // EXIF dates use "yyyy:MM:dd HH:mm:ss" — non-standard, needs TryParseExact.
        static DateTime? ParseExifDate(string? raw) =>
            raw is not null &&
            DateTime.TryParseExact(raw, "yyyy:MM:dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

        var captureDate      = ParseExifDate(exifSub?.GetDescription(ExifSubIfdDirectory.TagDateTimeOriginal));
        var dateTimeDigitized = ParseExifDate(exifSub?.GetDescription(ExifSubIfdDirectory.TagDateTimeDigitized));
        // IFD0 DateTime is the metadata-write timestamp — different from DateTimeOriginal.
        var dateTimeModified  = ParseExifDate(exif0?.GetDescription(ExifIfd0Directory.TagDateTime));

        var make   = exif0?.GetDescription(ExifIfd0Directory.TagMake)?.Trim();
        var model  = exif0?.GetDescription(ExifIfd0Directory.TagModel)?.Trim();
        var camera = (make, model) switch
        {
            (not null, not null) when model!.StartsWith(make!) => model,
            (not null, not null)                               => $"{make} {model}",
            (null,     not null)                               => model,
            (not null, null    )                               => make,
            _                                                  => null
        };

        // GPS: GetGeoLocation() parses the GPS IFD into decimal degrees.
        // Use var to avoid referencing MetadataExtractor.GeoLocation by name
        // (would require 'using MetadataExtractor' which conflicts with System.IO.Directory).
        string? gpsText = null;
        double? gpsLat = null, gpsLon = null;
        var location = gpsDir?.GetGeoLocation();
        if (location is not null && !location.IsZero)
        {
            gpsLat = location.Latitude;
            gpsLon = location.Longitude;
            var latDir = gpsLat >= 0 ? "N" : "S";
            var lonDir = gpsLon >= 0 ? "E" : "W";
            gpsText = $"{Math.Abs(gpsLat.Value):F5}\u00b0{latDir}, {Math.Abs(gpsLon.Value):F5}\u00b0{lonDir}";
        }

        // GPS timestamp is always UTC. TryGetGpsDate combines GPSDateStamp + GPSTimeStamp.
        DateTime? gpsDateTimeUtc = null;
        if (gpsDir is not null && gpsDir.TryGetGpsDate(out var gpsDate))
            gpsDateTimeUtc = DateTime.SpecifyKind(gpsDate, DateTimeKind.Utc);

        var software = exif0?.GetDescription(ExifIfd0Directory.TagSoftware)?.Trim();
        if (string.IsNullOrEmpty(software)) software = null;

        // ── Capture parameters ──────────────────────────────────────────────
        var lensModel = exifSub?.GetDescription(ExifSubIfdDirectory.TagLensModel)?.Trim();
        if (string.IsNullOrEmpty(lensModel))
            lensModel = exifSub?.GetDescription(ExifSubIfdDirectory.TagLensMake)?.Trim();
        if (string.IsNullOrEmpty(lensModel)) lensModel = null;

        var focalLength = exifSub?.GetDescription(ExifSubIfdDirectory.TagFocalLength);
        if (string.IsNullOrEmpty(focalLength)) focalLength = null;

        var fNumber = exifSub?.GetDescription(ExifSubIfdDirectory.TagFNumber);
        if (string.IsNullOrEmpty(fNumber)) fNumber = null;

        var exposureTime = exifSub?.GetDescription(ExifSubIfdDirectory.TagExposureTime);
        if (string.IsNullOrEmpty(exposureTime)) exposureTime = null;

        int? isoSpeed = exifSub is not null ? Dim(exifSub, ExifSubIfdDirectory.TagIsoEquivalent) : null;

        // ── Display / color ─────────────────────────────────────────────────
        var orientation = exif0 is not null ? Dim(exif0, ExifIfd0Directory.TagOrientation) : null;

        var colorSpace = exifSub?.GetDescription(ExifSubIfdDirectory.TagColorSpace);
        if (string.IsNullOrEmpty(colorSpace)) colorSpace = null;

        // ── Provenance ──────────────────────────────────────────────────────
        var copyright = exif0?.GetDescription(ExifIfd0Directory.TagCopyright)?.Trim();
        if (string.IsNullOrEmpty(copyright)) copyright = null;

        var artist = exif0?.GetDescription(ExifIfd0Directory.TagArtist)?.Trim();
        if (string.IsNullOrEmpty(artist)) artist = null;

        // ── Embedded thumbnail (IFD1) dimensions and byte count ──────────
        // Read from the already-parsed ExifThumbnailDirectory — no extra file I/O.
        int? thumbW = null, thumbH = null, thumbBytes = null;
        if (thumbDir is not null)
        {
            thumbW     = Dim(thumbDir, ExifDirectoryBase.TagImageWidth)
                      ?? Dim(thumbDir, ExifDirectoryBase.TagExifImageWidth);
            thumbH     = Dim(thumbDir, ExifDirectoryBase.TagImageHeight)
                      ?? Dim(thumbDir, ExifDirectoryBase.TagExifImageHeight);
            thumbBytes = Dim(thumbDir, ExifThumbnailDirectory.TagThumbnailLength);
        }

        // ── XMP metadata ────────────────────────────────────────────────────
        // XmpDirectory.Tags only contains "XMP Value Count" — individual XMP
        // properties live in the XmpMeta (IXmpMeta) object. Use targeted
        // GetPropertyString calls with the correct namespace URIs.

        const string NsXmp     = "http://ns.adobe.com/xap/1.0/";
        const string NsGDepth  = "http://ns.google.com/photos/1.0/depthmap/";
        const string NsGCamera = "http://ns.google.com/photos/1.0/camera/";
        const string NsHdrgm   = "http://ns.adobe.com/hdr-gain-map/1.0/";

        string? xmpCreatorTool = null;
        DateTime? xmpCreateDate = null, xmpModifyDate = null;
        string? depthFormat = null, depthNear = null, depthFar = null, depthMime = null;
        string? gainMapVersion = null;
        bool isMotionPhoto = false;
        string? motionPhotoVersion = null;

        var xmpDir = directories.OfType<XmpDirectory>().FirstOrDefault();
        var xmpMeta = xmpDir?.XmpMeta;
        if (xmpMeta is not null)
        {
            try
            {
                // XMP core
                xmpCreatorTool = XmpStr(xmpMeta, NsXmp, "CreatorTool");
                xmpCreateDate  = ParseXmpDate(XmpStr(xmpMeta, NsXmp, "CreateDate"));
                xmpModifyDate  = ParseXmpDate(XmpStr(xmpMeta, NsXmp, "ModifyDate"));

                // Google GDepth (depth map)
                depthFormat = XmpStr(xmpMeta, NsGDepth, "GDepth:Format");
                depthNear   = XmpStr(xmpMeta, NsGDepth, "GDepth:Near");
                depthFar    = XmpStr(xmpMeta, NsGDepth, "GDepth:Far");
                depthMime   = XmpStr(xmpMeta, NsGDepth, "GDepth:Mime");

                // Try without prefix — some writers omit the namespace prefix.
                depthFormat ??= XmpStr(xmpMeta, NsGDepth, "Format");
                depthNear   ??= XmpStr(xmpMeta, NsGDepth, "Near");
                depthFar    ??= XmpStr(xmpMeta, NsGDepth, "Far");
                depthMime   ??= XmpStr(xmpMeta, NsGDepth, "Mime");

                // HDR gain map
                gainMapVersion = XmpStr(xmpMeta, NsHdrgm, "Version");

                // Google Camera: motion photo
                var mpFlag = XmpStr(xmpMeta, NsGCamera, "GCamera:MotionPhoto");
                mpFlag ??= XmpStr(xmpMeta, NsGCamera, "MotionPhoto");
                isMotionPhoto = mpFlag is "1" or "true" or "True";

                motionPhotoVersion = XmpStr(xmpMeta, NsGCamera, "GCamera:MotionPhotoVersion");
                motionPhotoVersion ??= XmpStr(xmpMeta, NsGCamera, "MotionPhotoVersion");

                // Fallback: walk all properties for non-standard namespaces
                // (Samsung, Huawei, etc. may use different namespace URIs).
                if (depthFormat is null)
                {
                    foreach (var prop in xmpMeta.Properties)
                    {
                        var p = prop.Path;
                        var v = prop.Value;
                        if (p is null || string.IsNullOrEmpty(v)) continue;

                        if (p.Contains("Depth", StringComparison.OrdinalIgnoreCase))
                        {
                            if (p.Contains("Format", StringComparison.OrdinalIgnoreCase))
                                depthFormat ??= v;
                            else if (p.Contains("Near", StringComparison.OrdinalIgnoreCase))
                                depthNear ??= v;
                            else if (p.Contains("Far", StringComparison.OrdinalIgnoreCase))
                                depthFar ??= v;
                            else if (p.Contains("Mime", StringComparison.OrdinalIgnoreCase))
                                depthMime ??= v;
                        }
                        else if (p.Contains("MotionPhoto", StringComparison.OrdinalIgnoreCase) ||
                                 p.Contains("MicroVideo", StringComparison.OrdinalIgnoreCase))
                        {
                            if (p.Contains("Version", StringComparison.OrdinalIgnoreCase))
                                motionPhotoVersion ??= v;
                            else if (v is "1" or "true" or "True")
                                isMotionPhoto = true;
                        }
                        else if (p.Contains("GainMap", StringComparison.OrdinalIgnoreCase) ||
                                 p.Contains("hdrgm", StringComparison.OrdinalIgnoreCase))
                        {
                            if (p.Contains("Version", StringComparison.OrdinalIgnoreCase))
                                gainMapVersion ??= v;
                        }
                    }
                }
            }
            catch { /* best-effort */ }
        }

        // Trim empty strings to null.
        static string? NullIfEmpty(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s;

        xmpCreatorTool = NullIfEmpty(xmpCreatorTool);
        depthFormat    = NullIfEmpty(depthFormat);
        depthNear      = NullIfEmpty(depthNear);
        depthFar       = NullIfEmpty(depthFar);
        depthMime      = NullIfEmpty(depthMime);

        return new ExifSummary
        {
            Width              = width,
            Height             = height,
            CaptureDate        = captureDate,
            DateTimeDigitized  = dateTimeDigitized,
            DateTimeModified   = dateTimeModified,
            CameraModel        = camera,
            GpsText            = gpsText,
            GpsLatitude        = gpsLat,
            GpsLongitude       = gpsLon,
            GpsDateTimeUtc     = gpsDateTimeUtc,
            SoftwareTag        = software,
            LensModel          = lensModel,
            FocalLength        = focalLength,
            FNumber            = fNumber,
            ExposureTime       = exposureTime,
            IsoSpeed           = isoSpeed,
            Orientation        = orientation,
            ColorSpace         = colorSpace,
            Copyright          = copyright,
            Artist             = artist,
            XmpCreatorTool     = xmpCreatorTool,
            XmpCreateDate      = xmpCreateDate,
            XmpModifyDate      = xmpModifyDate,
            DepthFormat        = depthFormat,
            DepthNear          = depthNear,
            DepthFar           = depthFar,
            DepthMime          = depthMime,
            GainMapVersion     = gainMapVersion,
            IsMotionPhoto      = isMotionPhoto,
            MotionPhotoVersion = motionPhotoVersion,
            ThumbnailWidth     = thumbW,
            ThumbnailHeight    = thumbH,
            ThumbnailByteCount = thumbBytes
        };
    }

    // XMP dates can be "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:sszzz", or EXIF-style.
    private static DateTime? ParseXmpDate(string? raw)
    {
        if (raw is null) return null;

        // Try ISO 8601 first, then EXIF format.
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces, out var d))
            return d;

        if (DateTime.TryParseExact(raw, "yyyy:MM:dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            return d;

        return null;
    }

    // GetObject returns the raw stored type (ushort, uint, int, long…).
    // Convert.ToInt32 handles all numeric IConvertible types without locale issues.
    private static int? Dim(MetadataExtractor.Directory dir, int tagType)
    {
        var obj = dir.GetObject(tagType);
        if (obj is null) return null;
        try   { return Convert.ToInt32(obj); }
        catch { return null; }
    }

    // Safe XMP property lookup — returns null when the property doesn't exist
    // or when XmpCore throws for invalid paths.
    private static string? XmpStr(XmpCore.IXmpMeta xmp, string ns, string name)
    {
        try
        {
            if (xmp.DoesPropertyExist(ns, name))
                return xmp.GetPropertyString(ns, name);
        }
        catch { /* property path invalid or not found */ }
        return null;
    }
}
