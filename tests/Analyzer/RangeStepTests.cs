using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// The sign of a range's step sets its direction (`10 to 0 by -1` counts down), so only a literal step of zero is a
/// build error (RF-S516) instead of a loop that never ends. A step held in a variable is checked when the range is
/// iterated (tests/Fixtures/ExitCode/range_step_zero.rf).
/// </summary>
public sealed class RangeStepTests
{
    [Theory]
    [InlineData("0 to 10 by 0")]
    [InlineData("0 to 10 by 0x0")]
    [InlineData("0.0 to 1.0 by 0.0")]
    [InlineData("0.0 to 1.0 by -0.0")]
    public void Analyze_LiteralStepZero_ReportsError(string range)
    {
        AnalysisResult result = AnalyzeSa(source: Walk(range: range));
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RangeStepZero);
    }

    [Theory]
    [InlineData("10 til 0 by -3")]
    [InlineData("10 til 0 by 3")]
    [InlineData("0 to 10 by 2")]
    [InlineData("0.0 to 1.0 by 0.25")]
    [InlineData("1.0 to 0.0 by -0.5")]
    public void Analyze_LiteralStepNonZero_NoError(string range)
    {
        AnalysisResult result = AnalyzeSa(source: Walk(range: range));
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RangeStepZero);
    }

    /// <summary>A step held in a variable is not a build error: it is checked when the range is iterated.</summary>
    [Fact]
    public void Analyze_VariableStep_NoError()
    {
        string source = """
                        routine walk(step: S64)
                            each i in 10 til 0 by step
                                nothing
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RangeStepZero);
    }

    /// <summary>A Char range is RF-S503: a character has no next one. The message says so and points to code points.</summary>
    [Fact]
    public void Analyze_CharRange_PointsToCodePoints()
    {
        AnalysisResult result = AnalyzeSa(source: Walk(range: "'a' to 'e'"));
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RangeBoundsNotNumeric &&
                         e.Message.Contains(value: "codepoint()") &&
                         e.Message.Contains(value: "Char.from_codepoint"));
    }

    private static string Walk(string range)
    {
        return "routine walk()\n" +
               $"    each i in {range}\n" +
               "        nothing\n" +
               "    return\n";
    }
}
