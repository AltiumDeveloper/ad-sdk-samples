# Look up an installed extension's version

Finds an extension in Altium Designer's plugin registry by its HRID and returns its version, or says it is not
installed. A second command lists the registry's entries. It shows how to reach the plugin registry through the
client and read the entries' properties.

## What it shows
- `DXP.GlobalVars.Client`: the application object, the entry point to the registry.
- `DXP.IClient.GetPluginsRegistry`: the registry of installed extensions. It is a typed helper (extension) method:
  `using DXP;` makes it callable like an interface member.
- `DXP.IPluginsRegistry.FindItemByHRID`: looks up one entry by its human-readable ID (HRID).
- `DXP.IPluginsRegistry.GetCount` / `DXP.IPluginsRegistry.GetItem`: walk all entries by index.
- `DXP.IPluginsRegistryItem.HRID`, `DXP.IPluginsRegistryItem.Version`, `DXP.IPluginsRegistryItem.Status`:
  read with `GetHRID()`, `GetVersion()` and `GetStatus()` (a `DXP.TPluginsStatus`).

## Run it
1. No document is needed. Any Altium Designer state works.
2. **GetExtensionVersion › List Extensions**, or `tools/ad.ps1 -Sample GetExtensionVersion run ListExtensions`,
   to see which HRIDs are registered.
3. **GetExtensionVersion › Get Version** (uses the default HRID, `Altium Developer`), or
   `tools/ad.ps1 -Sample GetExtensionVersion run GetVersion "Hrid=<HRID>"`. You get the entry's version, or a
   "not installed" message.

| Command | Parameters | Result |
|---|---|---|
| `GetVersion` | `Hrid=<HRID>` (default `Altium Developer`) | `Installed extension '<HRID>' is version <v>.`, `... has no version string.`, or `Extension '<HRID>' is not installed.` |
| `ListExtensions` | none | `<n> extension(s) in the plugin registry:` and one line per entry: HRID, version, status |

## How it works
`GetExtensionVersionCommands.cs` gets the registry from `DXP.GlobalVars.Client` with `GetPluginsRegistry()` on
every call. It never caches it, because extensions can be installed or removed while Altium Designer runs.
`GetVersion` reads the `Hrid` parameter and calls `FindItemByHRID`. A null result means "not installed". Otherwise it returns `GetVersion()` of the item. `ListExtensions` loops from 0 to `GetCount() - 1` with
`GetItem(i)` and prints each item's HRID, version and status.

## Notes
- The registry holds the entries of `Extensions\ExtensionsRegistry.xml` (see the docs' Getting started guide,
  "Registering the extension"). An extension deployed by this repository's `tools/deploy.ps1` is registered with its
  sample name as HRID and an empty `<Version>`. `GetVersion` reports such an entry as having no version string.
- `FindItemByHRID` returns null for an unknown HRID.
- The HRID lookup is case-insensitive: `Hrid=draftsman` finds `Draftsman`. The reply shows
  the registered spelling.
- The registry lists the entries of `ExtensionsRegistry.xml` in file order. An entry with `<Status>0</Status>`
  reports `psActive`.
- A sample loaded through hot reload, without being deployed, has no registry entry of its own, so
  `Hrid=GetExtensionVersion` reports "not installed" unless the sample was deployed.
- The default HRID `Altium Developer` may not be registered in every installation. Use `ListExtensions` to find
  one that is.
