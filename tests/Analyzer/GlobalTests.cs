using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// RazorForge <c>global</c>: a place the program changes (<c>count += 1</c>). The one refusal is a plain global that is
/// changed somewhere and is used by a routine a Work reaches on another thread. An Unbroken or a Guarded changes what it
/// holds through its own routines, and a global nothing changes is read from anywhere.
/// </summary>
public class GlobalTests
{
    [Fact]
    public void Analyze_UnbrokenAndGuardedGlobals_ChangeThroughTheirRoutines()
    {
        AssertAnalyzesSa(source: """
                                 entity Table
                                     n: S64

                                 global hits: Unbroken[U64] = Unbroken[U64](initial: 0)
                                 global table: Guarded[Table, Exclusive] = Guarded[Table, Exclusive](from: Table(n: 0))
                                 global name: Text = "x"

                                 routine bump()
                                     hits.fetch_add(delta: 1)
                                     modifying table as t
                                         t.n += 1
                                     var copy = name
                                     return

                                 routine start()
                                     bump()
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_ValueGlobalWrite_IsAllowed()
    {
        AssertAnalyzesSa(source: """
                                 global count: S64 = 0

                                 routine start()
                                     count += 1
                                     count = 5
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_RecordGlobalMemberWrite_IsAllowed()
    {
        AssertAnalyzesSa(source: """
                                 record Settings
                                     volume: S64

                                 global settings: Settings = Settings(volume: 3)

                                 routine start()
                                     settings.volume = 5
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_ChangedGlobalUsedByWork_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: """
                                                         global count: S64 = 0

                                                         routine bump() -> S64
                                                             count += 1
                                                             return count

                                                         routine start()
                                                             var job = bump.to[Work]()
                                                             job.start_now()
                                                             return
                                                         """,
            expectedErrorSubstring: "uses the global 'count'");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.GlobalChangedAcrossThreads);
    }

    [Fact]
    public void Analyze_ChangedGlobalReachedThroughACall_IsRejected()
    {
        AssertHasErrorSa(source: """
                                 global count: S64 = 0

                                 routine read() -> S64
                                     return count

                                 routine outer() -> S64
                                     return read()

                                 routine start()
                                     count = 2
                                     var job = outer.to[Work]()
                                     job.start_now()
                                     return
                                 """,
            expectedErrorSubstring: "'read', which 'outer' calls, uses the global 'count'");
    }

    [Fact]
    public void Analyze_GlobalNothingChanges_IsReadByWork()
    {
        AssertAnalyzesSa(source: """
                                 global limit: S64 = 5

                                 routine read() -> S64
                                     return limit

                                 routine start()
                                     var job = read.to[Work]()
                                     job.start_now()
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_ChangedGlobalUsedByWorkStartedHere_IsAllowed()
    {
        AssertAnalyzesSa(source: """
                                 global count: S64 = 0

                                 routine bump() -> S64
                                     count += 1
                                     return count

                                 routine start()
                                     var job = bump.to[Work]()
                                     job.start_here()
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_GlobalsReadingEachOther_IsACycle()
    {
        AnalysisResult result = AssertHasErrorSa(source: """
                                                         global a: S64 = b + 1
                                                         global b: S64 = a + 1

                                                         routine start()
                                                             return
                                                         """,
            expectedErrorSubstring: "read each other in their initializers");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.ModuleGlobalCircularInit);
    }
}
