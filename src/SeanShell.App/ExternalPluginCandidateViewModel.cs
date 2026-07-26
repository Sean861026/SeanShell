using SeanShell.Plugins;

namespace SeanShell.App;

public sealed class ExternalPluginCandidateViewModel
{
    public ExternalPluginCandidateViewModel(ExternalPluginCandidate candidate)
    {
        Name = candidate.Name;
        Identity = candidate.Id is null
            ? candidate.PackageDirectoryName
            : $"{candidate.Publisher} · {candidate.Id} · {candidate.Version}";
        State = candidate.Status switch
        {
            ExternalPluginCandidateStatus.ReadyForConsent => "Trust checks passed",
            ExternalPluginCandidateStatus.Unsigned => "Unsigned",
            ExternalPluginCandidateStatus.UntrustedSignature => "Untrusted signature",
            ExternalPluginCandidateStatus.PublisherMismatch => "Publisher mismatch",
            ExternalPluginCandidateStatus.UnsafePath => "Unsafe path",
            ExternalPluginCandidateStatus.MissingAssembly => "Assembly missing",
            _ => "Invalid manifest",
        };
        Detail = candidate.Detail;
    }

    public string Name { get; }

    public string Identity { get; }

    public string State { get; }

    public string Detail { get; }
}
