using Microsoft.UI.Xaml;
using SeanShell.PluginContracts;
using SeanShell.Plugins;

namespace SeanShell.App;

public sealed class ExternalPluginCandidateViewModel
{
    public ExternalPluginCandidateViewModel(
        ExternalPluginCandidate candidate,
        bool isApproved,
        bool canChange)
    {
        Candidate = candidate;
        Name = candidate.Name;
        Identity = candidate.Id is null
            ? candidate.PackageDirectoryName
            : $"{candidate.Publisher} · {candidate.Id} · {candidate.Version}";
        State = isApproved
            ? "Approved · execution blocked"
            : candidate.Status switch
            {
                ExternalPluginCandidateStatus.ReadyForConsent => "Trust checks passed",
                ExternalPluginCandidateStatus.Unsigned => "Unsigned",
                ExternalPluginCandidateStatus.RevokedSignature => "Publisher revoked",
                ExternalPluginCandidateStatus.RevocationUnavailable => "Revocation unavailable",
                ExternalPluginCandidateStatus.ExpiredSignature => "Certificate expired",
                ExternalPluginCandidateStatus.ExplicitlyDistrusted => "Publisher distrusted",
                ExternalPluginCandidateStatus.UntrustedSignature => "Untrusted signature",
                ExternalPluginCandidateStatus.PublisherMismatch => "Publisher mismatch",
                ExternalPluginCandidateStatus.UnsafePath => "Unsafe path",
                ExternalPluginCandidateStatus.MissingAssembly => "Assembly missing",
                _ => "Invalid manifest",
            };
        Detail = candidate.TrustVerifiedAtUtc is { } verifiedAtUtc
            ? $"{candidate.Detail} Checked {verifiedAtUtc.ToLocalTime():g}."
            : candidate.Detail;
        CapabilityText = FormatCapabilities(candidate.Capabilities);
        ActionText = isApproved ? "Revoke consent" : "Approve capabilities";
        ActionVisibility = candidate.Status == ExternalPluginCandidateStatus.ReadyForConsent
            ? Visibility.Visible
            : Visibility.Collapsed;
        CanChange = canChange;
    }

    public ExternalPluginCandidate Candidate { get; }

    public string Name { get; }

    public string Identity { get; }

    public string State { get; }

    public string Detail { get; }

    public string CapabilityText { get; }

    public string ActionText { get; }

    public Visibility ActionVisibility { get; }

    public bool CanChange { get; }

    public static string FormatCapabilities(PluginCapability capabilities)
    {
        var names = new List<string>();
        if (capabilities.HasFlag(PluginCapability.LauncherCommands))
        {
            names.Add("Launcher commands");
        }

        if (capabilities.HasFlag(PluginCapability.BackgroundWork))
        {
            names.Add("Background work");
        }

        return names.Count == 0 ? "None" : string.Join(", ", names);
    }
}
