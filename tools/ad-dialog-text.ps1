<#
.SYNOPSIS
  Prints the text of every visible window of an Altium Designer process (dialogs: error details, prompts), read
  through UI Automation. For diagnosing a stalled or failing AD without a screenshot.

.EXAMPLE
  .\tools\ad-dialog-text.ps1 -ProcessId 21208
#>
param([Parameter(Mandatory = $true)] [int] $ProcessId, [string] $TitleLike = '*')

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$ae = [System.Windows.Automation.AutomationElement]
$cond = New-Object System.Windows.Automation.PropertyCondition($ae::ProcessIdProperty, $ProcessId)
$windows = $ae::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
foreach ($w in $windows) {
    if ($w.Current.Name -notlike $TitleLike) { continue }
    "=== window '$($w.Current.Name)' ($($w.Current.ClassName))"
    $all = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) {
        $text = $null
        try {
            $vp = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            if ($vp) { $text = $vp.Current.Value }
        } catch { }
        if (-not $text) { $text = $e.Current.Name }
        if ($text) { "[{0}] {1}" -f $e.Current.ControlType.ProgrammaticName.Replace('ControlType.', ''), $text }
    }
}
