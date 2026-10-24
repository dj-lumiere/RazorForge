using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Tests for accepting a protocol type argument inside <c>Viewing[T]</c> /
/// <c>Modifying[T]</c>, and for iterating over such a parameter via a
/// transparent-protocol unwrap. Regression coverage for the
/// <c>List[T].add_range(other: Viewing[Iterable[T]])</c> shape introduced
/// alongside the BytesIO API.
/// </summary>
public class ReferringIterableTests
{
    #region S152 — ReferenceTypeConstraintViolation should NOT fire on protocols

    /// <summary>Verifies Viewing[Iterable[T]] parameter does not trigger S152.</summary>
    [Fact]
    public void Referring_Iterable_AsParameter_NoS152()
    {
        // Iterable[T] is a protocol — not an entity by category, but any concrete
        // value bound to the parameter at the call site WILL be an entity (or
        // fail S152 at that site). The check at the declaration site must
        // accept protocol type arguments so `Viewing[Iterable[T]]` typechecks.
        string source = """
                        module L/Test
                        import IO/Console
                        routine consume_iterable[T](xs: Viewing[Emittable[T]])
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReferenceTypeConstraintViolation);
    }

    /// <summary>Verifies Modifying[Iterable[T]] parameter does not trigger S152.</summary>
    [Fact]
    public void Controlling_Iterable_AsParameter_NoS152()
    {
        // Same rule applies to Modifying[T] (extends Viewing[T]).
        string source = """
                        module L/Test
                        import IO/Console
                        routine consume_mut[T](xs: Modifying[Emittable[T]])
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReferenceTypeConstraintViolation);
    }

    #endregion

    #region S205 — TypeNotIterable should NOT fire on Viewing[Iterable[T]]

    /// <summary>Verifies for-loop over Viewing[Iterable[T]] does not trigger S205.</summary>
    [Fact]
    public void EachLoop_Over_Referring_Iterable_NoS205()
    {
        // The transparent-protocol unwrap in GetIterableElementType lets us
        // iterate a `Viewing[Iterable[T]]` directly — the for-loop sees
        // through the marker wrapper to the underlying Iterable[T].
        string source = """
                        module L/Test
                        import IO/Console
                        routine sum_it(xs: Viewing[Emittable[S64]]) -> S64
                            var total = 0
                            each x in xs
                                total = total + x
                            return total
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TypeNotIterable);
    }

    /// <summary>Verifies for-loop over bare Iterable[T] does not trigger S205.</summary>
    [Fact]
    public void EachLoop_Over_Bare_Iterable_NoS205()
    {
        // Bare Iterable[T] (no Viewing wrap) must also iterate — the element
        // type comes from the protocol's first type-argument.
        string source = """
                        module L/Test
                        import IO/Console
                        routine count_it(xs: Emittable[S64]) -> S64
                            var n = 0
                            each x in xs
                                n = n + 1
                            return n
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TypeNotIterable);
    }

    #endregion
}
