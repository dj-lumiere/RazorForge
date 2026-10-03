package com.razorforge.rider

import com.intellij.lang.Language
import com.intellij.openapi.fileTypes.PlainSyntaxHighlighter
import com.intellij.openapi.fileTypes.SyntaxHighlighter
import com.intellij.openapi.fileTypes.SyntaxHighlighterFactory
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import org.jetbrains.plugins.textmate.TextMateService
import org.jetbrains.plugins.textmate.language.syntax.highlighting.TextMateHighlighter
import org.jetbrains.plugins.textmate.language.syntax.lexer.TextMateHighlightingLexer

/**
 * RazorForge as a language Rider can name. Files stay with the TextMate grammar; this is what a hover's ```razorforge code block
 * is highlighted as, since Rider colors a code block only for a registered language with a highlighter.
 */
object RazorForgeLanguage : Language("razorforge") {
    private fun readResolve(): Any = RazorForgeLanguage

    override fun getDisplayName(): String = "RazorForge"

    /** A language is registered when its object loads: called before anything can ask for it. */
    fun ensureRegistered() = Unit
}

/** Highlights RazorForge code outside a file (a hover's code block) with the same TextMate grammar the editor uses. */
class RazorForgeSyntaxHighlighterFactory : SyntaxHighlighterFactory() {
    override fun getSyntaxHighlighter(project: Project?, virtualFile: VirtualFile?): SyntaxHighlighter =
        TextMateService.getInstance().getLanguageDescriptorByExtension("rf")
            ?.let { TextMateHighlighter(TextMateHighlightingLexer(it, LINE_LIMIT)) }
            ?: PlainSyntaxHighlighter()

    private companion object {
        /** Longer lines than this are left plain, as TextMate leaves them in the editor. */
        const val LINE_LIMIT = 10_000
    }
}
