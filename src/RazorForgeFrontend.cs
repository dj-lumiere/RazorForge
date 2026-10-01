using Builder.Tokenizer;
using RazorForge.Lexer;
using TypeModel.Enums;

namespace RazorForge;

/// <summary>The RazorForge front end: registers the RazorForge lexer with the builder core.</summary>
public static class RazorForgeFrontend
{
    /// <summary>Registers the RazorForge lexer (idempotent).</summary>
    public static void Register()
    {
        Lexers.Register(language: Language.RazorForge,
            tokenize: (source, fileName) => new RazorForgeLexer(source: source, fileName: fileName).Tokenize());
    }
}
