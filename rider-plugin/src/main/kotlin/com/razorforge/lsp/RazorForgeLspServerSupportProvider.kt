package com.razorforge.lsp

import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.openapi.diagnostic.logger
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.platform.lsp.api.LspServerSupportProvider
import com.intellij.platform.lsp.api.ProjectWideLspServerDescriptor
import java.io.File
import java.nio.charset.StandardCharsets

private val LOG = logger<RazorForgeLspServerSupportProvider>()

/**
 * One language's server: the file extensions it serves, the builder that runs it (`<Name>.dll lsp`),
 * and the environment variable that overrides where that DLL is.
 */
private enum class ServerLanguage(
    val displayName: String,
    val extensions: Set<String>,
    val dllName: String,
    val dllEnvVar: String,
) {
    RAZORFORGE("RazorForge", setOf("rf", "razorforge"), "RazorForge.dll", "RAZORFORGE_LSP_DLL"),
    SUFLAE("Suflae", setOf("sf"), "Suflae.dll", "SUFLAE_LSP_DLL");

    fun serves(file: VirtualFile): Boolean = file.extension?.lowercase() in extensions

    companion object {
        fun of(file: VirtualFile): ServerLanguage? = entries.firstOrNull { it.serves(file) }
    }
}

/**
 * Entry point Rider calls whenever a file is opened. Each language runs its own server, as in the
 * VS Code extension: a `.rf` file starts `RazorForge.dll lsp`, a `.sf` file starts `Suflae.dll lsp`.
 */
class RazorForgeLspServerSupportProvider : LspServerSupportProvider {
    override fun fileOpened(
        project: Project,
        file: VirtualFile,
        serverStarter: LspServerSupportProvider.LspServerStarter
    ) {
        val language = ServerLanguage.of(file) ?: return
        serverStarter.ensureServerStarted(LanguageLspServerDescriptor(project, language))
    }
}

/**
 * The project-wide server of one language (analysis is whole-program). Locates that language's
 * builder DLL, then runs it under `dotnet` with the `lsp` verb.
 */
private class LanguageLspServerDescriptor(project: Project, private val language: ServerLanguage) :
    ProjectWideLspServerDescriptor(project, language.displayName) {

    override fun isSupportedFile(file: VirtualFile): Boolean = language.serves(file)

    override fun createCommandLine(): GeneralCommandLine {
        val dll = resolveServerDll()
        LOG.info("Starting ${language.displayName} language server: dotnet \"$dll\" lsp")

        return GeneralCommandLine("dotnet", dll.absolutePath, "lsp").apply {
            // Run from the DLL's own folder so its sibling `Standard/` stdlib is found; the server
            // also honors the FORGE_STDLIB env override if you point it elsewhere.
            withWorkDirectory(dll.parentFile)
            // The server frames JSON-RPC as UTF-8.
            charset = StandardCharsets.UTF_8
        }
    }

    /**
     * Finds the language's DLL, in order:
     *   1. its environment variable (`RAZORFORGE_LSP_DLL` / `SUFLAE_LSP_DLL`, an absolute path), then
     *   2. the workspace dev build at `<project>/<Name>/bin/Debug/net10.0/<Name>.dll`.
     */
    private fun resolveServerDll(): File {
        System.getenv(language.dllEnvVar)
            ?.takeIf { it.isNotBlank() }
            ?.let { return File(it) }

        val base = project.basePath
            ?: error("Cannot locate ${language.dllName}: the project has no base path. " +
                "Set the ${language.dllEnvVar} environment variable to the DLL path.")

        val devBuild = File(base, "${language.displayName}/bin/Debug/net10.0/${language.dllName}")
        if (!devBuild.exists()) {
            LOG.warn("${language.dllName} not found at ${devBuild.absolutePath} — " +
                "build it (dotnet build) or set ${language.dllEnvVar}.")
        }
        return devBuild
    }
}
