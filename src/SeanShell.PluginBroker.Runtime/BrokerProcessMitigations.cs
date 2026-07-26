using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SeanShell.PluginBroker;

internal static class BrokerProcessMitigations
{
    private const uint DisableExtensionPoints = 0x00000001;
    private const uint NoRemoteOrLowIntegrityImages = 0x00000003;
    private const uint BlockChildProcesses = 0x00000001;

    public static void Apply()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The external plugin broker security profile requires Windows.");
        }

        ApplyPolicy(
            ProcessMitigationPolicy.ExtensionPointDisable,
            DisableExtensionPoints,
            "legacy extension points");
        ApplyPolicy(
            ProcessMitigationPolicy.ImageLoad,
            NoRemoteOrLowIntegrityImages,
            "remote and low-integrity image loading");
        ApplyPolicy(
            ProcessMitigationPolicy.ChildProcess,
            BlockChildProcesses,
            "child process creation");
    }

    private static void ApplyPolicy(
        ProcessMitigationPolicy policy,
        uint flags,
        string description)
    {
        if (!SetProcessMitigationPolicy(policy, ref flags, sizeof(uint)))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                $"Unable to disable {description} for the plugin broker.");
        }
    }

    private enum ProcessMitigationPolicy
    {
        ExtensionPointDisable = 6,
        ImageLoad = 10,
        ChildProcess = 13,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessMitigationPolicy(
        ProcessMitigationPolicy mitigationPolicy,
        ref uint buffer,
        nuint length);
}
