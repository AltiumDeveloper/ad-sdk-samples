<#
.SYNOPSIS
  Talks to a running Altium Designer (a sample's Debug shim) through the hot reload pipe.
  Thin wrapper over tools\hotreload.ps1 (from the Altium.Designer.HotReload package) with -Name <Sample>.
  With several Altium Designer instances running, pass -ProcessId or set AD_PROCESS_ID (tools\_target.ps1).

.EXAMPLE
  .\tools\ad.ps1 -Sample SmokeTest status -gc
  .\tools\ad.ps1 -Sample SmokeTest run Hello "Who=AD"
  .\tools\ad.ps1 -Sample SmokeTest -Pid 12345 reload      # a specific AD process

  Verbs: ping | status [-gc] | reload | list | run <Command> [Key=Value|Key=Value]
  Exit code: 0 ok, 1 command failed, 2 usage / ambiguous instance, 3 AD not reachable.
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true)] [string] $Sample,
    [Alias('Pid')] [int] $ProcessId = 0,
    [Parameter(ValueFromRemainingArguments = $true)] $Rest
)
try {
    . (Join-Path $PSScriptRoot '_target.ps1')
    if ($ProcessId -eq 0) { $ProcessId = Get-TargetProcessId }
    $global:LASTEXITCODE = 0
    # $Rest is an array, and splatting an array passes every element positionally: a named parameter of
    # hotreload.ps1 written among the verb's arguments (-ReadTimeoutMs 60000) would arrive as a verb or as part of
    # the Key=Value string instead of binding. Lift the named ones out, wherever the caller put them.
    $named = @{}
    $positional = @()
    for ($i = 0; $i -lt $Rest.Count; $i++) {
        $arg = [string] $Rest[$i]
        if ($arg -match '^-(ReadTimeoutMs|TimeoutMs)$' -and $i + 1 -lt $Rest.Count) {
            $named[$Matches[1]] = [int] $Rest[++$i]
        }
        else { $positional += $Rest[$i] }
    }
    if ($ProcessId -ne 0) { $named['ProcessId'] = $ProcessId }
    & (Join-Path $PSScriptRoot 'hotreload.ps1') -Name $Sample @named @positional
    exit $LASTEXITCODE
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 2
}
