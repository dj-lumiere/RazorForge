using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Contains tests for routine declaration.
/// </summary>
public class RoutineDeclarationTests
{
    #region Duplicate Routine Definition

    /// <summary>
    /// Verifies semantic analysis behavior for duplicate routine and reports the expected error.
    /// </summary>
    [Fact]
    public void Analyze_DuplicateRoutine_ReportsError()
    {
        string source = """
                        routine foo() -> S32
                            return 1
                        routine foo() -> S32
                            return 2
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.DuplicateRoutineDefinition);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for unique routines no duplicate error.
    /// </summary>
    [Fact]
    public void Analyze_UniqueRoutines_NoDuplicateError()
    {
        string source = """
                        routine foo() -> S32
                            return 1
                        routine bar() -> S32
                            return 2
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.DuplicateRoutineDefinition);
    }

    #endregion

    #region Parameter validation

    /// <summary>
    /// Verifies that a routine with distinct parameter names produces no error.
    /// </summary>
    [Fact]
    public void Analyze_DistinctParameterNames_NoError()
    {
        string source = """
                        routine add(a: S32, b: S32) -> S32
                            return a
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.DuplicateRoutineDefinition);
    }

    #endregion

    #region Return type validation

    /// <summary>
    /// Verifies that returning the wrong type from a routine reports a ReturnTypeMismatch error.
    /// </summary>
    [Fact]
    public void Analyze_ReturnTypeMismatch_ReportsError()
    {
        string source = """
                        routine get_text() -> Text
                            return 42
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReturnTypeMismatch);
    }

    /// <summary>
    /// Verifies that a void routine with a bare return produces no error.
    /// </summary>
    [Fact]
    public void Analyze_VoidRoutineWithBareReturn_NoError()
    {
        string source = """
                        routine do_work()
                            nothing
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReturnTypeMismatch);
    }

    /// <summary>
    /// A routine written without <c>-&gt; T</c> gives nothing back, so a <c>return</c> with a value in it is
    /// RF-S301 naming the routine and the value's type. The value never sets the result type.
    /// </summary>
    [Fact]
    public void Analyze_ValueReturnWithoutArrow_ReportsError()
    {
        string source = """
                        record Point
                            x: S64

                        routine is_big(p: Point)
                            return p.x > 3
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReturnTypeMismatch &&
                         e.Message.Contains(value: "'is_big' gives nothing back") &&
                         e.Message.Contains(value: "'-> Bool'"));
    }

    /// <summary>
    /// A routine declared <c>-&gt; None</c> that returns a value is RF-S301 too.
    /// </summary>
    [Fact]
    public void Analyze_ValueReturnFromExplicitNone_ReportsError()
    {
        string source = """
                        routine verify(x: S64) -> None
                            return x > 4
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReturnTypeMismatch &&
                         e.Message.Contains(value: "'verify' gives nothing back"));
    }

    /// <summary>
    /// A bare <c>return</c> in a routine declared <c>-&gt; Bool</c> leaves the caller without a value: RF-S301.
    /// </summary>
    [Fact]
    public void Analyze_BareReturnInValueRoutine_ReportsError()
    {
        string source = """
                        routine is_big(x: S64) -> Bool
                            if x > 3
                                return
                            return false
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReturnTypeMismatch &&
                         e.Message.Contains(value: "'is_big' gives back a 'Bool'") &&
                         e.Message.Contains(value: "hands back nothing"));
    }

    /// <summary>
    /// A routine without <c>-&gt; T</c> that mixes a bare <c>return</c> and a value <c>return</c> is reported at the
    /// value one.
    /// </summary>
    [Fact]
    public void Analyze_MixedBareAndValueReturns_ReportsError()
    {
        string source = """
                        routine mixed(x: S64)
                            if x > 3
                                return x > 4
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Single(collection: result.Errors,
            predicate: e => e.Code == SemanticDiagnosticCode.ReturnTypeMismatch &&
                            e.Message.Contains(value: "'mixed' gives nothing back"));
    }

    /// <summary>
    /// <c>return None</c> written out in a routine without <c>-&gt; T</c> hands back nothing and is fine.
    /// </summary>
    [Fact]
    public void Analyze_ReturnNoneWithoutArrow_NoError()
    {
        string source = """
                        routine do_work(x: S64)
                            if x > 3
                                return None
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReturnTypeMismatch);
    }

    /// <summary>
    /// A lambda's body is one expression and its value is the result, so a lambda holds no <c>return</c>
    /// (a parse error), and a lambda whose value does not fit the <c>Callable</c> it goes to is RF-S502.
    /// </summary>
    [Fact]
    public void Analyze_LambdaValueIntoNoneCallable_ReportsError()
    {
        string source = """
                        routine run(f: Callable[(S64,), None])
                            f(3)
                            return

                        routine start()
                            run(f: x => x > 3)
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ArgumentTypeMismatch);
    }

    /// <summary>
    /// <c>return</c> inside a lambda body is a parse error: the body is one expression.
    /// </summary>
    [Fact]
    public void Parse_ReturnInLambda_ReportsError()
    {
        AssertParseError(source: """
                                 routine start()
                                     var f = (x: S64) => return x
                                     return
                                 """);
    }

    #endregion

    #region Duplicate member routines

    /// <summary>
    /// Verifies that two implementations of the same member routine report a duplicate error.
    /// </summary>
    [Fact]
    public void Analyze_DuplicateMemberRoutine_ReportsError()
    {
        string source = """
                        record Point
                            x: S32
                            y: S32

                        @readonly
                        routine Point.area() -> S32
                            return me.x * me.y

                        @readonly
                        routine Point.area() -> S32
                            return 0
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.DuplicateRoutineDefinition);
    }

    #endregion
}
