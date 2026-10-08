# RazorForge for VS Code

RazorForge (`.rf`, `.razorforge`) in VS Code: highlighting from `../RazorForge.tmbundle`, comment toggling and bracket pairing,
and the RazorForge language server (`RazorForge lsp`): errors as you type, semantic colors, hover, completion, go to
definition, rename, find usages and formatting.

## Which server runs

The setting `razorforge.serverPath` sets it: `RazorForge.dll` (run with `dotnet`) or a `RazorForge` executable. Left empty, the
extension uses the dev build in the workspace, `<workspace>/RazorForge/bin/Debug/net10.0/RazorForge.dll`, else `RazorForge` on the
PATH. The server runs from a copy of its build folder in the extension's storage, so it never holds the files a rebuild
overwrites, and a rebuild restarts it. The command "RazorForge: Restart the language server" restarts it by hand.

## Build

Needs Node.js. The grammar, the language configuration and the icon are copied in from `../RazorForge.tmbundle` and
`../rider-plugin`, their one source.

```
npm install
npm run package          # -> dist/razorforge.vsix
code --install-extension dist/razorforge.vsix
```
