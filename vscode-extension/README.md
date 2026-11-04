# Revare for VS Code

Revare (`.rvr`, `.revare`) in VS Code: highlighting from `../Revare.tmbundle`, comment toggling and bracket pairing,
and the Revare language server (`Revare lsp`): errors as you type, semantic colors, hover, completion, go to
definition, rename, find usages and formatting.

## Which server runs

The setting `revare.serverPath` sets it: `Revare.dll` (run with `dotnet`) or a `Revare` executable. Left empty, the
extension uses the dev build in the workspace, `<workspace>/Revare/bin/Debug/net10.0/Revare.dll`, else `Revare` on the
PATH. The server runs from a copy of its build folder in the extension's storage, so it never holds the files a rebuild
overwrites, and a rebuild restarts it. The command "Revare: Restart the language server" restarts it by hand.

## Build

Needs Node.js. The grammar, the language configuration and the icon are copied in from `../Revare.tmbundle` and
`../rider-plugin`, their one source.

```
npm install
npm run package          # -> dist/revare.vsix
code --install-extension dist/revare.vsix
```
