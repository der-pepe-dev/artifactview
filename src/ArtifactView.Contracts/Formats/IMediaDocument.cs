namespace ArtifactView.Contracts.Formats;

public interface IMediaDocument
{
    string DisplayFormatName { get; }
    IReadOnlyList<string> Capabilities { get; }
}
