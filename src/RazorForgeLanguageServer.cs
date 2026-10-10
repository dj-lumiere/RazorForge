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

    /// <inheritdoc/>
    /// <remarks>From the improvement plan: `agent` in front of a call, `until` loops, `becomes` in an expression `when`.</remarks>
    public override IReadOnlyList<string> PlannedKeywords { get; } = ["agent", "until", "becomes", "inject"];

    /// <inheritdoc/>
    /// <remarks>From the improvement plan: `absent` / `lookup` go with their failure kinds,
    /// `threaded` / `suspended` give way to `agent` at the call.</remarks>
    public override IReadOnlyList<string> RetiredKeywords { get; } =
        ["absent", "lookup", "threaded", "suspended"];

    /// <inheritdoc/>
    public override string? Format(string text, string fileName)
    {
        return Builder.Formatting.SourceFormatter.Format(source: text, fileName: fileName, language: Language)
                      .Output;
    }
}
