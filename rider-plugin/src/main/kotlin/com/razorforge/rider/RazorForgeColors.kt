package com.razorforge.rider

import com.intellij.openapi.editor.DefaultLanguageHighlighterColors
import com.intellij.openapi.editor.colors.TextAttributesKey
import com.intellij.openapi.fileTypes.PlainSyntaxHighlighter
import com.intellij.openapi.fileTypes.SyntaxHighlighter
import com.intellij.openapi.options.colors.AttributesDescriptor
import com.intellij.openapi.options.colors.ColorDescriptor
import com.intellij.openapi.options.colors.ColorSettingsPage
import com.intellij.platform.lsp.api.customization.LspSemanticTokensSupport
import javax.swing.Icon

/**
 * RazorForge's own colors. Each starts as the C# color of its counterpart (record as struct, entity as class,
 * routine as method, module as namespace, preset as constant), so RazorForge reads like C# in any scheme until
 * Settings | Editor | Color Scheme | RazorForge says otherwise. A protocol is the exception: it
 * starts light blue and italic (colorSchemes/).
 */
object RazorForgeColors {
    val RECORD = key("RAZORFORGE_RECORD", TextAttributesKey.find("ReSharper.STRUCT_IDENTIFIER"))
    val ENTITY = key("RAZORFORGE_ENTITY", DefaultLanguageHighlighterColors.CLASS_NAME)
    val PROTOCOL = key("RAZORFORGE_PROTOCOL", DefaultLanguageHighlighterColors.INTERFACE_NAME)
    val GENERIC_PARAMETER = key("RAZORFORGE_GENERIC_PARAMETER", TextAttributesKey.find("ReSharper.TYPE_PARAMETER_IDENTIFIER"))
    val ROUTINE = key("RAZORFORGE_ROUTINE", DefaultLanguageHighlighterColors.INSTANCE_METHOD)
    val MODULE = key("RAZORFORGE_MODULE", TextAttributesKey.find("ReSharper.NAMESPACE_IDENTIFIER"))
    val PRESET = key("RAZORFORGE_PRESET", DefaultLanguageHighlighterColors.CONSTANT)
    val OPERATOR = key("RAZORFORGE_OPERATOR", DefaultLanguageHighlighterColors.OPERATION_SIGN)

    private fun key(name: String, csharp: TextAttributesKey) = TextAttributesKey.createTextAttributesKey(name, csharp)
}

/** The colors of the semantic token types the RazorForge language server sends that the platform doesn't know. */
internal object RazorForgeSemanticTokens : LspSemanticTokensSupport() {
    private val keys = mapOf(
        "recordType" to RazorForgeColors.RECORD,
        "entityType" to RazorForgeColors.ENTITY,
        "interface" to RazorForgeColors.PROTOCOL,
        "typeParameter" to RazorForgeColors.GENERIC_PARAMETER,
        "function" to RazorForgeColors.ROUTINE,
        "namespace" to RazorForgeColors.MODULE,
        "constant" to RazorForgeColors.PRESET,
        "operator" to RazorForgeColors.OPERATOR,
    )

    override val tokenTypes: List<String> = (super.tokenTypes + keys.keys).distinct()

    override fun getTextAttributesKey(tokenType: String, modifiers: List<String>): TextAttributesKey? =
        keys[tokenType] ?: super.getTextAttributesKey(tokenType, modifiers)
}

/** Settings | Editor | Color Scheme | RazorForge. */
class RazorForgeColorSettingsPage : ColorSettingsPage {
    private val descriptors = arrayOf(
        AttributesDescriptor("Types//Record, choice, flags, crashable", RazorForgeColors.RECORD),
        AttributesDescriptor("Types//Entity", RazorForgeColors.ENTITY),
        AttributesDescriptor("Types//Protocol", RazorForgeColors.PROTOCOL),
        AttributesDescriptor("Types//Generic parameter", RazorForgeColors.GENERIC_PARAMETER),
        AttributesDescriptor("Routine", RazorForgeColors.ROUTINE),
        AttributesDescriptor("Module", RazorForgeColors.MODULE),
        AttributesDescriptor("Preset", RazorForgeColors.PRESET),
        AttributesDescriptor("Operator", RazorForgeColors.OPERATOR),
    )

    private val tags = mapOf(
        "record" to RazorForgeColors.RECORD,
        "entity" to RazorForgeColors.ENTITY,
        "protocol" to RazorForgeColors.PROTOCOL,
        "generic" to RazorForgeColors.GENERIC_PARAMETER,
        "routine" to RazorForgeColors.ROUTINE,
        "module" to RazorForgeColors.MODULE,
        "preset" to RazorForgeColors.PRESET,
        "op" to RazorForgeColors.OPERATOR,
    )

    override fun getDisplayName(): String = "RazorForge"

    override fun getIcon(): Icon = RazorForgeIconProvider.ICON

    override fun getHighlighter(): SyntaxHighlighter = PlainSyntaxHighlighter()

    override fun getAttributeDescriptors(): Array<AttributesDescriptor> = descriptors

    override fun getColorDescriptors(): Array<ColorDescriptor> = ColorDescriptor.EMPTY_ARRAY

    override fun getAdditionalHighlightingTagToDescriptorMap(): Map<String, TextAttributesKey> = tags

    override fun getDemoText(): String = """
        import <module>IO</module>/<module>Console</module>

        preset <preset>LIMIT</preset>: <record>S64</record> = 100

        choice <record>Shape</record>
            CIRCLE
            SQUARE

        record <record>Point</record>
            x: <record>S64</record>
            y: <record>S64</record>

        entity <entity>Account</entity> obeys <protocol>Displayable</protocol>
            owner: <record>Text</record>
            history: <entity>List</entity>[<record>Point</record>]

        routine <routine>largest</routine>[<generic>T</generic>](items: <entity>List</entity>[<generic>T</generic>]) -> <generic>T</generic>
            var best = items.<routine>first</routine>()
            return best

        routine <routine>start</routine>()
            var total = <preset>LIMIT</preset> <op>+</op> 1
            <routine>show</routine>(total)
            return
    """.trimIndent()
}
