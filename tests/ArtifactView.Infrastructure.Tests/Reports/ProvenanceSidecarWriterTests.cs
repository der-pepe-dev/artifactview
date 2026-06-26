using System.Text.Json;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Reports;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Reports;

public sealed class ProvenanceSidecarWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public ProvenanceSidecarWriterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string OutputPath(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Write_creates_sidecar_file_next_to_output()
    {
        var path = OutputPath("artifact.jpg");
        ProvenanceSidecarWriter.Write(path, new ProvenanceSidecar
        {
            ExportedAt             = "2026-01-01T00:00:00Z",
            SourceFile             = "test.jpg",
            SourcePresence         = "present",
            ExtractionSource       = "exif-thumbnail",
            ExtractionMethod       = "bit-copy",
            ReconstructionCategory = "exact-artifact-extraction",
            OutputFormat           = "image/jpeg",
            ByteCount              = 1024
        });

        Assert.True(File.Exists(path + ".provenance.json"));
    }

    [Fact]
    public void Write_sidecar_contains_valid_json()
    {
        var path = OutputPath("artifact.jpg");
        ProvenanceSidecarWriter.Write(path, new ProvenanceSidecar
        {
            ExportedAt             = "2026-01-01T00:00:00Z",
            SourceFile             = "test.jpg",
            SourcePresence         = "present",
            ExtractionSource       = "exif-thumbnail",
            ExtractionMethod       = "bit-copy",
            ReconstructionCategory = "exact-artifact-extraction",
            OutputFormat           = "image/jpeg",
            ByteCount              = 1024
        });

        var json = File.ReadAllText(path + ".provenance.json");
        var ex = Record.Exception(() => JsonDocument.Parse(json));
        Assert.Null(ex);
    }

    [Fact]
    public void Write_sidecar_uses_camel_case_property_names()
    {
        var path = OutputPath("artifact.jpg");
        ProvenanceSidecarWriter.Write(path, new ProvenanceSidecar
        {
            ExportedAt             = "2026-01-01T00:00:00Z",
            SourceFile             = "source.jpg",
            SourcePresence         = "present",
            ExtractionSource       = "exif-thumbnail",
            ExtractionMethod       = "bit-copy",
            ReconstructionCategory = "exact-artifact-extraction",
            OutputFormat           = "image/jpeg",
            ByteCount              = 512
        });

        var json = File.ReadAllText(path + ".provenance.json");
        Assert.Contains("\"sourceFile\"", json);
        Assert.Contains("\"extractionSource\"", json);
        Assert.Contains("\"byteCount\"", json);
        Assert.DoesNotContain("\"SourceFile\"", json);
    }

    [Fact]
    public void Write_sidecar_omits_null_optional_fields()
    {
        var path = OutputPath("artifact.jpg");
        ProvenanceSidecarWriter.Write(path, new ProvenanceSidecar
        {
            ExportedAt             = "2026-01-01T00:00:00Z",
            SourceFile             = "source.jpg",
            SourcePresence         = "present",
            ExtractionSource       = "exif-thumbnail",
            ExtractionMethod       = "bit-copy",
            ReconstructionCategory = "exact-artifact-extraction",
            OutputFormat           = "image/jpeg",
            ByteCount              = 512,
            Warning                = null,
            Notes                  = null
        });

        var json = File.ReadAllText(path + ".provenance.json");
        Assert.DoesNotContain("\"warning\"", json);
        Assert.DoesNotContain("\"notes\"", json);
    }

    [Fact]
    public void Write_sidecar_includes_warning_when_set()
    {
        var path = OutputPath("artifact.png");
        ProvenanceSidecarWriter.Write(path, new ProvenanceSidecar
        {
            ExportedAt             = "2026-01-01T00:00:00Z",
            SourceFile             = "source.jpg",
            SourcePresence         = "ghost",
            ExtractionSource       = "thumbs-db",
            ExtractionMethod       = "lo-fi-reconstruction",
            ReconstructionCategory = "lo-fi-reconstruction",
            OutputFormat           = "image/png",
            ByteCount              = 2048,
            Warning                = "Output is NOT the original. Low-fidelity reconstruction."
        });

        var json = File.ReadAllText(path + ".provenance.json");
        Assert.Contains("\"warning\"", json);
        Assert.Contains("Output is NOT the original", json);
    }

    [Fact]
    public void Write_sidecar_includes_contributors_list()
    {
        var path = OutputPath("artifact.jpg");
        ProvenanceSidecarWriter.Write(path, new ProvenanceSidecar
        {
            ExportedAt             = "2026-01-01T00:00:00Z",
            SourceFile             = "source.jpg",
            SourcePresence         = "present",
            ExtractionSource       = "thumbs-db",
            ExtractionMethod       = "bit-copy",
            ReconstructionCategory = "exact-artifact-extraction",
            OutputFormat           = "image/jpeg",
            ByteCount              = 800,
            Contributors           = ["Thumbs.db: /path/to/Thumbs.db", "Stream ID: 42"]
        });

        var json = File.ReadAllText(path + ".provenance.json");
        Assert.Contains("Thumbs.db", json);
        Assert.Contains("Stream ID: 42", json);
    }

    [Fact]
    public void Write_convenience_overload_creates_sidecar()
    {
        var path = OutputPath("artifact.jpg");
        ProvenanceSidecarWriter.Write(
            path,
            sourceFile:             "source.jpg",
            sourcePresence:         "present",
            extractionSource:       "exif-thumbnail",
            extractionMethod:       "bit-copy",
            reconstructionCategory: "exact-artifact-extraction",
            outputFormat:           "image/jpeg",
            byteCount:              1024);

        Assert.True(File.Exists(path + ".provenance.json"));
    }

    [Fact]
    public void Write_convenience_overload_serializes_exportedAt_as_iso8601()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var path   = OutputPath("artifact.jpg");
        ProvenanceSidecarWriter.Write(
            path,
            sourceFile:             "source.jpg",
            sourcePresence:         "present",
            extractionSource:       "exif-thumbnail",
            extractionMethod:       "bit-copy",
            reconstructionCategory: "exact-artifact-extraction",
            outputFormat:           "image/jpeg",
            byteCount:              512);
        var after = DateTime.UtcNow.AddSeconds(1);

        var json = File.ReadAllText(path + ".provenance.json");
        var doc  = JsonDocument.Parse(json);
        var exportedAt = doc.RootElement.GetProperty("exportedAt").GetString()!;
        var parsed = DateTime.Parse(exportedAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.InRange(parsed.ToUniversalTime(), before, after);
    }

    [Fact]
    public void Write_is_silent_on_invalid_path()
    {
        var badPath = Path.Combine("/nonexistent/path/that/cannot/exist", "artifact.jpg");
        var ex = Record.Exception(() =>
            ProvenanceSidecarWriter.Write(badPath, new ProvenanceSidecar
            {
                ExportedAt             = "2026-01-01T00:00:00Z",
                SourceFile             = "source.jpg",
                SourcePresence         = "present",
                ExtractionSource       = "exif-thumbnail",
                ExtractionMethod       = "bit-copy",
                ReconstructionCategory = "exact-artifact-extraction",
                OutputFormat           = "image/jpeg",
                ByteCount              = 0
            }));

        Assert.Null(ex);
    }

    [Fact]
    public void Write_sidecar_path_is_output_path_plus_provenance_json()
    {
        var path = OutputPath("my_export__lofi_reconstruction__thumbcache.png");
        ProvenanceSidecarWriter.Write(path, new ProvenanceSidecar
        {
            ExportedAt             = "2026-01-01T00:00:00Z",
            SourceFile             = "source.jpg",
            SourcePresence         = "present",
            ExtractionSource       = "thumbcache",
            ExtractionMethod       = "bit-copy",
            ReconstructionCategory = "exact-artifact-extraction",
            OutputFormat           = "image/png",
            ByteCount              = 4096
        });

        var expected = path + ".provenance.json";
        Assert.True(File.Exists(expected));
        Assert.False(File.Exists(Path.ChangeExtension(path, ".provenance.json")));
    }
}
