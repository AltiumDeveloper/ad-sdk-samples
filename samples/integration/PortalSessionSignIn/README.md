# Sign in to the Altium portal with a session ID

Signs Altium Designer in to the Altium portal with a session ID obtained outside AD (for example from a browser
session), instead of the normal sign-in flow, and reports the portal's sign-in state. It teaches the portal
object of the client, `DXP.IDXPAltiumPortal`, and its `LoginBySessionID` call.

## What it shows
- `DXP.IClient.GetDXPAltiumPortal`: the portal object. It is an extension method (needs `using DXP;`), called on
  `DXP.GlobalVars.Client`.
- `DXP.IDXPAltiumPortal.LoginBySessionID`: signs in with a session handle; the first argument
  (`argWaitForResult`) is `true`.
- `DXP.IDXPAltiumPortal.LoggedIn` / `LoggingIn` / `IsBusy`: the sign-in state. They are methods in the .NET API
  (`portal.LoggedIn()`).
- `DXP.IDXPAltiumPortal.UserName` / `FullName`: who is signed in, read only when `LoggedIn()` is true.

## Run it
1. To sign in: sign out of the workspace first, and obtain a session ID from a browser session with the Altium
   portal. To only read the state, nothing is needed.
2. **PortalSessionSignIn › Login** (asks for the session ID in a dialog), or
   `tools/ad.ps1 -Sample PortalSessionSignIn run Login "SessionId=<session ID>"`; for the state,
   **PortalSessionSignIn › Login State** or `tools/ad.ps1 -Sample PortalSessionSignIn run LoginState ""`
3. `Login` returns `LoginBySessionID returned.` and the state line, for example
   `LoggedIn=True, LoggingIn=False, IsBusy=False, user name '<name>', full name '<name>'`. When already signed in it
   returns `Already signed in to the Altium portal; nothing done.` and does not call `LoginBySessionID`.

| Command | Parameters | Result |
|---|---|---|
| `Login` | `SessionId=<session ID>` (from the menu: optional, a dialog asks for it) | `LoginBySessionID returned.` + state line; `Already signed in ...; nothing done.`; `Cancelled; not signed in.`; an error when no ID is given through `tools/ad.ps1` |
| `LoginState` | none | `LoggedIn=…, LoggingIn=…, IsBusy=…` and, when signed in, the user name and full name |

## How it works
`PortalSessionSignInCommands.cs` gets the portal with `DXP.GlobalVars.Client.GetDXPAltiumPortal()`. `Login` first
checks `LoggedIn()` and does nothing when already signed in: a command cannot be disabled per sign-in state by the
shared shim, so the check is made in the command. It then takes `SessionId`, trimmed; from the menu with no
parameter it shows `SessionIdDialog` (a WinForms dialog: one masked text box, OK refuses an empty ID, Cancel). From
`tools/ad.ps1` no dialog is shown and an empty ID is an error. It calls `LoginBySessionID(true, id)`, turns a
`COMException` into an error with the state, and returns the state read by `DescribeState` (`LoggedIn`,
`LoggingIn`, `IsBusy`, then `UserName` and `FullName` when signed in). `LoginState` returns the same line.

## Notes
- A session ID is a credential. The sample never returns, logs or stores it; `PortalSessionHandle()` and
  `Password()` exist on `DXP.IDXPAltiumPortal` but are deliberately not read. Do not paste a real ID into scripts or
  shared logs: with `tools/ad.ps1` it travels in the command parameters.
- `Login` changes the signed-in state of Altium Designer. `LoginState` is read-only. The output contains the user
  name and full name; redact them before sharing.
- `LoggedIn()`, `LoggingIn()`, `IsBusy()`, `UserName()` and `FullName()` are callable from .NET and return the
  signed-in state and non-empty names.
- The reference has no description of `LoginBySessionID`: where a valid session ID comes from, its format and
  lifetime, whether `LoginBySessionID(true, …)` blocks until the sign-in has finished, and what it does with an
  invalid ID are not documented.
- Signing in with a session ID is a workaround for when the normal browser sign-in is not reachable, for example
  in the China region.
- The menu is on the no-document menu bar (the template's insertion).
