using Builder.Diagnostics;
using Builder.Tokenizer;

namespace Revare.Tests.Parser;

using static TestHelpers;

/// <summary>
/// The escapes of plain literals: inserted text has no <c>\x</c>, a plain literal keeps it for the place to read, and
/// <c>\u</c> takes only real code points. A <c>b</c> prefix is a grammar error.
/// </summary>
public class EscapeRuleTests
{
    private static string Wrap(string literal) => "routine start()\n    var s = " + literal + "\n    return\n";

    /// <summary>Inserted text refuses every <c>\x</c>, whatever its value.</summary>
    [Theory]
    [InlineData("f\"a{1}\\x00\"")]
    [InlineData("f\"\\xFF\"")]
    public void Text_HasNoHexEscape(string literal)
    {
        GrammarException error = Assert.Throws<GrammarException>(() => Tokenize(source: Wrap(literal: literal)));
        Assert.Contains(expectedSubstring: "\\u", actualString: error.Message);
    }

    /// <summary>A surrogate and a code point above U+10FFFF are refused in text.</summary>
    [Theory]
    [InlineData("\"\\u00D800\"")]
    [InlineData("\"\\u00DFFF\"")]
    [InlineData("\"\\u110000\"")]
    [InlineData("'\\u00D800'")]
    public void Text_RefusesNonScalarUnicodeEscape(string literal)
    {
        Assert.Throws<GrammarException>(() => Tokenize(source: Wrap(literal: literal)));
    }

    /// <summary>The ends of the valid range are accepted.</summary>
    [Fact]
    public void Text_AcceptsScalarEdges()
    {
        List<Token> tokens = Tokenize(source: Wrap(literal: "\"\\u00D7FF\\u00E000\\u10FFFF\""));
        Assert.Contains(collection: tokens, filter: t => t.Type == TokenType.TextLiteral);
    }

    /// <summary>A <c>\x</c> in plain text or a character stays in the token for the place to read.</summary>
    [Theory]
    [InlineData("\"\\x41\"")]
    [InlineData("\"\\xFF\"")]
    [InlineData("'\\x41'")]
    public void PlainLiteral_KeepsHexEscapeForThePlace(string literal)
    {
        Assert.Contains(collection: Tokenize(source: Wrap(literal: literal)),
            filter: t => t.Type is TokenType.TextLiteral or TokenType.CharacterLiteral);
    }

    /// <summary>A text literal's value holds each <c>\xHH</c> as the lone surrogate U+DC00 + HH.</summary>
    [Fact]
    public void PlainText_HoldsHexEscapeAsLoneSurrogate()
    {
        Token token = Tokenize(source: Wrap(literal: "\"a\\x00\\x80\\xFF\""))
           .Single(predicate: t => t.Type == TokenType.TextLiteral);
        Assert.Equal(expected: "a\uDC00\uDC80\uDCFF", actual: token.Text);
    }

    /// <summary>Non-ASCII characters stay characters in the token (the place turns them into UTF-8).</summary>
    [Fact]
    public void PlainText_KeepsNonAsciiCharacters()
    {
        Token token = Tokenize(source: Wrap(literal: "\"한국어\""))
           .Single(predicate: t => t.Type == TokenType.TextLiteral);
        Assert.Equal(expected: "한국어", actual: token.Text);
    }

    /// <summary>A literal with a <c>b</c> prefix is a grammar error that says what to write.</summary>
    [Theory]
    [InlineData("b\"hi\"")]
    [InlineData("br\"hi\\n\"")]
    [InlineData("b'a'")]
    [InlineData("b'\\xFF'")]
    public void BytePrefix_IsAGrammarError(string literal)
    {
        GrammarException error = Assert.Throws<GrammarException>(() => Tokenize(source: Wrap(literal: literal)));
        Assert.Equal(expected: GrammarDiagnosticCode.ByteLiteralPrefix, actual: error.Code);
        Assert.Contains(expectedSubstring: "Byte('x')", actualString: error.Message);
    }
}
