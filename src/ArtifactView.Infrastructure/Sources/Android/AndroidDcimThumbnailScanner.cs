using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using IO = System.IO;

namespace ArtifactView.Infrastructure.Sources.Android;

// Reads JPEG thumbnails from an Android DCIM/.thumbnails/ directory.
// Android stores two sizes: MICRO_KIND (96×96) and MINI_KIND (512×384).
// Files are typically named <media_id>.jpg where media_id is the MediaStore row ID.
// Some Android versions also write the original filename into EXIF ImageDescription.
public static class AndroidDcimThumbnailScanner
{
    private static readonly string[] s_thumbDirNames =
        ["thumbnails", ".thumbnails", "Thumbnails", ".Thumbnails"];

    // Returns the path of the thumbnails subdirectory, or null if none found.
    public static string? FindThumbnailsDir(string parentFolder)
    {
        foreach (var name in s_thumbDirNames)
        {
            var candidate = IO.Path.Combine(parentFolder, name);
            if (IO.Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    // Scans all JPEG files in the thumbnails directory and extracts available metadata.
    public static IReadOnlyList<AndroidThumbnailEntry> Scan(string thumbnailsDir)
    {
        if (!IO.Directory.Exists(thumbnailsDir)) return [];

        var results = new List<AndroidThumbnailEntry>();
        IEnumerable<string> files;
        try { files = IO.Directory.EnumerateFiles(thumbnailsDir, "*.jpg", IO.SearchOption.TopDirectoryOnly); }
        catch { return []; }

        foreach (var file in files)
        {
            try
            {
                var entry = ReadEntry(file);
                if (entry is not null)
                    results.Add(entry);
            }
            catch { }
        }

        return results;
    }

    private static AndroidThumbnailEntry? ReadEntry(string path)
    {
        int    width   = 0;
        int    height  = 0;
        string? originalFilename = null;
        DateTime? dateUtc = null;

        try
        {
            var dirs = ImageMetadataReader.ReadMetadata(path);

            foreach (var dir in dirs)
            {
                if (dir is MetadataExtractor.Formats.Jpeg.JpegDirectory jpegDir)
                {
                    width  = jpegDir.GetImageWidth();
                    height = jpegDir.GetImageHeight();
                }
                else if (dir is ExifIfd0Directory exif0)
                {
                    // Some Android ROMs write the original filename into ImageDescription.
                    if (exif0.ContainsTag(ExifDirectoryBase.TagImageDescription))
                    {
                        var desc = exif0.GetDescription(ExifDirectoryBase.TagImageDescription);
                        if (!string.IsNullOrWhiteSpace(desc) && desc.Contains('.'))
                            originalFilename = IO.Path.GetFileName(desc.Trim());
                    }

                    if (exif0.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var dt))
                        dateUtc = dt.ToUniversalTime();
                }
                else if (dir is ExifSubIfdDirectory subIfd)
                {
                    if (dateUtc is null &&
                        subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
                        dateUtc = dt.ToUniversalTime();
                }
            }
        }
        catch { }

        // Fall back to filesystem date when EXIF is absent.
        if (dateUtc is null)
        {
            try { dateUtc = IO.File.GetLastWriteTimeUtc(path); } catch { }
        }

        // Try to infer original filename from the thumbnail filename pattern.
        // Android's MINI_KIND thumbnails are sometimes named after the original sans extension.
        if (originalFilename is null)
            originalFilename = TryInferFilename(IO.Path.GetFileNameWithoutExtension(path));

        return new AndroidThumbnailEntry(path, originalFilename, dateUtc, width, height);
    }

    // Android MediaStore IDs are long integers. If the stem is numeric-only we
    // can't infer the original filename. Non-numeric stems may be the original name.
    private static string? TryInferFilename(string stem)
    {
        if (string.IsNullOrEmpty(stem)) return null;
        // Pure numeric → MediaStore ID, no filename info.
        if (long.TryParse(stem, out _)) return null;
        // Contains image-like characters (IMG_, DSC_, VID_, etc.) — treat as filename.
        return stem + ".jpg";
    }
}
