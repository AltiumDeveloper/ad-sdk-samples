<#
.SYNOPSIS
  Removes a sample's extension from Altium Designer: its <Item> in Extensions\ExtensionsRegistry.xml (a backup is
  kept next to it) and its folder in the Extensions folder. The reverse of tools\deploy.ps1.
  Takes effect on the next AD start; files AD has loaded are renamed aside and removed later.

.EXAMPLE
  .\tools\undeploy.ps1 -Sample DocumentOpenSaveNotification
#>
param([Parameter(Mandatory = $true)] [string] $Sample)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot '_sample.ps1')
$props = (& dotnet msbuild (Resolve-Sample $Sample).Shim -nologo -getProperty:AltiumSystemDir | Out-String).Trim()
$programDir = [IO.Path]::GetFullPath((Join-Path $props '..')).TrimEnd('\')
$build = Get-ChildItem 'HKLM:\SOFTWARE\Altium\Builds' | Get-ItemProperty |
    Where-Object { $_.ProgramsInstallPath -and $_.ProgramsInstallPath.TrimEnd('\') -ieq $programDir } | Select-Object -First 1
if (-not $build) { throw "No Altium Designer installation registered at $programDir" }
$extensionsDir = Join-Path $env:ProgramData "$($build.Application)\Extensions"
$registry = Join-Path $extensionsDir 'ExtensionsRegistry.xml'

if ($Sample -notmatch '^[A-Za-z0-9][A-Za-z0-9_.-]*$') { throw "'$Sample' is not a plain extension name." }
$text = [IO.File]::ReadAllText($registry)
$pattern = '(?s)[ \t]*<Item HRID="' + [regex]::Escape($Sample) + '"[^>]*>.*?</Item>[ \t]*(\r?\n)*'
$count = ([regex]::Matches($text, $pattern)).Count
if ($count -gt 0) {
    $new = [regex]::Replace($text, $pattern, '')
    [xml]$new | Out-Null   # never write a file AD cannot parse
    $stamp = Get-Date -Format 'yyyyMMddHHmmss'
    Copy-Item $registry "$registry.bak-$stamp"
    [IO.File]::WriteAllText($registry, $new, (New-Object Text.UTF8Encoding $false))
    Write-Host "  unregistered $Sample ($count entry) in $registry (backup .bak-$stamp)"
} else {
    Write-Host "  $Sample is not registered in $registry"
}

$dir = Join-Path $extensionsDir $Sample
if (Test-Path $dir) {
    foreach ($f in Get-ChildItem $dir -File -Recurse) {
        try { Remove-Item $f.FullName -Force } catch { Rename-Item $f.FullName ($f.Name + '.old-' + (Get-Date -Format 'yyyyMMddHHmmss')) }
    }
    try { Remove-Item $dir -Recurse -Force; Write-Host "  removed $dir" } catch { Write-Host "  ${dir}: files in use, renamed aside (removed after AD exits)" }
}
