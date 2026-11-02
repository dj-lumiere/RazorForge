using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// <c>S32(3)</c>: a number type called with one number written out reads that number as the type, checked against the
/// type's range. A variable (<c>S32(n)</c>) is not a number written out and keeps the error pointing to <c>n.to[S32]()</c>.
/// </summary>
public class NumberLiteralOfTypeTests
{
    [Fact]
    public void Analyze_NumberTypeCalledWithALiteral_IsThatType()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                          choice Step
                                              CALL
                                              VISIT

                                          routine start()
                                              var stack = [(Step.CALL, S32(3), 'a')]
                                              var small = U8(255)
                                              var neg = S16(-7)
                                              var half = B64(0.5)
                                              return
                                          """);

        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void Analyze_NumberTypeCalledWithALiteralOutOfRange_ReportsTheRangeOnce()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                          routine start()
                                              var b = U8(300)
                                              return
                                          """);

        SemanticError error = Assert.Single(collection: result.Errors);
        Assert.Contains(expectedSubstring: "overflows type 'U8'", actualString: error.Message);
    }

    [Fact]
    public void Analyze_NumberTypeCalledWithAVariable_PointsToTheConversion()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                          routine start()
                                              var n = 5
                                              var a = S32(n)
                                              return
                                          """);

        Assert.Contains(collection: result.Errors, filter: e => e.Message.Contains(value: "n.to[S32]()"));
    }
}
