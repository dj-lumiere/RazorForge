using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A binding from outside an <c>if</c>, a <c>when</c> or a loop is taken with <c>steal</c> on every path that goes on
/// past it, or on none (RF-S616): its end-of-scope teardown follows one rule, with no hidden flag.
/// </summary>
public class StealEveryPathTests
{
    private const string Prelude = """
                                   entity Node
                                       value: S64

                                   routine consume(n: Node) -> S64
                                       return n.value

                                   """;

    [Fact]
    public void Analyze_StealOnOneBranch_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine start()
                                                                       var n = Node(value: 1)
                                                                       if n.value > 0
                                                                           var v = consume(n: steal n)
                                                                       return
                                                                   """,
            expectedErrorSubstring: "on some paths through this 'if'");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.StealOnSomePaths);
    }

    [Fact]
    public void Analyze_StealOnEveryBranch_Passes()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           routine start()
                                               var n = Node(value: 1)
                                               if n.value > 0
                                                   var v = consume(n: steal n)
                                               else
                                                   var w = consume(n: steal n)
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_StealOnABranchThatLeaves_Passes()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           routine pick(flag: Bool) -> S64
                                               var n = Node(value: 1)
                                               if flag
                                                   return consume(n: steal n)
                                               return n.value
                                           """);
    }

    [Fact]
    public void Analyze_StealInALoopTurn_IsRejected()
    {
        AssertHasErrorSa(source: Prelude + """
                                           routine start()
                                               var n = Node(value: 1)
                                               var i = 0
                                               while i < 3
                                                   var v = consume(n: steal n)
                                                   i += 1
                                               return
                                           """,
            expectedErrorSubstring: "in a turn of this loop");
    }
}
