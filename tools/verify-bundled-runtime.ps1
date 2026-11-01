[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$bundlePath = (Resolve-Path -LiteralPath $OutputDirectory).Path
foreach ($fileName in @(
    'SeanShell.App.exe',
    'Microsoft.UI.Xaml.dll',
    'Microsoft.UI.Xaml.Controls.dll',
    'Microsoft.UI.Xaml.Controls.pri',
    'AppxManifest.xml'
)) {
    $bundleFile = Join-Path $bundlePath $fileName
    if (-not (Test-Path -LiteralPath $bundleFile -PathType Leaf)) {
        throw "Bundled Windows App SDK output is missing $fileName."
    }
}

[xml]$bundleManifest = Get-Content -LiteralPath (Join-Path $bundlePath 'AppxManifest.xml') -Raw
$bundleNamespaces = [System.Xml.XmlNamespaceManager]::new($bundleManifest.NameTable)
$bundleNamespaces.AddNamespace('p', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$bundleNamespaces.AddNamespace('uap10', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/10')
$bundleDependencies = $bundleManifest.SelectNodes('/p:Package/p:Dependencies/p:PackageDependency', $bundleNamespaces)
foreach ($dependency in $bundleDependencies) {
    if ($dependency.Name -like 'Microsoft.WindowsAppRuntime*') {
        throw 'Self-contained output unexpectedly depends on a shared Windows App Runtime package.'
    }
}

$bundleIntegrity = $bundleManifest.SelectSingleNode(
    '/p:Package/p:Properties/uap10:PackageIntegrity/uap10:Content', $bundleNamespaces)
if ($null -eq $bundleIntegrity -or $bundleIntegrity.Enforcement -ne 'on') {
    throw 'Package content integrity enforcement must remain enabled.'
}

$bundleHash = (Get-FileHash -LiteralPath (Join-Path $bundlePath 'Microsoft.UI.Xaml.dll') -Algorithm SHA256).Hash
Write-Host "Bundled Windows App SDK and package integrity verified. WinUI SHA256: $bundleHash"
