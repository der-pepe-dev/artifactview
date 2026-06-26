namespace ArtifactView.Contracts.Signatures;

public interface ISignatureRulePack
{
    string Id { get; }
    string DisplayName { get; }
    string Version { get; }
    IReadOnlyList<SignatureMatchResult> Match(ISignatureContext context);
}
