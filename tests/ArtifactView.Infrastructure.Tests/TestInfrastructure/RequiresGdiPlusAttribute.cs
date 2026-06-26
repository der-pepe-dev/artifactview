using System.Drawing;
using TUnit.Core;

namespace ArtifactView.Infrastructure.Tests;

/// <summary>
/// Skips a test when GDI+ / libgdiplus is unavailable (e.g. headless WSL2).
/// Probes actual availability at runtime — libgdiplus works on Linux too, so
/// this is a real <see cref="Bitmap"/> probe rather than an OS check
/// (<c>OperatingSystem.IsWindows()</c> returns false on WSL2). Replaces the old
/// Xunit.SkippableFact + inline <c>Lazy&lt;bool&gt;</c> probe pattern.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
public sealed class RequiresGdiPlusAttribute() : SkipAttribute("GDI+ / libgdiplus not available")
{
    private static readonly Lazy<bool> s_available = new(() =>
    {
        try { _ = new Bitmap(1, 1); return true; }
        catch { return false; }
    });

    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(!s_available.Value);
}
