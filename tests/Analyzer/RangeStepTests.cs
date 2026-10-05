using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A range's step is always a positive distance and its endpoints decide the direction, so a literal step that is
/// zero or negative is a build error (RF-S516) instead of a loop that never ends. A step held in a variable is
/// checked when the range is iterated (tests/Fixtures/ExitCode/range_step_not_positive.rf).
/// </summary>
public sealed class RangeStepTests
{
    [Theory]
    [InlineData("10 til 0 by -3")]
    [InlineData("0 to 10 by 0")]
    [InlineData("0 to 10 by 0x0")]
    [InlineData("0.0 to 1.0 by 0.0")]
    [InlineData("0.0 to 1.0 by -0.5")]
    public void Analyze_LiteralStepNotPositive_ReportsError(string range)
    {
        AnalysisResult result = AnalyzeSa(source: Walk(range: range));
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RangeStepNotPositive);
    }

    [Theory]
    [InlineData("10 til 0 by 3")]
    [InlineData("0 to 10 by 2")]
    [InlineData("0.0 to 1.0 by 0.25")]
    public void Analyze_LiteralStepPositive_NoError(string range)
    {
        AnalysisResult result = AnalyzeSa(source: Walk(range: range));
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RangeStepNotPositive);
    }

    /// <summary>A step held in a variable is not a build error: it is checked when the range is iterated.</summary>
    [Fact]
    public void Analyze_VariableStep_NoError()
    {
        string source = """
                        routine walk(step: S64)
                            each i in 10 til 0 by step
                                pass
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RangeStepNotPositive);
    }

    private static string Walk(string range)
    {
        return "routine walk()\n" +
               $"    each i in {range}\n" +
               "        pass\n" +
               "    return\n";
    }
}
