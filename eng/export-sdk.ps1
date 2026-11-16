<#
.SYNOPSIS
  Copies the files the samples compile against from an Altium Designer installation into a folder laid out for the
  private SDK repository that CI reads (.github/workflows/samples.yml, AGENTS.md "Continuous integration").

.DESCRIPTION
  Layout written under -Out:
    <version>\System\<assemblies>        the Altium SDK assemblies and the assemblies they need that .NET does not ship
    <version>\X2.runtimeconfig.json      Altium Designer's runtime config: Directory.Build.props reads the target framework from it
  <version> is the installation's major.minor (26.12), unless -Version is given.

  Compile-time references only: nothing here runs on the build machine, and none of it is redistributed with the
  samples. Keep the repository private.

  After copying, the script builds the whole solution against the copy (-Verify), which proves the set is complete.

.EXAMPLE
  .\eng\export-sdk.ps1 -AltiumSystemDir 'C:\Program Files\Altium\AD26\System' -Out C:\src\ad-sdk-assemblies
#>
param(
    [Parameter(Mandatory = $true)] [string] $AltiumSystemDir,
    [Parameter(Mandatory = $true)] [string] $Out,
    [string] $Version,
    [switch] $Verify
)

$ErrorActionPreference = 'Stop'
$system = [IO.Path]::GetFullPath($AltiumSystemDir).TrimEnd('\')
if (-not (Test-Path (Join-Path $system 'Altium.SDK.dll'))) { throw "Altium.SDK.dll not found in $system" }

if (-not $Version) {
    $program = Split-Path -Parent $system
    $build = Get-ChildItem 'HKLM:\SOFTWARE\Altium\Builds' -ErrorAction SilentlyContinue | Get-ItemProperty |
        Where-Object { $_.ProgramsInstallPath -and $_.ProgramsInstallPath.TrimEnd('\') -ieq $program } | Select-Object -First 1
    if (-not $build -or -not $build.Version) { throw "Cannot tell the version of the installation at ${program}: pass -Version 26.12" }
    $Version = ($build.Version -split '\.')[0..1] -join '.'
}

# What the build references from the installation (Directory.Build.targets, WebBrowserPanel) and what those
# assemblies reference in turn that is not part of .NET: Altium.SDK uses WCF client types and System.Management.
$files = @(
    'Altium.SDK.dll', 'Altium.SDK.Interfaces.dll',
    'System.ServiceModel.Primitives.dll', 'System.ServiceModel.Http.dll', 'System.Management.dll',
    'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll'
)
$target = Join-Path $Out "$Version\System"
New-Item -ItemType Directory -Force -Path $target | Out-Null
foreach ($f in $files) {
    $src = Join-Path $system $f
    if (-not (Test-Path $src)) { throw "$f not found in $system" }
    Copy-Item $src (Join-Path $target $f) -Force
    Write-Host "  $Version\System\$f"
}
$runtimeConfig = Join-Path (Split-Path -Parent $system) 'X2.runtimeconfig.json'
if (-not (Test-Path $runtimeConfig)) { throw "X2.runtimeconfig.json not found next to $system" }
Copy-Item $runtimeConfig (Join-Path $Out "$Version\X2.runtimeconfig.json") -Force
Write-Host "  $Version\X2.runtimeconfig.json"

if ($Verify) {
    $root = Split-Path -Parent $PSScriptRoot
    Write-Host "Building $root\AdSdkSamples.slnx against $target ..."
    & dotnet build (Join-Path $root 'AdSdkSamples.slnx') -c Release -nologo -v q "-p:AltiumSystemDir=$target" -m:2 -nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'The build against the exported files failed: a file is missing from the list above.' }
    Write-Host 'The exported files are enough to build every sample.'
}
Write-Host "Done. Update $Out\$Version\MANIFEST.md, commit, tag ad-<full build number>, push the tag, and set AD_SDK_VERSION / AD_SDK_REF in the samples repository."
