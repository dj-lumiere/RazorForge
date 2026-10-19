using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Tests for the readers-XOR-writer borrow checker (RF-S630). Within overlapping (nested) `using`
/// scopes on the SAME Guarded handle, a `modify()` (writer) conflicts with any other hold; `view()`
/// readers may coexist with other readers. Non-overlapping (sequential) scopes and distinct handles
/// never conflict. Detection is name-based on the Guarded handle (v1).
/// </summary>
public class RxwBorrowCheckerTests
{
    private const string Prelude = """
                                   import IO/Console
                                   import BuilderQuery

                                   entity Counter
                                       value: S64

                                   """;

    [Fact]
    public void Analyze_NestedClaimClaim_SameHandle_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      using s.modify() as c1
                                          using s.modify() as c2
                                              show("nested")
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "the enclosing 'using ... modify()'");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReadersXorWriter);
    }

    [Fact]
    public void Analyze_NestedClaimInspect_SameHandle_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      using s.modify() as c
                                          using s.view() as v
                                              show("nested")
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "the enclosing 'using ... modify()'");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReadersXorWriter);
    }

    [Fact]
    public void Analyze_NestedInspectClaim_SameHandle_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      using s.view() as v
                                          using s.modify() as c
                                              show("nested")
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "the enclosing 'using ... view()'");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReadersXorWriter);
    }

    [Fact]
    public void Analyze_NestedInspectInspect_SameHandle_Ok()
    {
        // Readers coexist — multiple view holds on the same handle are allowed.
        string source = Prelude + """
                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      using s.view() as v1
                                          using s.view() as v2
                                              show(f"{v1.value} {v2.value}")
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void Analyze_NestedClaim_DifferentHandles_Ok()
    {
        // Distinct handles do not conflict (name-based).
        string source = Prelude + """
                                  routine start()
                                      var s1 = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      var s2 = Guarded[Counter, MultiRead](from: Counter(value: 2))
                                      using s1.modify() as c1
                                          using s2.modify() as c2
                                              show("two handles")
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void Analyze_SequentialClaim_SameHandle_Ok()
    {
        // Non-overlapping scopes — the first modify is released before the second opens.
        string source = Prelude + """
                                  routine Counter.bump(inc: S64)
                                      me.value = me.value + inc
                                      return

                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      using s.modify() as c1
                                          c1.bump(inc: 1)
                                      using s.modify() as c2
                                          c2.bump(inc: 1)
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void Analyze_NestedClaim_AliasedHandle_Errors()
    {
        // `s2 = s` is a CLONE — a second handle to the SAME controller. Opening both for writing in
        // overlapping scopes deadlocks at runtime; the identity-keyed check catches it even though
        // the handle names differ.
        string source = Prelude + """
                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      var s2 = s
                                      using s.modify() as c1
                                          using s2.modify() as c2
                                              show("aliased")
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "same shared data");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReadersXorWriter);
    }

    [Fact]
    public void Analyze_NestedClaimInspect_AliasedHandle_Errors()
    {
        // A reader on a clone still conflicts with a writer on the original (same controller).
        string source = Prelude + """
                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      var s2 = s
                                      using s.modify() as c
                                          using s2.view() as v
                                              show("aliased reader")
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "same shared data");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReadersXorWriter);
    }

    [Fact]
    public void Analyze_NestedInspect_AliasedHandle_Ok()
    {
        // Two readers coexist even on the same controller — readers-XOR-writer permits shared reads.
        string source = Prelude + """
                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      var s2 = s
                                      using s.view() as v1
                                          using s2.view() as v2
                                              show("two readers, one controller")
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void Analyze_LockBackedWriteTokenTwiceInOneCall_Errors()
    {
        // The token `using s.modify()` binds is a plain Modifying: it is the lock's one writer by where it came from.
        string source = Prelude + """
                                  routine two(a: Modifying[Counter], b: Modifying[Counter])
                                      a.value += 1
                                      b.value += 1
                                      return

                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      using s.modify() as c
                                          two(a: c, b: c)
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "the write token of a Guarded's lock");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ExclusiveTokenDuplicate);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.SameContainerTwiceInCall);
    }

    [Fact]
    public void Analyze_ViewOnExclusive_SaysToModify()
    {
        string source = Prelude + """
                                  routine start()
                                      var s = Guarded[Counter, Exclusive](from: Counter(value: 1))
                                      using s.view() as v
                                          show(f"{v.value}")
                                      return
                                  """;

        AssertHasErrorSa(source: source, expectedErrorSubstring: "read it inside 'using s.modify() as m'");
    }
}
