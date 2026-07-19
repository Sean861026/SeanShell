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
    $seanShell.CloseMainWindow() | Out-Null
    Write-Host "Requested SeanShell to close."
}
