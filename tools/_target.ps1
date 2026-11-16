# Shared by ad.ps1 / dev.ps1: which Altium Designer process to talk to.
# 0 lets tools\hotreload.ps1 pick the running instance (it lists them and asks when there are several).
# Set AD_PROCESS_ID to talk to one particular instance.
function Get-TargetProcessId {
    if ($env:AD_PROCESS_ID) { return [int] $env:AD_PROCESS_ID }
    return 0
}
