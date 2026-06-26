namespace ArtifactView.Infrastructure.Metadata;

// Key field values extracted from EXIF for quick display in the grid and findings pipeline.
public sealed class ExifSummary
{
    public int?      Width       { get; init; }
    public int?      Height      { get; init; }
    public string?   CameraModel { get; init; }

    // GPS: decimal-degree string ready for display ("47.60620°N, 122.33210°W"), or null.
    public string?   GpsText      { get; init; }
    // Raw decimal degrees — positive = North/East, negative = South/West.
    public double?   GpsLatitude  { get; init; }
    public double?   GpsLongitude { get; init; }

    // Raw EXIF Software field — used by SoftwareAnalyzer to detect editing tools.
    public string?   SoftwareTag { get; init; }

    // EXIF datetime fields — all stored as naive local datetimes (no timezone).
    // DateTimeOriginal: when the shutter fired. Should be immutable after capture.
    public DateTime? CaptureDate       { get; init; }

    // DateTimeDigitized: when the image was digitized. Usually equals CaptureDate
    // for camera-captured photos; divergence can indicate format conversion.
    public DateTime? DateTimeDigitized { get; init; }

    // IFD0 DateTime: when the EXIF metadata block was last written.
    // Updated by editing tools (Lightroom, Photoshop, exiftool, etc.) even when
    // DateTimeOriginal is left intact. Divergence from CaptureDate is a key signal.
    public DateTime? DateTimeModified  { get; init; }

    // ── Capture parameters ──────────────────────────────────────────────
    // Formatted display strings — ready for the Summary tab and report.
    public string?   LensModel    { get; init; }
    public string?   FocalLength  { get; init; }  // e.g. "50.0 mm"
    public string?   FNumber      { get; init; }  // e.g. "f/2.8"
    public string?   ExposureTime { get; init; }  // e.g. "1/125 s"
    public int?      IsoSpeed     { get; init; }

    // ── Display / color ─────────────────────────────────────────────────
    // EXIF Orientation tag (1–8). Null when absent. Used by the viewer to
    // auto-rotate the image for correct display without modifying pixels.
    public int?      Orientation  { get; init; }
    public string?   ColorSpace   { get; init; }  // e.g. "sRGB", "Uncalibrated"

    // ── Provenance ──────────────────────────────────────────────────────
    public string?   Copyright    { get; init; }
    public string?   Artist       { get; init; }

    // ── XMP metadata ────────────────────────────────────────────────────
    // CreatorTool is often more specific than IFD0 Software (e.g.
    // "Adobe Photoshop Lightroom Classic 13.5" vs "Adobe Lightroom").
    public string?   XmpCreatorTool { get; init; }

    // XMP-level timestamps — may differ from EXIF dates when editing
    // tools update XMP but leave EXIF untouched, or vice versa.
    public DateTime? XmpCreateDate  { get; init; }
    public DateTime? XmpModifyDate  { get; init; }

    // ── XMP depth map properties (Google GDepth / Samsung) ──────────────
    // Non-null when the image declares an embedded depth map.
    public string?   DepthFormat { get; init; }   // e.g. "RangeInverse", "RangeLinear"
    public string?   DepthNear   { get; init; }   // near plane distance
    public string?   DepthFar    { get; init; }   // far plane distance
    public string?   DepthMime   { get; init; }   // e.g. "image/jpeg"

    // ── XMP gain map (HDR) ──────────────────────────────────────────────
    public string?   GainMapVersion { get; init; }

    // ── XMP motion photo ────────────────────────────────────────────────
    public bool      IsMotionPhoto      { get; init; }
    public string?   MotionPhotoVersion { get; init; }

    // ── GPS timestamp ────────────────────────────────────────────────────
    // GPS timestamps are always UTC. Stored separately from EXIF capture date
    // (which is naive local time) so the analyzer can cross-check them correctly.
    // Null when no GPS date/time tags are present.
    public DateTime? GpsDateTimeUtc { get; init; }

    // ── Embedded EXIF thumbnail (IFD1) ──────────────────────────────────
    // Populated from the EXIF thumbnail IFD, without decoding the thumbnail.
    // Null dimensions mean no IFD1 thumbnail was found.
    public int?  ThumbnailWidth     { get; init; }
    public int?  ThumbnailHeight    { get; init; }
    public int?  ThumbnailByteCount { get; init; }
    public bool  HasThumbnail       => ThumbnailWidth.HasValue;
}
