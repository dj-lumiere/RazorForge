using Builder.Frontends;
using RazorForge.Lexer;
using TypeModel.Enums;

namespace RazorForge;

/// <summary>
/// The RazorForge Language Server: the shared engine (<c>Builder.Execution.LspServer</c>) run by <c>razorforge lsp</c>,
/// serving <c>.rf</c> files with RazorForge's own keywords and names.
/// </summary>
internal sealed class RazorForgeLanguageServer : LanguageServerProfile
{
    /// <inheritdoc/>
    public override Language Language => Language.RazorForge;

    /// <inheritdoc/>
    public override string ServerName => "razorforge-lsp";

    /// <inheritdoc/>
    public override string CodeBlockLanguage => "razorforge";

    /// <inheritdoc/>
    public override IReadOnlyList<string> Keywords => RazorForgeLexer.KeywordSpellings;
}
