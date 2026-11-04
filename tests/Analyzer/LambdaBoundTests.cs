using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A lambda that reaches `me`, a token, a local collection or a `for` element is bound to it (C161): a local may hold
/// it, a routine may be handed it and call it, but it can't be kept in a member variable or returned. The error names
/// what the lambda is bound to and the line where that was made, and carries the names for the tools.
/// </summary>
public class LambdaBoundTests
{
    private const string Prelude = """
                                   import IO/Console
                                   import EmitterTools

                                   entity Board
                                       cells: List[S64]
                                       limit: S64

                                   record Holder
                                       callback: Callable[(S64,), Bool]

                                   routine call_with(f: Callable[(S64,), S64], x: S64) -> S64
                                       return f(x)

                                   """;

    [Fact]
    public void Bound_LocalPassedAndCalled_Analyzes()
    {
        AssertAnalyzes(source: Prelude + """
                                         routine Board.big_count() -> U64
                                             var above = (v: S64) => v > me.limit
                                             return me.cells.get_count(predicate: above)

                                         routine start()
                                             var xs = [10, 20, 30]
                                             var add_first = (v: S64) given xs => v + xs[0]
                                             show(f"{add_first(1)}")
                                             show(f"{call_with(f: add_first, x: 5)}")
                                             return
                                         """);
    }

    [Fact]
    public void Bound_ForElementToken_Analyzes()
    {
        AssertAnalyzes(source: Prelude + """
                                         routine total(boards: Viewing[List[Board]]) -> S64
                                             var sum: S64 = 0
                                             for board in boards
                                                 var plus = (k: S64) given board => board.limit + k
                                                 sum += call_with(f: plus, x: 1)
                                             return sum
                                         """);
    }

    [Fact]
    public void Bound_Returned_NamesWhatItIsBoundToAndWhereItWasMade()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine make() -> Callable[(S64,), S64]
                                                                       var xs = [1, 2]
                                                                       var f = (v: S64) given xs => v + xs[0]
                                                                       return f
                                                                   """,
            expectedErrorSubstring: "You capture 'xs' in this lambda and return it");
        SemanticError error = Assert.Single(collection: result.Errors.Where(predicate: e => e.Code == SemanticDiagnosticCode.LambdaCaptureRawEntity));
        Assert.Contains(expectedSubstring: "bound to 'xs' (made on line", actualString: error.Message);
        Assert.Equal(expected: ["xs"], actual: error.BoundTo);
    }

    [Fact]
    public void Bound_KeptInMemberVariable_IsRejected()
    {
        AssertHasErrorSa(source: Prelude + """
                                           routine hold() -> Holder
                                               var xs = [1, 2]
                                               return Holder(callback: (v: S64) given xs => v > xs[0])
                                           """,
            expectedErrorSubstring: "keep it in a 'Holder'");
    }

    [Fact]
    public void Bound_AssignedToField_IsRejected()
    {
        AssertHasErrorSa(source: Prelude + """
                                           routine Board.watch(holder: Modifying[List[Holder]])
                                               var f = (v: S64) => v > me.limit
                                               holder[0].callback = f
                                               return
                                           """,
            expectedErrorSubstring: "keep it in the member variable 'callback'");
    }

    [Fact]
    public void RecordMe_ReturnedLambda_StaysAllowed()
    {
        AssertAnalyzes(source: """
                               record Offset
                                   n: S64

                               routine Offset.adder() -> Callable[(S64,), S64]
                                   return (k: S64) => me.n + k
                               """);
    }
}
