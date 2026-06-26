using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ArtifactView.App.Viewing;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.ThumbCache;

namespace ArtifactView.App.Views;

public partial class CompareWindow : Window
{
    public CompareWindow(MediaEntityRow left, MediaEntityRow right)
    {
        InitializeComponent();

        Title = $"Compare  —  {left.DisplayName}  vs.  {right.DisplayName}";

        LoadPanel(left,  LeftImage,  LeftName,  LeftMeta);
        LoadPanel(right, RightImage, RightName, RightMeta);
    }

    private static void LoadPanel(
        MediaEntityRow row,
        System.Windows.Controls.Image image,
        System.Windows.Controls.TextBlock nameLabel,
        System.Windows.Controls.TextBlock metaLabel)
    {
        nameLabel.Text = row.DisplayName;

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(row.ResolutionText))  parts.Add(row.ResolutionText);
        if (!string.IsNullOrEmpty(row.PreferredDateText)) parts.Add(row.PreferredDateText);
        if (!string.IsNullOrEmpty(row.CameraModel))     parts.Add(row.CameraModel);
        if (!string.IsNullOrEmpty(row.FileSizeText))    parts.Add(row.FileSizeText);
        if (!string.IsNullOrEmpty(row.PresenceState) && row.PresenceState != "Present")
            parts.Add($"[{row.PresenceState}]");
        metaLabel.Text = string.Join("  ·  ", parts);

        image.Source = LoadBitmap(row);
    }

    private static BitmapSource? LoadBitmap(MediaEntityRow row)
    {
        // Live file — decode directly
        if (File.Exists(row.LogicalPath))
        {
            try
            {
                return ImageDecoder.Decode(row.LogicalPath);
            }
            catch { }
        }

        // Ghost — try to recover from Thumbs.db payload
        if (!string.IsNullOrEmpty(row.ThumbsDbPath) && !string.IsNullOrEmpty(row.ThumbsDbStreamName))
        {
            try
            {
                var entries = ThumbsDbReader.ReadEntries(row.ThumbsDbPath);
                var entry   = entries.FirstOrDefault(e => e.StreamName == row.ThumbsDbStreamName);
                if (entry is not null)
                {
                    var payload = ThumbsDbReader.ExtractPayload(row.ThumbsDbPath, entry);
                    if (payload is { Length: > 0 })
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.StreamSource = new MemoryStream(payload);
                        bmp.CacheOption  = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }
                }
            }
            catch { }
        }

        return null;
    }
}
