package com.razorforge.rider

import com.intellij.DynamicBundle
import org.jetbrains.annotations.PropertyKey

private const val BUNDLE = "messages.RazorForgeBundle"

/** The plugin's text in the IDE's language (messages/RazorForgeBundle*.properties). */
internal object RazorForgeBundle : DynamicBundle(BUNDLE) {
    fun message(@PropertyKey(resourceBundle = BUNDLE) key: String, vararg params: Any): String = getMessage(key, *params)
}
