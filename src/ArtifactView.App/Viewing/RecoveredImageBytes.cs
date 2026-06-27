using System.IO;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Sources.DiskImage;

namespace ArtifactView.App.Viewing;

// Resolves the raw bytes for rows that have no host-filesystem path: carved artifacts,
// live files inside a disk image, and deleted (NTFS/FAT/exFAT) files. Centralises the
// dispatch used by both the main viewer and the background filmstrip-thumbnail loader.
public static class RecoveredImageBytes
{
    public static bool HasByteSource(MediaEntityRow row) =>
        (!string.IsNullOrEmpty(row.CarvedImagePath) && row.CarvedLength > 0)
        || (row.PresenceState == "Deleted" && (row.DeletedMftRecordNumber >= 0 || row.DeletedFatStartCluster >= 0))
        || !string.IsNullOrEmpty(row.DiskImageInternalPath);

    // Returns the file bytes for a byte-source row, or null when unavailable/not applicable.
    public static byte[]? TryGet(MediaEntityRow row)
    {
        try
        {
            // Carved artifact: a byte range within the image.
            if (!string.IsNullOrEmpty(row.CarvedImagePath) && row.CarvedLength > 0)
            {
                if (row.CarvedLength > int.MaxValue) return null;
                var buf = new byte[(int)row.CarvedLength];
                using var fs = new FileStream(row.CarvedImagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(row.CarvedOffset, SeekOrigin.Begin);
                return ReadExactly(fs, buf) ? buf : null;
            }

            // Deleted files (NTFS via MFT record, FAT/exFAT via start cluster).
            if (row.PresenceState == "Deleted")
            {
                if (row.DeletedMftRecordNumber >= 0)
                    return DiskImagePartitionReader.ReadDeletedFileBytes(
                        row.DiskImagePath, row.DiskImagePartitionIndex, row.DeletedMftRecordNumber);

                if (row.DeletedFatStartCluster >= 0)
                    return row.DiskImageFilesystem == "exFAT"
                        ? DiskImagePartitionReader.ReadDeletedExFatFileBytes(
                            row.DiskImagePath, row.DiskImagePartitionIndex, row.DeletedFatStartCluster, row.FileSizeBytes)
                        : DiskImagePartitionReader.ReadDeletedFatFileBytes(
                            row.DiskImagePath, row.DiskImagePartitionIndex, row.DeletedFatStartCluster, row.FileSizeBytes);

                return null;
            }

            // Live file inside a disk image.
            if (!string.IsNullOrEmpty(row.DiskImageInternalPath))
                return DiskImagePartitionReader.ReadFileBytes(
                    row.DiskImagePath, row.DiskImagePartitionIndex, row.DiskImageInternalPath, row.DiskImageFilesystem);

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool ReadExactly(Stream s, byte[] buf)
    {
        int total = 0;
        while (total < buf.Length)
        {
            int n = s.Read(buf, total, buf.Length - total);
            if (n == 0) return false;
            total += n;
        }
        return true;
    }
}
