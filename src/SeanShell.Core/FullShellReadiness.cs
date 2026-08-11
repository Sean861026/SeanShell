namespace SeanShell.Core;

public enum FullShellReadinessState
{
    UnsupportedEdition,
    SafetyWorkPending,
    Unavailable,
}

public sealed record FullShellReadinessSnapshot(
    string ProductName,
    string EditionId,
    FullShellReadinessState State,
    string Title,
    string Message)
{
    public bool IsSupportedEdition => State == FullShellReadinessState.SafetyWorkPending;
}

public static class FullShellReadinessResolver
{
    private static readonly HashSet<string> SupportedEditionIds = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Enterprise",
        "EnterpriseN",
        "EnterpriseS",
        "EnterpriseSN",
        "Education",
        "EducationN",
        "IoTEnterprise",
        "IoTEnterpriseS",
    };

    public static FullShellReadinessSnapshot Resolve(
        string? productName,
        string? editionId)
    {
        var normalizedProductName = string.IsNullOrWhiteSpace(productName)
            ? "Windows"
            : productName.Trim();
        var normalizedEditionId = string.IsNullOrWhiteSpace(editionId)
            ? "Unknown"
            : editionId.Trim();

        if (normalizedEditionId == "Unknown")
        {
            return new FullShellReadinessSnapshot(
                normalizedProductName,
                normalizedEditionId,
                FullShellReadinessState.Unavailable,
                "Windows edition could not be verified",
                "SeanShell will not offer Full shell configuration until Windows reports a supported edition.");
        }

        if (!SupportedEditionIds.Contains(normalizedEditionId))
        {
            return new FullShellReadinessSnapshot(
                normalizedProductName,
                normalizedEditionId,
                FullShellReadinessState.UnsupportedEdition,
                "Full shell is unavailable on this Windows edition",
                "Microsoft Shell Launcher requires Enterprise, Education, or IoT Enterprise. Companion Taskbar remains available without replacing Explorer.");
        }

        return new FullShellReadinessSnapshot(
            normalizedProductName,
            normalizedEditionId,
            FullShellReadinessState.SafetyWorkPending,
            "Windows edition supports Shell Launcher",
            "SeanShell is completing its recovery package and administrator-only configuration flow before Full shell can be enabled.");
    }
}
