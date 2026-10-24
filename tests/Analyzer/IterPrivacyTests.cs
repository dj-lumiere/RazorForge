#pragma warning disable CS1591
using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// `access` and `control` are builder-only: argument coercion inserts them, and user code calling them directly is a
/// build error. `emitter()` is a routine like any other: user code may call it (the emitter it gives is held like a
/// token when it walks a local container).
/// </summary>
public class IterPrivacyTests
{
    [Fact]
    public void UserCode_CallsEmitter_OnList_Allowed()
    {
        string source = """
                        routine start()
                            var xs = [1, 2, 3]
                            var it = xs.emitter()
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.DirectWiredRoutineCall);
    }

    [Fact]
    public void EachLoop_OverList_Resolves()
    {
        string source = """
                        routine start()
                            var xs = [1, 2, 3]
                            each x in xs
                                return
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void UserCode_CallsDollarAccess_OnOwnedEntity_Rejected()
    {
        string source = """
                        entity Counter
                            value: S64

                        routine start()
                            var c = Counter(value: 1)
                            var r = c.access()
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.DirectWiredRoutineCall);
    }

    [Fact]
    public void UserCode_CallsDollarControl_OnOwnedEntity_Rejected()
    {
        string source = """
                        entity Counter
                            value: S64

                        routine start()
                            var c = Counter(value: 1)
                            var w = c.control()
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.DirectWiredRoutineCall);
    }

    [Fact]
    public void ReferringParam_CallSiteCoercion_Resolves()
    {
        // The builder injects `.access()` automatically at the Viewing[T] param.
        // No user-visible $access call appears in source; the check must not fire on the
        // synthesized coercion.
        string source = """
                        record Box
                            n: S64

                        routine echo(b: Viewing[Box]) -> S64
                            return 0

                        routine start()
                            var b = Box(n: 1)
                            var r = echo(b: b)
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.DirectWiredRoutineCall);
    }

    [Fact]
    public void UserCode_DefinesEmitter_OnRecord_Allowed()
    {
        string source = """
                        record Bag
                            n: S64

                        routine Bag.emitter() -> Bag
                            return me
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.DirectWiredRoutineCall);
    }
}
