[CmdletBinding()]
param()

$explorer = Get-Process -Name explorer -ErrorAction SilentlyContinue

if ($null -eq $explorer) {
    Start-Process -FilePath "$env:WINDIR\explorer.exe"
    Write-Host "Windows Explorer was started."
} else {
    Write-Host "Windows Explorer is already running."
}

$seanShell = Get-Process -Name SeanShell.App -ErrorAction SilentlyContinue
if ($null -ne $seanShell) {
    foreach ($process in @($seanShell)) {
        $process.CloseMainWindow() | Out-Null
    }
    Write-Host "Requested SeanShell to close."

    Start-Sleep -Seconds 2
    $remaining = Get-Process -Name SeanShell.App -ErrorAction SilentlyContinue
    if ($null -ne $remaining) {
        foreach ($process in @($remaining)) {
            Stop-Process -Id $process.Id -Force
        }
        Write-Host "Stopped SeanShell after it did not close within 2 seconds."
    }
}

if (-not ("SeanShellTaskbarRecovery.NativeMethods" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SeanShellTaskbarRecovery
{
    public static class NativeMethods
    {
        private const int SwShow = 5;

        private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

        public static void RestoreAll()
        {
            var primary = FindWindow("Shell_TrayWnd", null);
            if (primary != IntPtr.Zero)
            {
                ShowWindow(primary, SwShow);
            }

            EnumWindows((handle, parameter) =>
            {
                var className = new StringBuilder(256);
                GetClassName(handle, className, className.Capacity);
                if (string.Equals(
                    className.ToString(),
                    "Shell_SecondaryTrayWnd",
                    StringComparison.Ordinal))
                {
                    ShowWindow(handle, SwShow);
                }

                return true;
            }, IntPtr.Zero);
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(
            EnumWindowsProc callback,
            IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(
            string className,
            string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(
            IntPtr handle,
            StringBuilder className,
            int maximumCount);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr handle, int command);
    }
}
"@
}

[SeanShellTaskbarRecovery.NativeMethods]::RestoreAll()
Write-Host "Requested every Windows taskbar to become visible."

$startupHealthPaths = [System.Collections.Generic.List[string]]::new()
$startupHealthPaths.Add((Join-Path $env:LOCALAPPDATA "SeanShell\startup-health.json"))

$package = Get-AppxPackage -Name "EDFE4C52-E9FB-47BA-94FE-4B02C1B828F2" -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -ne $package) {
    $packageData = Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)"
    $startupHealthPaths.Add(
        (Join-Path $packageData "LocalCache\Local\SeanShell\startup-health.json"))
}

foreach ($startupHealthPath in $startupHealthPaths) {
    if (Test-Path -LiteralPath $startupHealthPath) {
        Remove-Item -LiteralPath $startupHealthPath -Force
        Write-Host "SeanShell startup health history was reset: $startupHealthPath"
    }
}
