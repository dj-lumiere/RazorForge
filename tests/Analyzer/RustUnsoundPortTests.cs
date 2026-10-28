using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Rust soundness bugs (`I-unsound` issues) ported to RazorForge (intent C46) that built before and are build errors
/// now. Each case is the port reduced to its core, with the Rust issue it follows. The last cases check that the legal
/// neighbours of those programs still build.
/// </summary>
public class RustUnsoundPortTests
{
    private const string Prelude = """
                                   import IO/Console

                                   entity Cell
                                       n: S64

                                   """;

    private static void AssertHasErrorCode(string source, SemanticDiagnosticCode code, string expected)
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + source, expectedErrorSubstring: expected);
        Assert.Contains(collection: result.Errors, filter: e => e.Code == code);
    }

    // ---- a name a pattern binds reads the matched place in place (Rust #144312, #51117, #31287) ---------------

    [Fact]
    public void Analyze_RecordPatternNameAfterMemberReplaced_Errors()
    {
        AssertHasErrorCode(source: """
                           entity Chest
                               item: Cell

                           routine start()
                               var ch = Chest(item: Cell(n: 42))
                               if ch is Chest(item: as c)
                                   ch.item = Cell(n: 7)
                                   show(f"{c.n}")
                               return
                           """,
            code: SemanticDiagnosticCode.TokenUsedAfterSourceChanged, expected: "replaced by the assignment to 'ch.item'");
    }

    [Fact]
    public void Analyze_WhenArmNameAfterMatchedMemberCleared_Errors()
    {
        AssertHasErrorCode(source: """
                           entity Slot
                               item: Cell?

                           routine start()
                               var s0 = Slot(item: Cell(n: 42))
                               when s0.item
                                   is None => show("none")
                                   else as c =>
                                       s0.item = none
                                       show(f"{c.n}")
                               return
                           """,
            code: SemanticDiagnosticCode.TokenUsedAfterSourceChanged, expected: "replaced by the assignment to 's0.item'");
    }

    [Fact]
    public void Analyze_WhenArmNameAfterMatchedMemberStolen_Errors()
    {
        AssertHasErrorCode(source: """
                           entity Chest
                               item: Cell?

                           routine start()
                               var ch = Chest(item: Cell(n: 42))
                               when ch.item
                                   is None => show("none")
                                   else as c =>
                                       steal ch.item as old
                                       show(f"{c.n}")
                               return
                           """,
            code: SemanticDiagnosticCode.TokenUsedAfterSourceChanged, expected: "moved out by 'steal ch.item'");
    }

    [Fact]
    public void Analyze_EachElementAfterElementReplaced_Errors()
    {
        AssertHasErrorCode(source: """
                           routine start()
                               var xs = List[Cell]()
                               xs.add_last(value: Cell(n: 42))
                               for c in xs
                                   xs[0] = Cell(n: 7)
                                   show(f"{c.n}")
                               return
                           """,
            code: SemanticDiagnosticCode.TokenUsedAfterSourceChanged, expected: "replaced by the assignment to 'xs[...]'");
    }

    // ---- two names that may be one entity (Rust #48803, #164510, #40288) -------------------------------------

    [Fact]
    public void Analyze_TokenAfterReplaceThroughRetainedCopy_Errors()
    {
        AssertHasErrorCode(source: """
                           entity Holder
                               inner: Cell

                           routine start()
                               var r = Retained(from: Holder(inner: Cell(n: 42)))
                               var r2 = r
                               var v = r.inner.view()
                               r2.inner = Cell(n: 7)
                               show(f"{v.n}")
                               return
                           """,
            code: SemanticDiagnosticCode.TokenUsedAfterSourceChanged, expected: "may be the same Holder");
    }

    [Fact]
    public void Analyze_TokenAfterReplaceThroughOtherTokenParameter_Errors()
    {
        AssertHasErrorCode(source: """
                           entity Holder
                               inner: Cell

                           routine mix(a: Modifying[Holder], b: Modifying[Holder]) -> S64
                               var t = a.inner.view()
                               b.inner = Cell(n: 7)
                               return t.n

                           routine start()
                               var h = Holder(inner: Cell(n: 42))
                               var other = Holder(inner: Cell(n: 1))
                               show(f"{mix(a: h, b: other)}")
                               return
                           """,
            code: SemanticDiagnosticCode.TokenUsedAfterSourceChanged, expected: "may be the same Holder as 'a'");
    }

    [Fact]
    public void Analyze_CopiedWriteTokenHandedTwice_Errors()
    {
        AssertHasErrorCode(source: """
                           routine bump2(a: Modifying[Cell], b: Modifying[Cell])
                               a.n = a.n + 1
                               b.n = b.n + 10
                               return

                           routine start()
                               var c = Cell(n: 0)
                               var m = c.modify()
                               var m2 = m
                               bump2(a: m, b: m2)
                               return
                           """,
            code: SemanticDiagnosticCode.SameContainerTwiceInCall, expected: "tokens taken from the same 'c'");
    }

    // ---- a callback the library calls changes the container it works on (Rust #82282, #80335) ----------------

    [Fact]
    public void Analyze_SortCallbackGrowsAliasOfReceiver_Errors()
    {
        AssertHasErrorCode(source: """
                           routine grow(xs: Retained[List[S64]], a: S64, b: S64) -> Bool
                               xs.add_last(value: 1)
                               return a < b

                           routine start()
                               var r = Retained(from: List[S64]())
                               r.add_last(value: 2)
                               var alias = r
                               r.sort_by(compare: (a, b) given (alias) => grow(xs: alias, a: a, b: b))
                               return
                           """,
            code: SemanticDiagnosticCode.ReshapingDuringIteration, expected: "is working on the elements of 'r'");
    }

    // ---- a member routine written for some instances only (Rust #130140) ------------------------------------

    [Fact]
    public void Analyze_SortedByOnListOfEntities_Errors()
    {
        AssertHasErrorCode(source: """
                           routine start()
                               var xs = List[Cell]()
                               xs.add_last(value: Cell(n: 2))
                               var ys = xs.sorted_by(compare: (a, b) => a.n < b.n)
                               return
                           """,
            code: SemanticDiagnosticCode.ValueTypeConstraintViolation, expected: "needs RecordType T");
    }

    // ---- globals and presets read before they hold anything (Rust #71078) -----------------------------------

    [Fact]
    public void Analyze_GlobalReadingItself_Errors()
    {
        AssertHasErrorCode(source: """
                           global A: S64 = A + 1

                           routine start()
                               show(f"{A}")
                               return
                           """,
            code: SemanticDiagnosticCode.ModuleGlobalCircularInit, expected: "reads itself in its initializer");
    }

    [Fact]
    public void Analyze_PresetComputedFromItself_Errors()
    {
        AssertHasErrorCode(source: """
                           preset P: S64 = Q + 1
                           preset Q: S64 = P + 1

                           routine start()
                               show(f"{P}")
                               return
                           """,
            code: SemanticDiagnosticCode.ModuleGlobalCircularInit, expected: "computed from itself");
    }

    [Fact]
    public void Analyze_GlobalDestructsReadingEachOther_Errors()
    {
        AssertHasErrorCode(source: """
                           entity A
                               n: S64

                           routine A.destruct()
                               show(f"{GB.n}")
                               return

                           entity B
                               n: S64

                           routine B.destruct()
                               show(f"{GA.n}")
                               return

                           global GA: A = A(n: 1)
                           global GB: B = B(n: 2)

                           routine start()
                               return
                           """,
            code: SemanticDiagnosticCode.ModuleGlobalCircularInit, expected: "initializers and destructs");
    }

    // ---- legal neighbours -----------------------------------------------------------------------------------

    [Fact]
    public void Analyze_TwoRetainedMadeApart_Builds()
    {
        AssertAnalyzesSa(source: Prelude + """
                                 entity Holder
                                     inner: Cell

                                 routine start()
                                     var r = Retained(from: Holder(inner: Cell(n: 42)))
                                     var r2 = Retained(from: Holder(inner: Cell(n: 1)))
                                     var v = r.inner.view()
                                     r2.inner = Cell(n: 7)
                                     show(f"{v.n}")
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_PatternNameUsedBeforeTheChange_Builds()
    {
        AssertAnalyzesSa(source: Prelude + """
                                 entity Slot
                                     item: Cell?

                                 routine start()
                                     var s0 = Slot(item: Cell(n: 42))
                                     when s0.item
                                         is None => show("none")
                                         else as c =>
                                             show(f"{c.n}")
                                             s0.item = none
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_SortedOnListOfValues_Builds()
    {
        AssertAnalyzesSa(source: Prelude + """
                                 routine start()
                                     var xs = [3, 1, 2]
                                     var ys = xs.sorted_by(compare: (a, b) => a < b)
                                     show(f"{ys}")
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_GlobalDestructReadingAnotherGlobal_Builds()
    {
        AssertAnalyzesSa(source: Prelude + """
                                 entity Reporter
                                     tag: S64

                                 routine Reporter.destruct()
                                     show(f"{LOG.n}")
                                     return

                                 global REP: Reporter = Reporter(tag: 7)
                                 global LOG: Cell = Cell(n: 3)

                                 routine start()
                                     return
                                 """);
    }
}
