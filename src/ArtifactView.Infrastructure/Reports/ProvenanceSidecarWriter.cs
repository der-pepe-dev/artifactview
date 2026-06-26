using System.Text.Json;
using System.Text.Json.Serialization;
using ArtifactView.Core.Models;

namespace ArtifactView.Infrastructure.Reports;

// Writes a .provenance.json sidecar alongside every exported artifact or
// reconstruction.  File sits next to the exported file so it travels with it.
public static class ProvenanceSidecarWriter
{
    private static readonly JsonSerializerOptions s_opts = new()
    {
        WriteIndented            = true,
        DefaultIgnoreCondition   = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy     = JsonNamingPolicy.CamelCase
    };

    // Writes <outputPath>.provenance.json.
    // Silent on failure — sidecar is supplemental, never fatal.
    public static void Write(string outputPath, ProvenanceSidecar sidecar)
    {
        try
        {
            var sidecarPath = outputPath + ".provenance.json";
            var json        = JsonSerializer.Serialize(sidecar, s_opts);
            File.WriteAllText(sidecarPath, json, System.Text.Encoding.UTF8);
        }
        catch { /* best-effort */ }
    }

    // Convenience: build and write the sidecar in one call.
    public static void Write(
        string   outputPath,
        string   sourceFile,
        string   sourcePresence,
        string   extractionSource,
        string   extractionMethod,
        string   reconstructionCategory,
        string   outputFormat,
        long     byteCount,
        string?  warning       = null,
        string?  notes         = null,
        IReadOnlyList<string>? contributors = null)
    {
        Write(outputPath, new ProvenanceSidecar
        {
            ExportedAt              = DateTime.UtcNow.ToString("o"),
            SourceFile              = sourceFile,
            SourcePresence          = sourcePresence,
            ExtractionSource        = extractionSource,
            ExtractionMethod        = extractionMethod,
            ReconstructionCategory  = reconstructionCategory,
            OutputFormat            = outputFormat,
            ByteCount               = byteCount,
            Warning                 = warning,
            Notes                   = notes,
            Contributors            = contributors ?? []
        });
    }
}
