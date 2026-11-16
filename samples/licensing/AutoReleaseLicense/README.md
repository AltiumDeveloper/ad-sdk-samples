# Return leased licenses after user inactivity

Watches keyboard and mouse input in Altium Designer and, once the user has been idle for a set time, returns the
leased licenses with `DXP.ILicenseManager.ReturnLeasedLicensesOnLogout`. It shows how an extension tracks idle time
on AD's UI thread and where native callbacks must live in a hot-reloaded extension.

## What it shows
- `DXP.IClient.GetLicenseManager`: the license manager, an extension method on the client (`DXP.GlobalVars.Client`).
- `DXP.ILicenseManager.ReturnLeasedLicensesOnLogout`: the release call, made once per idle period.
- `DXP.ILicenseManager.GetCurrentLicenseInfo`: the current license ids and names, as a read-back before and after.
- Win32 thread hooks (`SetWindowsHookEx` with `WH_MOUSE` / `WH_KEYBOARD`) on AD's UI thread, owned by the shim so
  that no native callback points into the hot-reloaded implementation.
- A `System.Windows.Forms.Timer` created on AD's UI thread instead of a background thread, so the SDK is only called
  on the main thread.

## Run it
The extension must be deployed with `tools/deploy.ps1 -Sample AutoReleaseLicense` and Altium Designer restarted.

1. No document needed. A real release needs a leased (network) license; the dry run works with any license.
2. **AutoReleaseLicense › Start Auto Release**, or `tools/ad.ps1 -Sample AutoReleaseLicense run StartAutoRelease "IdleSeconds=5"`
3. Leave AD alone for more than 5 s, then **AutoReleaseLicense › Auto Release Status**: "Idle triggers: 1" and a
   "Last result" saying it would have called `ReturnLeasedLicensesOnLogout`. **Stop Auto Release** removes the hooks.

| Command | Parameters | Result |
|---|---|---|
| `StartAutoRelease` | `IdleSeconds=<1..86400>` (default 30), `DryRun=false` to really return licenses | "Auto release started: idle threshold … s, dry run …"; when already running, updates the settings |
| `StopAutoRelease` | | "Auto release stopped." or "Auto release not running." |
| `AutoReleaseStatus` | | running or not, threshold, seconds since the last input in AD, input events seen, idle triggers, last result, last error |
| `ReleaseLicensesNow` | `DryRun=false` to really return licenses | the call (or the dry run) and the license ids/names before and after |
| `AutoRelease.Idle` | `DryRun`, `IdleSeconds` (set by the shim) | run by the shim's timer, not on a menu; its text becomes the status's "Last result" |

## How it works
`AutoReleaseLicense/IdleWatcher.cs` is the shim part. When AD creates the server module, the partial
`RegisterViews` records the native ID of AD's UI thread and publishes three delegates (start, stop, status) in
AppDomain data, because the implementation cannot reference the shim assembly. Start installs a mouse and a keyboard
thread hook on that UI thread (`hMod` 0, so only input to AD's own windows counts) whose procedure only stores
`Environment.TickCount64` and calls `CallNextHookEx`; the delegates are kept in static fields so the garbage
collector cannot free them under the native hook. A one-second WinForms timer compares the idle time with the
threshold and, once per idle period, runs the implementation command `AutoRelease.Idle` through the framework's
`Commands.Invoke`, keeping its result or exception for the status.

`AutoReleaseLicense.Impl/Commands/AutoReleaseLicenseCommands.cs` holds the commands. `StartAutoRelease`,
`StopAutoRelease` and `AutoReleaseStatus` validate parameters, call the shim's delegates and format the state.
`ReleaseLicensesNow` and `AutoRelease.Idle` get the license manager from `DXP.GlobalVars.Client`, read
`GetCurrentLicenseInfo`, call `ReturnLeasedLicensesOnLogout` unless it is a dry run, and read the license info back.

## Notes
- Returning licenses is a dry run unless `DryRun=false`, so the menu items never return a license. What
  `ReturnLeasedLicensesOnLogout` returns exactly (all leased licenses, or only the unused ones) and whether it affects
  a standalone or subscription seat is undocumented.
- Only input that AD's UI thread receives resets the idle clock; working in another application counts as idle in AD.
  The idle clock starts when `StartAutoRelease` runs.
- The hooks and the timer live in the shim, which is loaded once and never unloaded; a hot reload of the
  implementation does not touch them. Stop them with `StopAutoRelease`. A new shim build takes effect after an AD
  restart.
- The watcher does not start by itself. Starting it at load would need `SystemExtension = True`, which these
  samples avoid because a failing shim would stop AD from starting.
- The thread hooks install on AD's UI thread without error, the WinForms timer ticks under AD's message loop, and a
  tick can call the hot-reloaded `AutoRelease.Idle` command.
- `GetCurrentLicenseInfo` returns both ids and names as newline-separated lists with a trailing newline.
