using Microsoft.Win32;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class FullShellReadinessService
{
    private const string CurrentVersionKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public FullShellReadinessSnapshot Capture()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(CurrentVersionKey);
            var productName = key?.GetValue("ProductName") as string;
            var editionId = key?.GetValue("EditionID") as string;
            productName = NormalizeWindowsProductName(productName);
            return FullShellReadinessResolver.Resolve(productName, editionId);
        }
        catch (Exception exception) when (
            exception is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return FullShellReadinessResolver.Resolve(null, null);
        }
    }

    private static string? NormalizeWindowsProductName(string? productName)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) &&
            productName?.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase) == true)
        {
            return $"Windows 11{productName["Windows 10".Length..]}";
        }

        return productName;
    }
}
