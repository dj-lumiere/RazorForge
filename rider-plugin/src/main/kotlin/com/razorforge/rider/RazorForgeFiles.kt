package com.razorforge.rider

import com.intellij.ide.FileIconProvider
import com.intellij.ide.plugins.PluginManagerCore
import com.intellij.openapi.extensions.PluginId
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.IconLoader
import com.intellij.openapi.vfs.VirtualFile
import org.jetbrains.plugins.textmate.api.TextMateBundleProvider
import javax.swing.Icon

internal const val PLUGIN_ID = "com.razorforge.rider"

private val EXTENSIONS = setOf("rf", "razorforge")

internal fun VirtualFile.isRazorForge(): Boolean = extension?.lowercase() in EXTENSIONS

/**
 * Registers the TextMate bundle shipped next to the plugin's jar: the grammar from RazorForge.tmbundle plus the comment
 * and bracket rules of language-configuration.json. `.rf` files open as TextMate files, highlighted by that grammar.
 */
class RazorForgeBundleProvider : TextMateBundleProvider {
    override fun getBundles(): List<TextMateBundleProvider.PluginBundle> {
        val plugin = PluginManagerCore.getPlugin(PluginId.getId(PLUGIN_ID)) ?: return emptyList()
        return listOf(TextMateBundleProvider.PluginBundle("RazorForge", plugin.pluginPath.resolve("bundle")))
    }
}

/** Gives `.rf` files the RazorForge icon in place of the generic TextMate one. */
class RazorForgeIconProvider : FileIconProvider {
    override fun getIcon(file: VirtualFile, flags: Int, project: Project?): Icon? =
        if (file.isRazorForge()) ICON else null

    internal companion object {
        val ICON: Icon = IconLoader.getIcon("/icons/razorforge.svg", RazorForgeIconProvider::class.java)
    }
}
