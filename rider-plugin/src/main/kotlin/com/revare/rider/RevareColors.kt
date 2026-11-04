package com.revare.rider

import com.intellij.openapi.editor.DefaultLanguageHighlighterColors
import com.intellij.openapi.editor.colors.CodeInsightColors
import com.intellij.openapi.editor.colors.TextAttributesKey
import com.intellij.openapi.fileTypes.PlainSyntaxHighlighter
import com.intellij.openapi.fileTypes.SyntaxHighlighter
import com.intellij.openapi.options.colors.AttributesDescriptor
import com.intellij.openapi.options.colors.ColorDescriptor
import com.intellij.openapi.options.colors.ColorSettingsPage
import com.intellij.platform.lsp.api.customization.LspSemanticTokensSupport
import javax.swing.Icon

/**
 * Revare's own colors. Each starts as the C# color of its counterpart (record as struct, entity as class,
 * routine as method, module as namespace, preset as constant), so Revare reads like C# in any scheme until
 * Settings | Editor | Color Scheme | Revare says otherwise. A carrier or variant whose kind follows its members
 * takes the color of what each use makes it. A protocol, an annotation and such a type not decided yet are the
 * exceptions: a protocol starts light blue and italic, an annotation yellow, a carrier or variant whose kind no
 * instantiation has decided yet pink (colorSchemes/). A decided one takes the record or the entity color.
 */
object RevareColors {
    val RECORD = key("REVARE_RECORD", TextAttributesKey.find("ReSharper.STRUCT_IDENTIFIER"))
    val ENTITY = key("REVARE_ENTITY", DefaultLanguageHighlighterColors.CLASS_NAME)
    val MEMBER_KIND = key("REVARE_MEMBER_KIND", TextAttributesKey.find("ReSharper.STRUCT_IDENTIFIER"))
    val PROTOCOL = key("REVARE_PROTOCOL", DefaultLanguageHighlighterColors.INTERFACE_NAME)
    val GENERIC_PARAMETER = key("REVARE_GENERIC_PARAMETER", TextAttributesKey.find("ReSharper.TYPE_PARAMETER_IDENTIFIER"))
    val ROUTINE = key("REVARE_ROUTINE", DefaultLanguageHighlighterColors.INSTANCE_METHOD)
    val MODULE = key("REVARE_MODULE", TextAttributesKey.find("ReSharper.NAMESPACE_IDENTIFIER"))
    val PRESET = key("REVARE_PRESET", DefaultLanguageHighlighterColors.CONSTANT)
    val OPERATOR = key("REVARE_OPERATOR", DefaultLanguageHighlighterColors.OPERATION_SIGN)
    val DOC_TAG = key("REVARE_DOC_TAG", DefaultLanguageHighlighterColors.DOC_COMMENT_TAG)
    val DOC_VALUE = key("REVARE_DOC_VALUE", DefaultLanguageHighlighterColors.DOC_COMMENT_TAG_VALUE)
    val ANNOTATION = key("REVARE_ANNOTATION", DefaultLanguageHighlighterColors.METADATA)

    /** A keyword the next version drops or renames: struck through over its keyword color. */
    val RETIRED = key("REVARE_RETIRED_KEYWORD", CodeInsightColors.DEPRECATED_ATTRIBUTES)

    private fun key(name: String, csharp: TextAttributesKey) = TextAttributesKey.createTextAttributesKey(name, csharp)
}

/** The colors of the semantic token types the Revare language server sends that the platform doesn't know. */
internal object RevareSemanticTokens : LspSemanticTokensSupport() {
    private val keys = mapOf(
        "recordType" to RevareColors.RECORD,
        "entityType" to RevareColors.ENTITY,
        "memberKindType" to RevareColors.MEMBER_KIND,
        "interface" to RevareColors.PROTOCOL,
        "typeParameter" to RevareColors.GENERIC_PARAMETER,
        "function" to RevareColors.ROUTINE,
        "namespace" to RevareColors.MODULE,
        "constant" to RevareColors.PRESET,
        "operator" to RevareColors.OPERATOR,
        "docTag" to RevareColors.DOC_TAG,
        "docValue" to RevareColors.DOC_VALUE,
        "decorator" to RevareColors.ANNOTATION,
    )

    override val tokenTypes: List<String> = (super.tokenTypes + keys.keys).distinct()

    override fun getTextAttributesKey(tokenType: String, modifiers: List<String>): TextAttributesKey? =
        if ("retired" in modifiers) RevareColors.RETIRED
        else keys[tokenType] ?: super.getTextAttributesKey(tokenType, modifiers)
}

/** Settings | Editor | Color Scheme | Revare. */
class RevareColorSettingsPage : ColorSettingsPage {
    private val descriptors = arrayOf(
        AttributesDescriptor(RevareBundle.message("color.types.record"), RevareColors.RECORD),
        AttributesDescriptor(RevareBundle.message("color.types.entity"), RevareColors.ENTITY),
        AttributesDescriptor(RevareBundle.message("color.types.member.kind"), RevareColors.MEMBER_KIND),
        AttributesDescriptor(RevareBundle.message("color.types.protocol"), RevareColors.PROTOCOL),
        AttributesDescriptor(RevareBundle.message("color.types.generic"), RevareColors.GENERIC_PARAMETER),
        AttributesDescriptor(RevareBundle.message("color.routine"), RevareColors.ROUTINE),
        AttributesDescriptor(RevareBundle.message("color.module"), RevareColors.MODULE),
        AttributesDescriptor(RevareBundle.message("color.preset"), RevareColors.PRESET),
        AttributesDescriptor(RevareBundle.message("color.operator"), RevareColors.OPERATOR),
        AttributesDescriptor(RevareBundle.message("color.doc.tag"), RevareColors.DOC_TAG),
        AttributesDescriptor(RevareBundle.message("color.doc.value"), RevareColors.DOC_VALUE),
        AttributesDescriptor(RevareBundle.message("color.annotation"), RevareColors.ANNOTATION),
    )

    private val tags = mapOf(
        "record" to RevareColors.RECORD,
        "entity" to RevareColors.ENTITY,
        "memberkind" to RevareColors.MEMBER_KIND,
        "protocol" to RevareColors.PROTOCOL,
        "generic" to RevareColors.GENERIC_PARAMETER,
        "routine" to RevareColors.ROUTINE,
        "module" to RevareColors.MODULE,
        "preset" to RevareColors.PRESET,
        "op" to RevareColors.OPERATOR,
        "annotation" to RevareColors.ANNOTATION,
    )

    override fun getDisplayName(): String = "Revare"

    override fun getIcon(): Icon = RevareIconProvider.ICON

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

        record <record>Pair</record>[<generic>A</generic>, <generic>B</generic>]
        needs RecordType <generic>A</generic>, RecordType <generic>B</generic>
            first: <generic>A</generic>
            second: <generic>B</generic>

        entity <entity>Account</entity> obeys <protocol>Displayable</protocol>
            owner: <record>Text</record>
            history: <entity>List</entity>[<record>Point</record>]
            backup: <entity>Account</entity><entity>?</entity>

        <annotation>@positional</annotation>
        routine <routine>largest</routine>[<generic>T</generic>](items: <entity>List</entity>[<generic>T</generic>], spare: <memberkind>Optional</memberkind>[<generic>T</generic>]) -> <generic>T</generic>
            var best = items.<routine>first</routine>()
            return best

        routine <routine>start</routine>()
            var total = <preset>LIMIT</preset> <op>+</op> 1
            <routine>show</routine>(total)
            return
    """.trimIndent()
}
