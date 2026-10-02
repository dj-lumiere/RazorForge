# RazorForge for Rider

A Rider plugin for RazorForge (`.rf`, `.razorforge`):

- Syntax highlighting from `../RazorForge.tmbundle`, plus `#` comment toggling and bracket pairing (`bundle/`).
- The RazorForge file icon.
- The RazorForge language server (`RazorForge lsp`), through Rider's LSP client: diagnostics, completion, hover,
  go to definition, references, rename, signature help, inlay hints, code actions, symbols and formatting.

Suflae and Tessera have plugins of their own, in `../../Suflae/rider-plugin` and `../../Tessera/rider-plugin`.

## Which server runs

Settings | Languages & Frameworks | RazorForge sets the server: `RazorForge.dll` (run with `dotnet`) or a RazorForge
executable. Left empty, the plugin uses the dev build, `<project>/RazorForge/bin/Debug/net10.0/RazorForge.dll`
(the LumiFoundry workspace) or `<project>/bin/Debug/net10.0/RazorForge.dll`.

The server runs from a copy of its folder in Rider's system directory (`razorforge-lsp/`), never from `bin/`, so it
never holds the files the next `dotnet build` overwrites. When the build folder changes and then stays the same for
a few seconds, the plugin restarts the server from a fresh copy, so a rebuilt builder takes over by itself.

## Build

Needs Rider 2026.2 (build 262) or later. The build compiles against an installed Rider and uses Rider's own JDK, set
once for every plugin in `%USERPROFILE%\.gradle\gradle.properties`:

```
riderLocalPath=C:/Users/<you>/AppData/Local/Programs/Rider
org.gradle.java.installations.paths=C:/Users/<you>/AppData/Local/Programs/Rider/jbr
```

Without `riderLocalPath`, the build downloads the Rider named by `platformVersion` in `gradle.properties`.

```
gradlew buildPlugin    # -> build/distributions/razorforge-rider-<version>.zip
gradlew runIde         # a sandbox Rider with the plugin loaded
```

Install the zip with Settings | Plugins | ⚙ | Install Plugin from Disk.

## Layout

```
build.gradle.kts             IntelliJ Platform Gradle plugin, copies the grammar into the plugin's bundle/
bundle/                      package.json and language-configuration.json of the TextMate bundle
src/main/kotlin/com/razorforge/rider/
  RazorForgeFiles.kt         TextMate bundle and file icon
  RazorForgeLanguageServer.kt  starts the server, runs it from a copy, restarts it after a rebuild
  RazorForgeSettings.kt      the server setting and its settings page
src/main/resources/META-INF/plugin.xml
```
