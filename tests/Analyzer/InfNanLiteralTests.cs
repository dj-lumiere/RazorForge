using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// <c>inf</c> and <c>nan</c> are number literals: they take their type from where they go, every binary float and
/// the fixed decimal floats take them, and nothing else does.
/// </summary>
public class InfNanLiteralTests
{
    /// <summary>Verifies the places that give <c>inf</c> and <c>nan</c> a float type analyze cleanly.</summary>
    [Fact]
    public void FloatTargets_Analyze()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine half() -> B16
                                                      return nan

                                                  routine probe(x: B128) -> Bool
                                                      var a: B64 = inf
                                                      var b: B32 = -inf
                                                      var c: D64 = nan
                                                      var d: D128 = -inf
                                                      return x < inf
                                                  """);
        Assert.Empty(collection: result.Errors);
    }

    /// <summary>Verifies <c>inf</c> with nothing to give it a type is RF-S019.</summary>
    [Fact]
    public void NoType_IsRejected()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine probe()
                                                      var x = inf
                                                      return
                                                  """);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.SpecialFloatNeedsType);
    }

    /// <summary>Verifies <c>inf</c> and <c>nan</c> where a type without them goes is RF-S020.</summary>
    [Theory]
    [InlineData("var x: Decimal = inf")]
    [InlineData("var x: S64 = nan")]
    public void TypeWithoutThem_IsRejected(string statement)
    {
        AnalysisResult result = AnalyzeSa(source: $"""
                                                   routine probe()
                                                       {statement}
                                                       return
                                                   """);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.SpecialFloatNotInType);
    }

    /// <summary>Verifies the old width-carrying spelling is RF-G010 and names the new one.</summary>
    [Fact]
    public void WidthSpelling_IsAnError()
    {
        GrammarException exception = Assert.Throws<GrammarException>(
            testCode: () => Tokenize(source: "routine test()\n    var x = inf_b64\n    return\n"));
        Assert.Equal(expected: GrammarDiagnosticCode.TypeNamingSuffix, actual: exception.Code);
        Assert.Contains(expectedSubstring: "var x: B64 = inf", actualString: exception.RawMessage);
    }
}
