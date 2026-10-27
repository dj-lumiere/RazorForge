using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A token (<c>Viewing[T]</c> / <c>Modifying[T]</c>) is never a type argument (RF-S601): a container, a <c>Optional</c>, a
/// tuple, a variant or a generic type holding one could keep it after the place it points at is gone. A routine may
/// return <c>Optional</c> of a token (an iterator's <c>emit</c> under <c>try</c>), and the caller unpacks it with
/// <c>when</c> where it gets it. A lambda never captures a token (RF-S606). The first five cases are the analogs of
/// Rust #164250 that read a dead entity before.
/// </summary>
public class TokenTypeArgumentTests
{
    private const string Prelude = """
                                   import IO/Console

                                   entity Cell
                                       n: S64

                                   """;

    private static void AssertHasErrorS601(string source, string expected)
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + source, expectedErrorSubstring: expected);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenMemberVariableNotAllowed);
    }

    [Fact]
    public void Analyze_EntityFieldListOfTokens_MovedByDestruct_Errors()
    {
        AssertHasErrorS601(source: """
                                        entity Q
                                            held: List[Viewing[Cell]]

                                        global KEPT: Guarded[List[Viewing[Cell]], Exclusive] = Guarded[List[Viewing[Cell]], Exclusive](from: List[Viewing[Cell]]())

                                        routine Q.destruct()
                                            modifying KEPT as k
                                                each v in me.held
                                                    k.add_last(value: v)
                                            return

                                        routine start()
                                            var l = Cell(n: 42)
                                            var q = Q(held: List[Viewing[Cell]]())
                                            q.held.add_last(value: l.view())
                                            return
                                        """,
            expected: "for member variable 'held'");
    }

    [Fact]
    public void Analyze_ModifyingListOfTokensParameter_Errors()
    {
        AssertHasErrorS601(source: """
                                        routine fill(xs: Modifying[List[Viewing[Cell]]])
                                            var l = Cell(n: 42)
                                            xs.add_last(value: l.view())
                                            return

                                        routine start()
                                            return
                                        """,
            expected: "for parameter 'xs'");
    }

    [Fact]
    public void Analyze_MaybeOfTokenInVariable_Errors()
    {
        AssertHasErrorS601(source: """
                                        routine start()
                                            var m: Optional[Viewing[Cell]] = none
                                            if true
                                                var l = Cell(n: 42)
                                                m = l.view()
                                            when m
                                                is None =>
                                                    show("none")
                                                else as v =>
                                                    show(f"{v.n}")
                                            return
                                        """,
            expected: "Unpack it with `when` where you get it");
    }

    [Fact]
    public void Analyze_LocalListOfTokens_Errors()
    {
        AssertHasErrorS601(source: """
                                        routine start()
                                            var xs = List[Viewing[Cell]]()
                                            if true
                                                var l = Cell(n: 42)
                                                xs.add_last(value: l.view())
                                            show(f"{xs[0].n}")
                                            return
                                        """,
            expected: "`Viewing[Cell]` in it is a token");
    }

    [Fact]
    public void Analyze_LambdaCapturingToken_Errors()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine make() -> Callable[(), S64]
                                                                       var l = Cell(n: 42)
                                                                       var v = l.view()
                                                                       return () given (v) => v.n

                                                                   routine start()
                                                                       var f = make()
                                                                       show(f"{f()}")
                                                                       return
                                                                   """,
            expectedErrorSubstring: "You capture 'v' in this lambda");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.LambdaCaptureToken);
    }

    [Fact]
    public void Analyze_InferredListLiteralOfTokens_Errors()
    {
        AssertHasErrorS601(source: """
                                        routine start()
                                            var l = Cell(n: 42)
                                            var xs = [l.view()]
                                            return
                                        """,
            expected: "You use `List[Viewing[Cell]]` for this value");
    }

    [Fact]
    public void Analyze_TupleHoldingToken_Errors()
    {
        AssertHasErrorS601(source: """
                                        routine start()
                                            var l = Cell(n: 42)
                                            var t = (l.view(), 1)
                                            return
                                        """,
            expected: "You use `Tuple[Viewing[Cell], S64]` for this value");
    }

    [Fact]
    public void Analyze_UserGenericTypeWithTokenArgument_Errors()
    {
        AssertHasErrorS601(source: """
                                        entity Box[T]
                                            v: T

                                        routine start()
                                            var l = Cell(n: 42)
                                            var b = Box(v: l.view())
                                            return
                                        """,
            expected: "You use `Box[Viewing[Cell]]` for this value");
    }

    [Fact]
    public void Analyze_VariantCaseHoldingTokenList_Errors()
    {
        AssertHasErrorS601(source: """
                                        variant Held
                                            List[Viewing[Cell]]
                                            S64

                                        routine start()
                                            return
                                        """,
            expected: "for variant case");
    }

    [Fact]
    public void Analyze_TryOfTokenUnpackedWithWhen_Analyzes()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           routine start()
                                               var cells = [Cell(n: 1), Cell(n: 2)]
                                               when try cells.view_at(index: 0)
                                                   is None =>
                                                       show("none")
                                                   else as c =>
                                                       show(f"{c.n}")
                                               each pair in cells.enumerate()
                                                   show(f"{pair.item0}")
                                               each (i, c) in cells.enumerate()
                                                   show(f"{i} {c.n}")
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_TryOfTokenBoundToName_Errors()
    {
        AssertHasErrorS601(source: """
                                        routine start()
                                            var cells = [Cell(n: 1)]
                                            var m = try cells.view_at(index: 0)
                                            return
                                        """,
            expected: "You keep `Optional[Viewing[Cell]]` in 'm'");
    }
}
