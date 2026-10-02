using Builder.Tokenizer;
using SyntaxTree;

namespace RazorForge.Tests.Parser;

using static TestHelpers;

/// <summary>
/// The grammar the canonical layout relies on: a comma before the closing bracket of a broken list, a call chain
/// broken before each <c>.</c>, <c>have</c>/<c>lack</c> arms in a <c>when</c> statement, checked expand sources,
/// and <c>\u</c> escapes above U+FFFF.
/// </summary>
public class LayoutGrammarTests
{
    /// <summary>Verifies that a comma before <c>)</c> ends an argument list instead of starting an argument.</summary>
    [Fact]
    public void Parse_TrailingCommaInArguments()
    {
        Program program = AssertParses(source: """
                                               routine start()
                                                   var total = add(
                                                       a: 1,
                                                       b: 2,
                                                   )
                                                   return
                                               """);
        var routine = GetDeclaration<RoutineDeclaration>(program: program);
        var declaration = (DeclarationStatement)((BlockStatement)routine.Body).Statements[index: 0];
        var call = (CallExpression)((VariableDeclaration)declaration.Declaration).Initializer!;
        Assert.Equal(expected: 2, actual: call.Arguments.Count);
    }

    /// <summary>Verifies that a comma before <c>)</c> ends a parameter list.</summary>
    [Fact]
    public void Parse_TrailingCommaInParameters()
    {
        Program program = AssertParses(source: """
                                               routine add(
                                                   a: S32,
                                                   b: S32,
                                               ) -> S32
                                                   return a + b
                                               """);
        Assert.Equal(expected: 2, actual: GetDeclaration<RoutineDeclaration>(program: program).Parameters.Count);
    }

    /// <summary>Verifies that list, set and dict literals take a comma before their closing bracket.</summary>
    [Fact]
    public void Parse_TrailingCommaInCollectionLiterals()
    {
        Program program = AssertParses(source: """
                                               routine start()
                                                   var xs = [1, 2,]
                                                   var s = {1, 2,}
                                                   var d = {1: 2, 3: 4,}
                                                   return
                                               """);
        var statements = ((BlockStatement)GetDeclaration<RoutineDeclaration>(program: program).Body).Statements;
        Assert.Equal(expected: 2,
            actual: ((ListLiteralExpression)Initializer(statement: statements[index: 0])).Elements.Count);
        Assert.Equal(expected: 2,
            actual: ((SetLiteralExpression)Initializer(statement: statements[index: 1])).Elements.Count);
        Assert.Equal(expected: 2,
            actual: ((DictLiteralExpression)Initializer(statement: statements[index: 2])).Pairs.Count);
    }

    /// <summary>Verifies that a line starting with <c>.</c> continues the call chain of the line before it.</summary>
    [Fact]
    public void Parse_CallChainBrokenBeforeDots()
    {
        Program program = AssertParses(source: """
                                               routine start()
                                                   var t = name
                                                       .trim()
                                                       .upper()
                                                   return
                                               """);
        var statements = ((BlockStatement)GetDeclaration<RoutineDeclaration>(program: program).Body).Statements;
        Assert.Equal(expected: 2, actual: statements.Count);
        var outer = (CallExpression)Initializer(statement: statements[index: 0]);
        Assert.Equal(expected: "upper", actual: ((MemberExpression)outer.Callee).MemberName);
    }

    /// <summary>Verifies that a leading <c>.</c> emits no layout token.</summary>
    [Fact]
    public void Tokenize_LeadingDotLineEmitsNoLayoutToken()
    {
        List<Token> tokens = Tokenize(source: "routine start()\n    var t = a\n        .b()\n    return\n");
        int dot = tokens.FindIndex(match: t => t.Type == TokenType.Dot);
        Assert.Equal(expected: TokenType.Identifier, actual: tokens[index: dot - 1].Type);
    }

    /// <summary>Verifies that a <c>when</c> statement takes <c>have</c> and <c>lack</c> arms.</summary>
    [Fact]
    public void Parse_FlagsArmsInWhenStatement()
    {
        Program program = AssertParses(source: """
                                               routine check(p: Perm)
                                                   when p
                                                       have READ and WRITE => pass
                                                       lack EXEC => pass
                                                       else => pass
                                                   return
                                               """);
        var when = (WhenStatement)((BlockStatement)GetDeclaration<RoutineDeclaration>(program: program).Body)
           .Statements[index: 0];
        Assert.IsType<FlagsPattern>(@object: when.Clauses[index: 0].Pattern);
        Assert.True(condition: ((FlagsPattern)when.Clauses[index: 1].Pattern).IsNegated);
    }

    /// <summary>Verifies that a misspelled arm-expansion source is a grammar error, not read as <c>branchof</c>.</summary>
    [Fact]
    public void Parse_UnknownArmExpansionSourceIsError()
    {
        AssertParseError(source: """
                                 routine T.show() -> Text
                                     when me
                                         expand m in brnchof(T)
                                             is $typeof(m) v => return "x"
                                     return "y"
                                 """);
    }

    /// <summary>Verifies that a <c>\u</c> escape above U+FFFF keeps its whole code point.</summary>
    [Fact]
    public void Tokenize_UnicodeEscapeAboveBmp()
    {
        List<Token> tokens = Tokenize(source: "routine start()\n    var s = \"\\u01F600\"\n    return\n");
        Token text = tokens.First(predicate: t => t.Type == TokenType.TextLiteral);
        Assert.Equal(expected: char.ConvertFromUtf32(utf32: 0x1F600), actual: text.Text);
    }

    private static Expression Initializer(Statement statement)
    {
        return ((VariableDeclaration)((DeclarationStatement)statement).Declaration).Initializer!;
    }
}
