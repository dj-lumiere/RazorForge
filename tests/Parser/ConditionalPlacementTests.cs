using Builder.Diagnostics;
using SyntaxTree;

namespace RazorForge.Tests.Parser;

using static TestHelpers;

/// <summary>
/// A conditional value <c>if c then a else b</c> stands only as the whole value of <c>var x =</c>, <c>x =</c> or
/// <c>return</c>. Every other position (inside parentheses, an argument, an operand, a subscript, an f-text slot, a
/// branch of another conditional, after <c>try</c>/<c>check</c>) is RF-G112 with a message that points at a named
/// value or <c>when</c>.
/// </summary>
public sealed class ConditionalPlacementTests
{
    [Theory]
    [InlineData("var x = (if c then 4 else 3) + 1")]
    [InlineData("var x = 1 + if c then 4 else 3")]
    [InlineData("var x = (if c then 4 else 3)")]
    [InlineData("var x = pick(a: if c then 4 else 3, b: 1)")]
    [InlineData("var x = pick(a: 1, b: if c then 4 else 3)")]
    [InlineData("var x = Point(x: if c then 4 else 3, y: 1)")]
    [InlineData("var x = xs[if c then 0 else 1]")]
    [InlineData("var x = f\"{if c then 4 else 3}\"")]
    [InlineData("var x = if c then (if c then 1 else 2) else 3")]
    [InlineData("var x = if c then 1 else if c then 2 else 3")]
    [InlineData("var x = if (if c then true else false) then 1 else 2")]
    [InlineData("var x = not if c then true else false")]
    [InlineData("x += if c then 1 else 2")]
    [InlineData("show(if c then 1 else 2)")]
    [InlineData("var x = when\n        c => if c then 1 else 2\n        else => 3")]
    public void Parse_NestedConditional_ReportsG112(string line)
    {
        AssertRejected(source: Wrap(line: line), recovery: false);
    }

    [Theory]
    [InlineData("var x = try if c then f() else g()")]
    [InlineData("var x = check if c then f() else g()")]
    [InlineData("return try if c then f() else g()")]
    public void Parse_ConditionalAfterRecovery_ReportsG112(string line)
    {
        AssertRejected(source: Wrap(line: line), recovery: true);
    }

    [Theory]
    [InlineData("var x = if c then 4 else 3")]
    [InlineData("var x: S64 = if c then 4 + 1 else pick(a: 3, b: 1)")]
    [InlineData("var x = 0\n    x = if c then 4 else 3")]
    [InlineData("return if c then 4 else 3")]
    [InlineData("var (a, b) = if c then (1, 2) else (3, 4)")]
    [InlineData("var x = if c then try f() else 3")]
    public void Parse_TopLevelConditional_Parses(string line)
    {
        Program program = AssertParses(source: Wrap(line: line));
        Assert.NotNull(@object: program);
    }

    [Fact]
    public void Parse_ConditionalInWhenStatementArmReturn_Parses()
    {
        AssertParses(source: """
                             routine days(m: S64, leap: Bool) -> S64
                                 when m
                                     == 2 => return if leap then 29 else 28
                                     else => return 31
                             """);
    }

    private static string Wrap(string line)
    {
        return "routine probe(c: Bool) -> S64\n    " + line + "\n    return 0\n";
    }

    private static void AssertRejected(string source, bool recovery)
    {
        (Program _, Builder.Parser.Parser parser) = ParseWithErrors(source: source);
        GrammarException? error = parser.GetStructuredErrors()
                                        .FirstOrDefault(predicate: e => e.RawMessage.Contains(value: "conditional value"));
        Assert.NotNull(@object: error);
        Assert.Equal(expected: GrammarDiagnosticCode.ExpectedExpression, actual: error.Code);
        Assert.Contains(expectedSubstring: "'when'", actualString: error.RawMessage);
        if (recovery)
        {
            Assert.Contains(expectedSubstring: "in front of a conditional value", actualString: error.RawMessage);
        }
    }
}
