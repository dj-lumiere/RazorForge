package com.razorforge.lsp

import com.intellij.lang.Language
import com.intellij.openapi.fileTypes.LanguageFileType
import com.intellij.openapi.util.IconLoader
import javax.swing.Icon

/**
 * The two file languages this plugin contributes. They carry no PSI/parser of their own — the
 * IntelliJ LSP client handles semantics — but a registered [Language] + [LanguageFileType] is
 * what makes Rider open `.rf`/`.sf` as editable text and gives the LSP server support provider a
 * concrete file type to attach to.
 */
object RazorForgeLanguage : Language("RazorForge")

object SuflaeLanguage : Language("Suflae")

/** File icons, loaded from resources/icons (a `_dark` sibling is picked up for dark themes). */
private object Icons {
    val RAZORFORGE: Icon = IconLoader.getIcon("/icons/razorforge.svg", Icons::class.java)
    val SUFLAE: Icon = IconLoader.getIcon("/icons/suflae.svg", Icons::class.java)
}

object RazorForgeFileType : LanguageFileType(RazorForgeLanguage) {
    override fun getName(): String = "RazorForge"
    override fun getDescription(): String = "RazorForge source file"
    override fun getDefaultExtension(): String = "rf"
    override fun getIcon(): Icon = Icons.RAZORFORGE
}

object SuflaeFileType : LanguageFileType(SuflaeLanguage) {
    override fun getName(): String = "Suflae"
    override fun getDescription(): String = "Suflae source file"
    override fun getDefaultExtension(): String = "sf"
    override fun getIcon(): Icon = Icons.SUFLAE
}
