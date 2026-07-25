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
