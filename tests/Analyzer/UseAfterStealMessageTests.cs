using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// RF-S615, a use of a name after <c>steal</c> moved its value out: the message names the line of the steal, where
/// the value went (a binding, a field, or a call), and the way out (use the new owner, or use the name before
/// the steal).
/// </summary>
public class UseAfterStealMessageTests
{
    [Fact]
    public void Analyze_UseAfterStealIntoBinding_NamesLineAndNewOwner()
    {
        AnalysisResult result = AssertHasErrorSa(source: """
                                                         entity Node
                                                             value: S64

                                                         routine start()
                                                             var mine = Node(value: 1)
                                                             var held = steal mine
                                                             var v = mine.value
                                                             return
                                                         """,
            expectedErrorSubstring:
            "You use 'mine' here, but 'steal' on line 6 handed what it held over to 'held'. " +
            "Use 'held', or use 'mine' before line 6.");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.UseAfterSteal);
    }

    [Fact]
    public void Analyze_UseAfterStealIntoCall_NamesLineAndCall()
    {
        AnalysisResult result = AssertHasErrorSa(source: """
                                                         entity Node
                                                             value: S64

                                                         routine keep(n: Node)
                                                             return

                                                         routine start()
                                                             var mine = Node(value: 1)
                                                             keep(n: steal mine)
                                                             var v = mine.value
                                                             return
                                                         """,
            expectedErrorSubstring:
            "You use 'mine' here, but 'steal' on line 9 handed what it held over to the call to 'keep'. " +
            "Use 'mine' before line 9, or give 'mine' a new value first.");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.UseAfterSteal);
    }

    [Fact]
    public void Analyze_UseAfterStealIntoAssignedPlace_NamesThePlace()
    {
        AssertHasErrorSa(source: """
                                 entity Node
                                     value: S64

                                 routine start()
                                     var mine = Node(value: 1)
                                     var slot = Node(value: 2)
                                     slot = steal mine
                                     var v = mine.value
                                     return
                                 """,
            expectedErrorSubstring: "'steal' on line 7 handed what it held over to 'slot'. Use 'slot'");
    }
}
