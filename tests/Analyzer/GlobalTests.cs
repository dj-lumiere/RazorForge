using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// RazorForge <c>global</c>: reached from every thread, so the binding is never assigned again after its
/// initialization. An Atomic or a Guarded changes what it holds through its own routines, any other value is only read.
/// </summary>
public class GlobalTests
{
    [Fact]
    public void Analyze_AtomicAndGuardedGlobals_ChangeThroughTheirRoutines()
    {
        AssertAnalyzesSa(source: """
                                 entity Table
                                     n: S64

                                 global hits: Atomic[U64] = Atomic[U64](initial: 0)
                                 global table: Guarded[Table, Exclusive] = Guarded[Table, Exclusive](from: Table(n: 0))
                                 global name: Text = "x"

                                 routine bump()
                                     hits.fetch_add(delta: 1)
                                     using table.modify() as t
                                         t.n += 1
                                     var copy = name
                                     return

                                 routine start()
                                     bump()
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_ValueGlobalWrite_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: """
                                                         global count: S64 = 0

                                                         routine start()
                                                             count += 1
                                                             return
                                                         """,
            expectedErrorSubstring: "'count' is a global, which every thread reaches, so it is only read");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.AssignmentToImmutable);
    }

    [Fact]
    public void Analyze_RecordGlobalMemberWrite_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: """
                                                         record Settings
                                                             volume: S64

                                                         global settings: Settings = Settings(volume: 3)

                                                         routine start()
                                                             settings.volume = 5
                                                             return
                                                         """,
            expectedErrorSubstring: "its member variable 'volume' can't be assigned");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.MemberVariableAssignmentOnImmutable);
    }

    [Fact]
    public void Analyze_EntityGlobalChange_IsRejected()
    {
        AssertHasErrorSa(source: """
                                 global names: List[Text] = List[Text]()

                                 routine start()
                                     names.add_last(value: "a")
                                     return
                                 """,
            expectedErrorSubstring: "it can't be changed by 'add_last'");
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
