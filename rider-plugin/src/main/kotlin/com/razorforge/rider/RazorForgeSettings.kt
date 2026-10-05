package com.razorforge.rider

import com.intellij.openapi.components.BaseState
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.SimplePersistentStateComponent
import com.intellij.openapi.components.State
import com.intellij.openapi.components.Storage
import com.intellij.openapi.components.service
import com.intellij.openapi.fileChooser.FileChooserDescriptorFactory
import com.intellij.openapi.options.BoundConfigurable
import com.intellij.openapi.project.ProjectManager
import com.intellij.openapi.ui.DialogPanel
import com.intellij.ui.dsl.builder.AlignX
import com.intellij.ui.dsl.builder.bindText
import com.intellij.ui.dsl.builder.panel

/** Which RazorForge language server to run. Empty means the workspace dev build (see `locateServer`). */
@Service(Service.Level.APP)
@State(name = "RazorForgeSettings", storages = [Storage("razorforge.xml")])
internal class RazorForgeSettings : SimplePersistentStateComponent<RazorForgeSettings.Options>(Options()) {
    class Options : BaseState() {
        var serverPath by string()
    }

    var serverPath: String
        get() = state.serverPath.orEmpty()
        set(value) {
            state.serverPath = value.trim().ifEmpty { null }
        }

    companion object {
        fun getInstance(): RazorForgeSettings = service()
    }
}

/** Settings | Languages & Frameworks | RazorForge. */
internal class RazorForgeConfigurable : BoundConfigurable("RazorForge") {
    private val settings = RazorForgeSettings.getInstance()

    override fun createPanel(): DialogPanel = panel {
        row(RazorForgeBundle.message("settings.label")) {
            textFieldWithBrowseButton(FileChooserDescriptorFactory.singleFile().withTitle(RazorForgeBundle.message("settings.title")))
                .bindText(settings::serverPath)
                .align(AlignX.FILL)
                .comment(RazorForgeBundle.message("settings.comment"))
        }
    }

    override fun apply() {
        super.apply()
        ProjectManager.getInstance().openProjects.forEach(RazorForgeLanguageServer::restart)
    }
}
