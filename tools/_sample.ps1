# Shared by deploy.ps1 / dev.ps1 / ad.ps1 / undeploy.ps1: finds a sample's projects by name
# (samples\<category>\<Name>). Dot-source it: . (Join-Path $PSScriptRoot '_sample.ps1')
function Resolve-Sample([string] $name) {
    $root = Split-Path -Parent $PSScriptRoot
    if (-not $name) { throw 'Pass -Sample <Name> (a folder under samples\<category>\).' }
    $candidates = @(Get-ChildItem (Join-Path $root 'samples') -Directory -Recurse -Depth 1 -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ieq $name -and (Test-Path (Join-Path $_.FullName "$($_.Name)\$($_.Name).csproj")) })
    if ($candidates.Count -eq 0) { throw "Sample '$name' not found under samples\<category>\." }
    if ($candidates.Count -gt 1) { throw "Sample '$name' is ambiguous: $($candidates.FullName -join ', ')" }
    $dir = $candidates[0].FullName
    $n = $candidates[0].Name
    [pscustomobject]@{
        Name = $n
        Dir  = $dir
        Shim = Join-Path $dir "$n\$n.csproj"
        Impl = Join-Path $dir "$n.Impl\$n.Impl.csproj"
    }
}
