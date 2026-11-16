# List the folders of an installed Vault

Connects to one of the Vaults (workspaces) installed in Altium Designer and lists the top-level folders that
`DXP.IEDMS_Vault.GetFolders` returns, with the size of the subtree loaded under each. It teaches the EDMS request
pattern: get the vault manager, pick an installed vault, start an asynchronous request, wait for it, and check its
outcome before reading it.

## What it shows
- `EDP.Utils.GetVaultManager`: the vault manager, directly from the .NET API, with no P/Invoke.
- `DXP.IEDMS_VaultManager.GetInstalledVaultCount` / `GetInstalledVault`: the installed vaults, by index.
- `DXP.IEDMS_Vault.GetHRID` / `GetServiceURL`: which vault was used, for the returned text.
- `DXP.IEDMS_Vault.GetFolders` with `DXP.TEDMS_VaultFlagSet` (`DXP.TEDMS_VaultFlag.edmsWait`): an asynchronous
  folder request that returns a `DXP.IULB_FoldersResult`.
- `DXP.IEDM_AsyncResult.WaitUntilDone` / `IsDone` / `GetOutput` (`DXP.IEDM_Result.Success`, `Message`): wait with
  a timeout and check whether the request succeeded.
- `DXP.IULB_FoldersResult.Folders` (`DXP.IInterfaceList`): `Get(i)` returns `object`, cast to `DXP.IULB_Folder`.
- `DXP.IULB_Folder.HRID` / `GetSubFolderCount` / `SubFolder[i]`: the folder name and the subtree that
  `recursive = true` loads under each top-level folder.

## Run it
1. Sign in to a workspace in Altium Designer, so that at least one Vault is installed. No document is needed.
2. **GetVaultFolder › Get Vault Folders**, or `tools/ad.ps1 -Sample GetVaultFolder run GetVaultFolders "Vault=<index>"`
3. The result names the vault, the number of top-level folders and of the folders below them, then lists one
   top-level folder per line with its direct subfolder count and the size of its subtree.

| Command | Parameters | Result |
|---|---|---|
| `GetVaultFolders` | `Vault=<index>` (default 0), `Timeout=<seconds>` (default 30), `Max=<lines>` (default 200) | `Vault <i> of <n> '<HRID>' (<service URL>): <count> top-level folder(s), <below> folder(s) below them.` followed by `<HRID> (<k> subfolder(s), <m> in its subtree)` lines |

## How it works
`GetVaultFolderCommands.cs` reads the three integer parameters, then gets the vault manager from
`EDP.Utils.GetVaultManager()`. It checks the installed vault count and the requested index, and gets the vault
with `GetInstalledVault`. It calls `GetFolders` with these arguments: no parent folder GUID, no folder
type filter, recursive, the `edmsWait` flag, no callback and no context. It waits with `WaitUntilDone(Timeout)`,
then reads `GetOutput()` and reports a failure from it. Finally it walks the `Folders` list, casts each entry to
`IULB_Folder`, counts its subtree by walking `GetSubFolder(i)` recursively, and returns one line per top-level
folder (at most `Max` lines).

## Notes
- Read-only: nothing in the vault is changed.
- `Folders` holds only the top-level folders (empty `ParentFolderGUID`) whether `recursive` is true or false.
  With `recursive = true` each folder carries its subtree in `SubFolder[i]`; with `false`, `GetSubFolderCount()`
  is 0.
- With `edmsWait` the call blocks until the request is done, and `WaitUntilDone` returns at once. Without the flag
  the call returns immediately (`IsDone()` false) and `WaitUntilDone(seconds)` waits, returning `true` when done.
  The sample keeps both.
- A failed request still completes: `GetOutput().Success` is `false` and `Message` holds the reason (the same text
  as `Result.GetFirstErrorMessage()`; `FaultCode` is empty). Examples: `Not supported.` from some installed
  workspaces, `The server name could not be resolved.` from unreachable ones. Pass `Vault=` to pick one that answers.
- The format of the first `GetFolders` argument (several parent folder GUIDs: separator unknown) is not
  documented; the sample passes `null` (the root).
- The request runs on Altium Designer's main thread and blocks it for up to `Timeout` seconds.
