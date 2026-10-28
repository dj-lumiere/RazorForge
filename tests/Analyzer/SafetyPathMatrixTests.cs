using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// The cells of the safety path matrix (intent C45) that used to build and are build errors now (C47): each case is the
/// matrix repro reduced to its core, with the diagnostic it gets. The last cases check that the legal neighbours of
/// those paths still build.
/// </summary>
public class SafetyPathMatrixTests
{
    private const string Prelude = """
                                   import IO/Console
                                   import IterTools

                                   entity Cell
                                       n: S64

                                   """;

    private static void AssertHasErrorCode(string source, SemanticDiagnosticCode code, string expected)
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + source, expectedErrorSubstring: expected);
        Assert.Contains(collection: result.Errors, filter: e => e.Code == code);
    }

    // ---- tokens never leave the routine's variables (RF-S601) ----------------------------------------------

    [Fact]
    public void Analyze_TokenInWrittenVariant_Errors()
    {
        AssertHasErrorCode(source: """
                           routine start()
                               var v: Viewing[Cell] | S64 = 0
                               if true
                                   var l = Cell(n: 42)
                                   v = l.view()
                               return
                           """,
            code: SemanticDiagnosticCode.TokenMemberVariableNotAllowed, expected: "is a token");
    }

    [Fact]
    public void Analyze_GlobalOfTokenType_Errors()
    {
        AssertHasErrorCode(source: """
                           global KEPT: Viewing[Cell] = Cell(n: 1).view()

                           routine start()
                               return
                           """,
            code: SemanticDiagnosticCode.TokenMemberVariableNotAllowed, expected: "never kept in a global");
    }

    [Fact]
    public void Analyze_GenericParameterBoundToToken_Errors()
    {
        AssertHasErrorCode(source: """
                           routine same[T](x: T) -> T
                               return steal x

                           routine start()
                               var l = Cell(n: 42)
                               var w = same(x: l.view())
                               return
                           """,
            code: SemanticDiagnosticCode.TokenMemberVariableNotAllowed, expected: "makes 'T' of 'same'");
    }

    // ---- an entity lives in one place (RF-S413) -------------------------------------------------------------

    [Fact]
    public void Analyze_GenericValueHandedOverTwice_Errors()
    {
        AssertHasErrorCode(source: """
                           routine twice[T](x: T) -> List[T]
                               var xs = List[T]()
                               xs.add_last(value: x)
                               xs.add_last(value: x)
                               return xs

                           routine start()
                               return
                           """,
            code: SemanticDiagnosticCode.BareEntityAssignment, expected: "'T' may be an entity");
    }

    // ---- single-thread handles stay on their thread (RF-S632) ------------------------------------------------

    [Fact]
    public void Analyze_EntityHoldingRetainedStolenIntoWork_Errors()
    {
        AssertHasErrorCode(source: """
                           entity Q
                               r: Retained[Cell]

                           routine work(q: Q) -> S64
                               return q.r.n

                           routine start()
                               var r = Retained(from: Cell(n: 1))
                               var q = Q(r: r)
                               var j = work.to[Work](q: steal q)
                               j.start_now()
                               show(f"{j.retrieve()}")
                               return
                           """,
            code: SemanticDiagnosticCode.ThreadArgNotShareable, expected: "holds a `Retained` at `r`");
    }

    [Fact]
    public void Analyze_ListOfRetainedStolenIntoWork_Errors()
    {
        AssertHasErrorCode(source: """
                           routine work(xs: List[Retained[Cell]]) -> S64
                               return xs[0].n

                           routine start()
                               var r = Retained(from: Cell(n: 1))
                               var xs = [r]
                               var j = work.to[Work](xs: steal xs)
                               j.start_now()
                               show(f"{j.retrieve()}")
                               return
                           """,
            code: SemanticDiagnosticCode.ThreadArgNotShareable, expected: "in its type argument `Retained[Cell]`");
    }

    [Fact]
    public void Analyze_LambdaCapturingRetainedHandedToWork_Errors()
    {
        AssertHasErrorCode(source: """
                           routine work(f: Callable[(), S64]) -> S64
                               return f()

                           routine start()
                               var r = Retained(from: Cell(n: 1))
                               var f = () given (r) => r.n
                               var j = work.to[Work](f: f)
                               show(f"{j.retrieve()}")
                               return
                           """,
            code: SemanticDiagnosticCode.ThreadArgNotShareable, expected: "a lambda that captured `r`");
    }

    [Fact]
    public void Analyze_LambdaCapturingRetainedHandedThroughParametersToWork_Errors()
    {
        AssertHasErrorCode(source: """
                           routine work(f: Callable[(), S64]) -> S64
                               return f()

                           routine spawn(f: Callable[(), S64]) -> Work[S64]
                               return work.to[Work](f: f)

                           routine relay(g: Callable[(), S64]) -> Work[S64]
                               return spawn(f: g)

                           routine start()
                               var r = Retained(from: Cell(n: 1))
                               var f = () given (r) => r.n
                               var j = relay(g: f)
                               show(f"{j.retrieve()}")
                               return
                           """,
            code: SemanticDiagnosticCode.ThreadArgNotShareable, expected: "'relay' hands its 'g' to a Work");
    }

    [Fact]
    public void Analyze_LambdaCapturingRetainedKeptInMemberVariableHandedToWork_Errors()
    {
        AssertHasErrorCode(source: """
                           record Holder
                               f: Callable[(), S64]

                           routine work(f: Callable[(), S64]) -> S64
                               return f()

                           routine start()
                               var r = Retained(from: Cell(n: 1))
                               var h = Holder(f: () given (r) => r.n)
                               var j = work.to[Work](f: h.f)
                               j.start_now()
                               show(f"{j.retrieve()}")
                               return
                           """,
            code: SemanticDiagnosticCode.ThreadArgNotShareable, expected: "This lambda captured `r`");
    }

    [Fact]
    public void Analyze_LambdaCapturingRetainedKeptInListHandedToWork_Errors()
    {
        AssertHasErrorCode(source: """
                           routine work(f: Callable[(), S64]) -> S64
                               return f()

                           routine start()
                               var r = Retained(from: Cell(n: 1))
                               var fs: List[Callable[(), S64]] = []
                               fs.add_last(value: () given (r) => r.n)
                               var j = work.to[Work](f: fs[0])
                               j.start_now()
                               show(f"{j.retrieve()}")
                               return
                           """,
            code: SemanticDiagnosticCode.ThreadArgNotShareable, expected: "This lambda captured `r`");
    }

    [Fact]
    public void Analyze_LambdaCapturingValueKeptInMemberVariableHandedToWork_Analyzes()
    {
        AssertAnalyzes(source: """
                       import IO/Console

                       record Holder
                           f: Callable[(), S64]

                       routine work(f: Callable[(), S64]) -> S64
                           return f()

                       routine start()
                           var n: S64 = 4
                           var h = Holder(f: () given (n) => n + 1)
                           var j = work.to[Work](f: h.f)
                           j.start_now()
                           show(f"{j.retrieve()}")
                           return
                       """);
    }

    [Fact]
    public void Analyze_ChannelOfRetainedHandedToWork_Errors()
    {
        AssertHasErrorCode(source: """
                           routine work(rx: Receiver[Retained[Cell]]) -> S64
                               var sum = 0
                               for c in rx
                                   sum += c.n
                               return sum

                           routine start()
                               var (tx, rx) = make_channel[Retained[Cell]](capacity: 4)
                               var j = work.to[Work](rx: steal rx)
                               j.start_now()
                               tx.close()
                               show(f"{j.retrieve()}")
                               return
                           """,
            code: SemanticDiagnosticCode.ThreadArgNotShareable, expected: "Receiver[Retained[Cell]]");
    }

    [Fact]
    public void Analyze_GlobalRetained_Errors()
    {
        AssertHasErrorCode(source: """
                           global G: Retained[Cell] = Retained(from: Cell(n: 7))

                           routine start()
                               return
                           """,
            code: SemanticDiagnosticCode.ThreadArgNotShareable, expected: "Every running job reaches a global");
    }

    // ---- globals are only read (RF-S256, RF-S421) ----------------------------------------------------------------

    [Fact]
    public void Analyze_ModifyingBlockOnPlainEntityGlobal_Errors()
    {
        AssertHasErrorCode(source: """
                           global G: Cell = Cell(n: 1)

                           routine start()
                               modifying G as m
                                   m.n = 5
                               return
                           """,
            code: SemanticDiagnosticCode.ModifyingCallOnImmutable, expected: "a global is only read");
    }

    [Fact]
    public void Analyze_StealGlobalInAnotherRoutine_Errors()
    {
        AssertHasErrorCode(source: """
                           global G: Cell = Cell(n: 42)

                           routine consume(c: Cell)
                               return

                           routine take()
                               consume(c: steal G)
                               return

                           routine start()
                               take()
                               return
                           """,
            code: SemanticDiagnosticCode.InvalidDropTarget, expected: "You steal the global 'G'");
    }

    // ---- the walked collection keeps its shape (RF-S625) -------------------------------------------------------

    [Fact]
    public void Analyze_ReshapeThroughAdapterSource_Errors()
    {
        AssertHasErrorCode(source: """
                           routine start()
                               var xs: List[S64] = [1, 2, 3]
                               for x in xs.where(predicate: y => y > 0)
                                   xs.add_last(value: x)
                               return
                           """,
            code: SemanticDiagnosticCode.ReshapingDuringIteration, expected: "'add_last' adds to or removes from 'xs'");
    }

    // ---- dangerous routines only inside danger (RF-S609) --------------------------------------------------------

    [Fact]
    public void Analyze_DangerousRoutineAsValue_Errors()
    {
        AssertHasErrorCode(source: """
                           dangerous routine raw() -> S64
                               return 7

                           routine start()
                               var f = raw
                               show(f"{f()}")
                               return
                           """,
            code: SemanticDiagnosticCode.DangerousCallOutsideDangerBlock, expected: "You call 'f'");
    }

    [Fact]
    public void Analyze_LambdaMadeInDangerCallingDangerous_Errors()
    {
        AssertHasErrorCode(source: """
                           dangerous routine raw() -> S64
                               return 7

                           routine start()
                               var f = () => 0
                               danger
                                   f = () => raw()
                               show(f"{f()}")
                               return
                           """,
            code: SemanticDiagnosticCode.DangerousCallOutsideDangerBlock, expected: "'raw'");
    }

    [Fact]
    public void Analyze_OperatorOnDangerousRoutine_Errors()
    {
        AssertHasErrorCode(source: """
                           record P
                               x: S64

                           dangerous routine P.add(you: P) -> P
                               return P(x: me.x + you.x)

                           routine start()
                               var p = P(x: 1) + P(x: 2)
                               return
                           """,
            code: SemanticDiagnosticCode.DangerousCallOutsideDangerBlock, expected: "'+' calls 'add'");
    }

    [Fact]
    public void Analyze_FTextOnDangerousRepresent_Errors()
    {
        AssertHasErrorCode(source: """
                           record P
                               x: S64

                           dangerous routine P.represent() -> Text
                               return "p"

                           routine start()
                               var p = P(x: 1)
                               show(f"{p}")
                               return
                           """,
            code: SemanticDiagnosticCode.DangerousCallOutsideDangerBlock, expected: "calls 'represent'");
    }

    // ---- steal / failable / destruct ---------------------------------------------------------------------------

    [Fact]
    public void Analyze_StealInLoopTurnThatContinues_Errors()
    {
        AssertHasErrorCode(source: """
                           routine consume(c: Cell)
                               return

                           routine start()
                               var a = Cell(n: 42)
                               for i in 0 til 3
                                   if i == 1
                                       consume(c: steal a)
                                       continue
                               return
                           """,
            code: SemanticDiagnosticCode.StealOnSomePaths, expected: "goes on to the next turn");
    }

    [Fact]
    public void Analyze_FailableDestruct_Errors()
    {
        AssertHasErrorCode(source: """
                           entity Q
                               n: S64

                           failable routine Q.destruct()
                               throw VerificationFailedError()

                           routine start()
                               var q = Q(n: 1)
                               return
                           """,
            code: SemanticDiagnosticCode.DestructNotFailable, expected: "with `failable`");
    }

    // ---- messages ----------------------------------------------------------------------------------------------

    [Fact]
    public void Analyze_BlockTokenCopiedOut_NamesTheToken()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine start()
                                                                       var g = Guarded[Cell, Exclusive](from: Cell(n: 42))
                                                                       var other = Cell(n: 0)
                                                                       var w = other.modify()
                                                                       modifying g as m
                                                                           w = m
                                                                       return
                                                                   """,
            expectedErrorSubstring: "You copy 'm', a `Modifying[Cell]` token");
        Assert.DoesNotContain(collection: result.Errors, filter: e => e.Message.Contains(value: "none —"));
    }

    // ---- legal neighbours ----------------------------------------------------------------------------------------

    [Fact]
    public void Analyze_LegalNeighbours_Analyze()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           entity Bank
                                               a: Guarded[Cell, Exclusive]
                                               b: Guarded[Cell, Exclusive]

                                           routine keep[T](x: T) -> List[T]
                                               var xs = List[T]()
                                               xs.add_last(value: steal x)
                                               return xs

                                           routine work(q: Cell) -> S64
                                               return q.n

                                           routine start()
                                               var xs: List[S64] = [1, 2, 3]
                                               for x in xs.duplicate()
                                                   xs.add_last(value: x)
                                               var kept = keep(x: Cell(n: 2))
                                               var c = Cell(n: 3)
                                               var j = work.to[Work](q: steal c)
                                               j.start_now()
                                               show(f"{j.retrieve()} {kept.count()}")
                                               var bank = Bank(a: Guarded[Cell, Exclusive](from: Cell(n: 1)),
                                                   b: Guarded[Cell, Exclusive](from: Cell(n: 2)))
                                               modifying bank.a as x, bank.b as y
                                                   x.n = 2
                                                   y.n = 3
                                               return
                                           """);
    }
}
