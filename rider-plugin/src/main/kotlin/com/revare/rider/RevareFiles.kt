package com.revare.rider

import com.intellij.ide.FileIconProvider
import com.intellij.ide.plugins.PluginManagerCore
import com.intellij.openapi.extensions.PluginId
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.IconLoader
import com.intellij.openapi.vfs.VirtualFile
import org.jetbrains.plugins.textmate.api.TextMateBundleProvider
import javax.swing.Icon

internal const val PLUGIN_ID = "com.revare.rider"

private val EXTENSIONS = setOf("rvr", "revare")

internal fun VirtualFile.isRevare(): Boolean = extension?.lowercase() in EXTENSIONS

/**
 * Registers the TextMate bundle shipped next to the plugin's jar: the grammar from Revare.tmbundle plus the comment
 * and bracket rules of language-configuration.json. `.rvr` files open as TextMate files, highlighted by that grammar.
 */
class RevareBundleProvider : TextMateBundleProvider {
    override fun getBundles(): List<TextMateBundleProvider.PluginBundle> {
        val plugin = PluginManagerCore.getPlugin(PluginId.getId(PLUGIN_ID)) ?: return emptyList()
        return listOf(TextMateBundleProvider.PluginBundle("Revare", plugin.pluginPath.resolve("bundle")))
    }
}

/** Gives `.rvr` files the Revare icon in place of the generic TextMate one. */
class RevareIconProvider : FileIconProvider {
    override fun getIcon(file: VirtualFile, flags: Int, project: Project?): Icon? =
        if (file.isRevare()) ICON else null

    internal companion object {
        val ICON: Icon = IconLoader.getIcon("/icons/revare.svg", RevareIconProvider::class.java)
    }
}
