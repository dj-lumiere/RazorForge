using Builder.Diagnostics;
using Builder.Tokenizer;

namespace Revare.Tests.Parser;

using static TestHelpers;

/// <summary>
/// The escapes by what the literal makes: text has no <c>\x</c> and takes only real code points in <c>\u</c>, a byte
/// string takes <c>\x00</c> to <c>\xFF</c> as bytes and turns a non-ASCII character into its UTF-8 bytes.
/// </summary>
public class EscapeRuleTests
{
    private static string Wrap(string literal) => "routine start()\n    var s = " + literal + "\n    return\n";

    /// <summary>Text refuses every <c>\x</c>, whatever its value.</summary>
    [Theory]
    [InlineData("\"\\x41\"")]
    [InlineData("\"\\xFF\"")]
    [InlineData("f\"a{1}\\x00\"")]
    [InlineData("'\\x41'")]
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

    /// <summary>A byte string holds <c>\x00</c> to <c>\xFF</c> as single bytes.</summary>
    [Fact]
    public void Bytes_TakeHexEscapesAsBytes()
    {
        Token token = Tokenize(source: Wrap(literal: "b\"\\x00\\x80\\xFF\""))
           .Single(predicate: t => t.Type == TokenType.BytesLiteral);
        Assert.Equal(expected: "\u0000\u0080\u00FF", actual: token.Text);
    }

    /// <summary>A non-ASCII character in a byte string is its UTF-8 bytes.</summary>
    [Fact]
    public void Bytes_TakeNonAsciiCharactersAsUtf8()
    {
        Token token = Tokenize(source: Wrap(literal: "b\"한국어\""))
           .Single(predicate: t => t.Type == TokenType.BytesLiteral);
        Assert.Equal(expected: 9, actual: token.Text.Length);
        Assert.Equal(expected: System.Text.Encoding.UTF8.GetBytes(s: "한국어"),
            actual: token.Text.Select(selector: c => (byte)c).ToArray());
    }

    /// <summary>A byte character takes <c>\xFF</c>, and refuses a non-ASCII character.</summary>
    [Fact]
    public void ByteCharacter_TakesHexEscape()
    {
        Assert.Contains(collection: Tokenize(source: Wrap(literal: "b'\\xFF'")),
            filter: t => t.Type == TokenType.ByteLetterLiteral);
        Assert.Throws<GrammarException>(() => Tokenize(source: Wrap(literal: "b'é'")));
    }
}
