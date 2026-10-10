using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Tests for the `agent` argument boundary (RF-S632). The work of an Agent may run on any thread in parallel with
/// the code that made it, so each argument `agent f(...)` computes now must cross safely: `steal`-moved
/// (exclusive transfer), trivially-copyable value data (passed BY VALUE), or a thread-shareable wrapper
/// (Atomic/Guarded/Witnessed/Consulting/Amending, which carries its own synchronization). Anything else passed by
/// copy (a bare entity, or a record/tuple that transitively owns a single-threaded RC wrapper (Retained/Tracked) or
/// single-threaded token (Viewing/Modifying)) would silently alias unsynchronized state and is rejected.
/// </summary>
public class ThreadArgShareabilityTests
{
    private const string Prelude = """
                                   import IO/Console

                                   record Point
                                       posted x: S64
                                       posted y: S64

                                   entity Node
                                       value: S64

                                   """;

    [Fact]
    public void Analyze_ScalarThreadArg_Ok()
    {
        string source = Prelude + """
                                  routine work(n: S64) -> S64
                                      return n + 1

                                  routine start()
                                      var t = agent work(n: 5)
                                      drop t.retrieve()
                                      return
                                  """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_ValueRecordThreadArg_Ok()
    {
        // A plain value record (no interior reference) is copied by value — safe to cross.
        string source = Prelude + """
                                  routine work(p: Point) -> S64
                                      return p.x + p.y

                                  routine start()
                                      var origin = Point(x: 1, y: 2)
                                      var t = agent work(p: origin)
                                      drop t.retrieve()
                                      return
                                  """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_AtomicThreadArg_Ok()
    {
        // Atomic carries its own synchronization — shared by reference across the boundary.
        string source = Prelude + """
                                  routine work(cell: Atomic[S64]) -> S64
                                      drop cell.fetch_add(delta: 1_s64)
                                      return 0_s64

                                  routine start()
                                      var counter = Atomic[S64](initial: 0_s64)
                                      var t = agent work(cell: counter)
                                      drop t.retrieve()
                                      return
                                  """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_SharedThreadArg_Ok()
    {
        // Guarded is an atomic Arc — explicitly shareable across threads.
        string source = Prelude + """
                                  routine work(s: Guarded[Node, ReadOnly]) -> S64
                                      return 0_s64

                                  routine start()
                                      var s = Guarded[Node, ReadOnly](from: Node(value: 1))
                                      var t = agent work(s: s)
                                      drop t.retrieve()
                                      return
                                  """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_BareEntityThreadArg_Errors()
    {
        // A bare entity is a heap handle — passing it would alias the same object across threads.
        string source = Prelude + """
                                  routine work(node: Node) -> S64
                                      return node.value

                                  routine start()
                                      var n = Node(value: 1)
                                      var t = agent work(node: n)
                                      drop t.retrieve()
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring:
            "to an agent, but");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ThreadArgNotShareable);
    }

    [Fact]
    public void Analyze_RecordOwningRetainedThreadArg_Errors()
    {
        // A record that transitively owns a single-threaded RC wrapper (Retained) would alias its
        // interior across threads — must use Guarded instead.
        string source = Prelude + """
                                  record Holder
                                      posted node: Retained[Node]

                                  routine work(h: Holder) -> S64
                                      return 0_s64

                                  routine start()
                                      var h = Holder(node: Retained(from: Node(value: 1)))
                                      var t = agent work(h: h)
                                      drop t.retrieve()
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "which holds a `Retained` at `node`");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ThreadArgNotShareable);
    }

    [Fact]
    public void Analyze_RecordOwningGuardedThreadArg_Ok()
    {
        // A record owning a `Guarded` field crosses fine: Guarded carries its own atomic
        // synchronization, so the record's interior is sound across parallel workers — exactly as a
        // BARE `Guarded` param crosses (Analyze_SharedThreadArg_Ok). The thread-unsafe offender set is
        // DERIVED from the thread-shareable classification, so a thread-safe wrapper is never barred
        // just because a record wraps it. (The old hand-maintained set wrongly rejected this.)
        string source = Prelude + """
                                  record Holder
                                      posted node: Guarded[Node, ReadOnly]

                                  routine work(h: Holder) -> S64
                                      return 0_s64

                                  routine start()
                                      var h = Holder(node: Guarded[Node, ReadOnly](from: Node(value: 1)))
                                      var t = agent work(h: h)
                                      drop t.retrieve()
                                      return
                                  """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_StealBareEntityThreadArg_Ok()
    {
        // A `steal`-moved bare entity is an EXCLUSIVE transfer — the caller loses access, so exactly
        // one live handle survives. Safe to cross even though the param type is a bare entity. (This
        // is the over-strictness the steal-credit removed: the type-only check rejected it before.)
        string source = Prelude + """
                                  routine work(node: Node) -> S64
                                      return node.value

                                  routine start()
                                      var n = Node(value: 1)
                                      var t = agent work(node: steal n)
                                      drop t.retrieve()
                                      return
                                  """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_StealRetainedIntoAgent_HandsOverTheEntity()
    {
        // `steal r` on a Retained takes the entity out of the handle (it must be the last one), so the work gets the
        // bare entity, moved, and never the single-thread handle.
        string source = Prelude + """
                                  routine work(n: Node) -> S64
                                      return n.value

                                  routine start()
                                      var r = Retained(from: Node(value: 1))
                                      var t = agent work(n: steal r)
                                      drop t.retrieve()
                                      return
                                  """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_StealTracked_Rejected()
    {
        string source = Prelude + """
                                  routine start()
                                      var r = Retained(from: Node(value: 1))
                                      var w = r.observe()
                                      var n = steal w
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "a weak handle doesn't keep the entity");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.StealSharedOwnership);
    }

    [Fact]
    public void Analyze_StealRecordOwningRetainedArg_Errors()
    {
        // `steal` on a plain record IS allowed (no-op), but stealing a record that transitively owns
        // a Retained does NOT make it safe to cross: moving the record doesn't make its interior
        // non-atomic refcount exclusive. The boundary credits `steal` ONLY for a bare entity, so this
        // is still rejected (RF-S632) — must use `Guarded`/`Witnessed`.
        string source = Prelude + """
                                  record Holder
                                      posted node: Retained[Node]

                                  routine work(h: Holder) -> S64
                                      return 0_s64

                                  routine start()
                                      var h = Holder(node: Retained(from: Node(value: 1)))
                                      var t = agent work(h: steal h)
                                      drop t.retrieve()
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring:
            "Start it with 'start_here()' first");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ThreadArgNotShareable);
    }

    /// <summary>An Agent holding a token runs on this thread with start_here(), and is retrieved here.</summary>
    [Fact]
    public void Analyze_TokenArgStartedHere_Ok()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           routine bump(n: Modifying[Node]) -> S64
                                               n.value += 1
                                               return n.value

                                           routine start()
                                               var node = Node(value: 1)
                                               var job = agent bump(n: node.modify())
                                               job.start_here()
                                               var got = job.retrieve()
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_TokenArgStartedOnAnotherThread_Errors()
    {
        AssertHasErrorSa(source: Prelude + """
                                           routine bump(n: Modifying[Node]) -> S64
                                               return n.value

                                           routine start()
                                               var node = Node(value: 1)
                                               var job = agent bump(n: node.modify())
                                               job.start_now()
                                               var got = job.retrieve()
                                               return
                                           """,
            expectedErrorSubstring: "not 'start_now()'");
    }

    [Fact]
    public void Analyze_TokenArgAgentLeavingTheRoutine_Errors()
    {
        AssertHasErrorSa(source: Prelude + """
                                           routine bump(n: Modifying[Node]) -> S64
                                               return n.value

                                           routine make(node: Node) -> Agent[S64]
                                               var job = agent bump(n: node.modify())
                                               return job
                                           """,
            expectedErrorSubstring: "stays in the routine that made it");
    }
}
