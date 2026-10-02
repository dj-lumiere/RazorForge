import org.jetbrains.intellij.platform.gradle.IntelliJPlatformType

plugins {
    id("java")
    // Kotlin no newer than the one the target Rider bundles (262 ships 2.4.0), since the plugin runs on Rider's own stdlib.
    id("org.jetbrains.kotlin.jvm") version "2.4.0"
    id("org.jetbrains.intellij.platform") version "2.19.0"
}

group = "com.razorforge"
version = "0.2.0"

repositories {
    mavenCentral()
    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        // `riderLocalPath` (e.g. in ~/.gradle/gradle.properties) builds against an installed Rider instead of
        // downloading the one named by `platformVersion`.
        val localRider = providers.gradleProperty("riderLocalPath").orNull
        if (localRider != null) {
            local(localRider)
        } else {
            create(IntelliJPlatformType.Rider, providers.gradleProperty("platformVersion"))
        }
        bundledPlugin("org.jetbrains.plugins.textmate")
    }
}

intellijPlatform {
    // No .form files, so there is nothing to instrument.
    instrumentCode = false
    buildSearchableOptions = false

    pluginConfiguration {
        ideaVersion {
            // 262 is the first branch with LspIntegrationProvider / ProjectWideLspClientDescriptor.
            sinceBuild = "262"
            untilBuild = provider { null }
        }
    }
}

tasks {
    // The TextMate bundle sits next to the plugin's jar: the grammar comes straight from ../RazorForge.tmbundle (its one
    // source), the VS Code-style package.json and language-configuration.json from bundle/.
    prepareSandbox {
        from(layout.projectDirectory.dir("bundle")) {
            into(pluginName.map { "$it/bundle" })
        }
        from(layout.projectDirectory.dir("../RazorForge.tmbundle/Syntaxes")) {
            into(pluginName.map { "$it/bundle/syntaxes" })
        }
    }
}
