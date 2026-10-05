package com.razorforge.rider

import com.intellij.codeInsight.actions.ReformatCodeProcessor
import com.intellij.openapi.actionSystem.ActionManager
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.AnActionWrapper
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.actionSystem.impl.ActionConfigurationCustomizer
import com.intellij.psi.PsiFile

/**
 * Rider's Reformat and Cleanup (Ctrl+Alt+L by default, and its dialog form) runs ReSharper's cleanup, which knows
 * nothing of RazorForge files. For a RazorForge file it reformats through the language server instead, so the one command
 * formats every language. Any other file goes to Rider's own cleanup unchanged.
 */
class RazorForgeCleanupActions : ActionConfigurationCustomizer {
    override fun customize(actionManager: ActionManager) {
        for (id in listOf("SilentCodeCleanup", "CodeCleanup")) {
            val original = actionManager.getAction(id) ?: continue
            actionManager.replaceAction(id, RazorForgeCleanup(original))
        }
    }
}

private class RazorForgeCleanup(original: AnAction) : AnActionWrapper(original) {
    override fun update(e: AnActionEvent) {
        if (ownFile(e) != null) e.presentation.isEnabledAndVisible = true else super.update(e)
    }

    override fun actionPerformed(e: AnActionEvent) {
        val file = ownFile(e) ?: return super.actionPerformed(e)
        ReformatCodeProcessor(file, false).run()
    }

    private fun ownFile(e: AnActionEvent): PsiFile? =
        e.getData(CommonDataKeys.PSI_FILE)?.takeIf { it.virtualFile?.isRazorForge() == true }
}
