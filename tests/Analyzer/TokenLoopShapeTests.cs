using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A <c>for</c> loop over a token goes through the container the token is on (intent C68): changing that container's
/// shape in the loop is RF-S625, for a token from <c>xs.view()</c>, from a <c>viewing</c> block, and from a routine that
/// may give back any of the tokens handed to it.
/// </summary>
public class TokenLoopShapeTests
{
    private static void AssertReshapeError(string source, string expected)
    {
        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: expected);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReshapingDuringIteration);
    }

    [Fact]
    public void Analyze_ReshapeSourceWhileLoopingOverItsToken_Errors()
    {
        AssertReshapeError(source: """
                           routine start()
                               var a: List[S64] = [1, 2, 3]
                               var w = a.view()
                               for x in w
                                   a.add_last(value: x)
                               return
                           """, expected: "'w' is a token on 'a'");
    }

    [Fact]
    public void Analyze_ReshapeSourceWhileLoopingOverViewingAlias_Errors()
    {
        AssertReshapeError(source: """
                           routine start()
                               var a: List[S64] = [1, 2, 3]
                               viewing a as w
                                   for x in w
                                       a.add_last(value: x)
                               return
                           """, expected: "'w' is a token on 'a'");
    }

    [Fact]
    public void Analyze_ReshapeAnySourceOfReturnedToken_Errors()
    {
        AssertReshapeError(source: """
                           routine shorter(x: Viewing[List[S64]], y: Viewing[List[S64]]) -> Viewing[List[S64]]
                               if x.count() <= y.count()
                                   return x
                               return y

                           routine start()
                               var a: List[S64] = [1, 2, 3]
                               var b: List[S64] = [4]
                               var w = shorter(x: a.view(), y: b.view())
                               for x in w
                                   b.add_last(value: x)
                               return
                           """, expected: "'w' is a token on 'b'");
    }

    [Fact]
    public void Analyze_ReshapeOtherContainerWhileLoopingOverToken_Builds()
    {
        AssertAnalyzesSa(source: """
                         routine start()
                             var a: List[S64] = [1, 2, 3]
                             var b: List[S64] = []
                             var w = a.view()
                             for x in w
                                 b.add_last(value: x)
                             return
                         """);
    }
}
