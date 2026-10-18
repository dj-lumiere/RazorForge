using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Tests for the drop keyword behavior:
/// - drop routine_call() - no warning
/// - routine_call() with non-None return - warning
/// - routine_call() with None return - no warning
/// - drop 42 - error (must be call)
/// </summary>
public class DiscardTests
{
    #region RazorForge - Discard With Call

    /// <summary>
    /// Verifies semantic analysis behavior for drop call without unexpected warnings.
    /// </summary>
    [Fact]
    public void Analyze_DiscardCall_NoWarning()
    {
        string source = """
                        routine get_value() -> S32
                            return 42

                        routine test()
                            drop get_value()
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
        Assert.Empty(collection: result.Warnings);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for call without drop non blank return warning.
    /// </summary>
    [Fact]
    public void Analyze_CallWithoutDiscard_NonNoneReturn_Warns()
    {
        string source = """
                        routine get_value() -> S32
                            return 42

                        routine test()
                            get_value()
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
        Assert.Contains(collection: result.Warnings,
            filter: w => w.Code == SemanticWarningCode.UnusedRoutineReturnValue);
    }

    /// <summary>
    /// A routine marked <c>@discardable</c> returns a by-product: its result may be dropped without <c>drop</c>.
    /// </summary>
    [Fact]
    public void Analyze_CallToDiscardable_NoWarning()
    {
        string source = """
                        @discardable
                        routine record_value() -> Bool
                            return true

                        routine test()
                            record_value()
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
        Assert.DoesNotContain(collection: result.Warnings,
            filter: w => w.Code == SemanticWarningCode.UnusedRoutineReturnValue);
    }

    /// <summary>
    /// Verifies semantic analysis behavior for call without drop blank return without unexpected warnings.
    /// </summary>
    [Fact]
    public void Analyze_CallWithoutDiscard_NoneReturn_NoWarning()
    {
        string source = """
                        routine do_something()
                            pass
                            return

                        routine test()
                            do_something()
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
        Assert.Empty(collection: result.Warnings);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for assigned call without unexpected warnings.
    /// </summary>
    [Fact]
    public void Analyze_AssignedCall_NoWarning()
    {
        string source = """
                        routine get_value() -> S32
                            return 42

                        routine test()
                            var x = get_value()
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
        // No warning about unused return value since it's assigned
        Assert.DoesNotContain(collection: result.Warnings,
            filter: w => w.Message.Contains(value: "unused",
                comparisonType: StringComparison.OrdinalIgnoreCase));
    }
    /// <summary>
    /// Verifies semantic analysis behavior for drop member call without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_DiscardMemberCall_NoError()
    {
        string source = """
                        routine get_value() -> S32
                            return 42

                        record Wrapper
                            value: S32

                        routine Wrapper.extract(self: Wrapper) -> S32
                            return self.value

                        routine test()
                            var w = Wrapper(value: 0)
                            drop w.extract(w)
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }
    /// <summary>
    /// Verifies that the parser accepts drop member call succeeds.
    /// </summary>
    [Fact]
    public void Parse_DiscardMemberCall_Succeeds()
    {
        string source = """
                        record Counter
                            value: S32

                        routine Counter.increment(self: Counter) -> Counter
                            return Counter(value: self.value)

                        routine test()
                            var c = Counter(value: 0)
                            drop c.increment()
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts drop failable member call succeeds.
    /// </summary>
    [Fact]
    public void Parse_DiscardFailableMemberCall_Succeeds()
    {
        string source = """
                        record Wrapper
                            value: S32

                        failable routine Wrapper.get_value(self: Wrapper) -> S32
                            return self.value

                        routine test()
                            var w = Wrapper(value: 42)
                            drop w.get_value()
                            return
                        """;

        AssertParses(source: source);
    }

    #endregion

    #region RazorForge - Discard In Failable Context

    /// <summary>
    /// Verifies that drop on a failable call inside a failable routine produces no error.
    /// </summary>
    [Fact]
    public void Analyze_DiscardFailableCallInFailableContext_NoError()
    {
        string source = """
                        failable routine get(flag: Bool) -> S32
                            if flag
                                throw AbsentValueError()
                            return 42

                        failable routine test()
                            drop get(flag: true)
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    /// <summary>
    /// Verifies that calling a failable routine without handling inside a non-failable routine
    /// is currently suppressed (UnhandledCrashableCall is in SuppressedWarnings alongside SW007).
    /// When enforcement is wanted, remove UnhandledCrashableCall from SuppressedWarnings.
    /// </summary>
    [Fact]
    public void Analyze_DiscardFailableCallInNonFailableContext_CurrentlySuppressed()
    {
        string source = """
                        failable routine get(flag: Bool) -> S32
                            if flag
                                throw AbsentValueError()
                            return 42

                        routine test()
                            drop get(flag: true)
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Warnings,
            filter: w => w.Code == SemanticWarningCode.UnhandledCrashableCall);
    }

    /// <summary>
    /// Verifies that drop on a `try`-recovered call in a non-failable context produces no error.
    /// </summary>
    [Fact]
    public void Analyze_DiscardTryVariantInNonFailableContext_NoError()
    {
        string source = """
                        failable routine get(flag: Bool) -> S32
                            if flag
                                throw AbsentValueError()
                            return 42

                        routine test()
                            drop try get(flag: true)
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    #endregion

    #region RazorForge - Discard Chained And Constructed Calls

    /// <summary>
    /// Verifies that the parser accepts drop on a chained memberRoutine call.
    /// </summary>
    [Fact]
    public void Parse_DiscardChainedCall_Succeeds()
    {
        string source = """
                        record Counter
                            value: S32

                        routine Counter.incremented(n: S32) -> Counter
                            return Counter(value: me.value)

                        routine Counter.get_value() -> S32
                            return me.value

                        routine test()
                            var c = Counter(value: 0)
                            drop c.incremented(n: 1).get_value()
                            return
                        """;

        AssertParses(source: source);
    }

    /// <summary>
    /// Verifies that the parser accepts drop on a constructor-style call expression.
    /// </summary>
    [Fact]
    public void Parse_DiscardConstructorCall_Succeeds()
    {
        string source = """
                        record Point
                            x: S32
                            y: S32

                        routine test()
                            drop Point(x: 1, y: 2)
                            return
                        """;

        AssertParses(source: source);
    }

    /// <summary>
    /// Verifies semantic analysis of sequential drop calls produces no unexpected errors.
    /// </summary>
    [Fact]
    public void Analyze_MultipleDiscardCallsSequentially_NoError()
    {
        string source = """
                        routine get_a() -> S32
                            return 1

                        routine get_b() -> S32
                            return 2

                        routine test()
                            drop get_a()
                            drop get_b()
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
        Assert.Empty(collection: result.Warnings);
    }

    /// <summary>
    /// Verifies that drop on a void member call produces no warnings.
    /// </summary>
    [Fact]
    public void Analyze_DiscardVoidMemberCall_NoWarning()
    {
        string source = """
                        record Logger
                            count: S32

                        routine Logger.log(message: Text)
                            pass
                            return

                        routine test()
                            var lg = Logger(count: 0)
                            drop lg.log(message: "hello")
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    #endregion

    #region RazorForge - Discard Non-Call Targets (semantic error)

    // Whether the discarded expression is a routine call is a SEMANTIC check (RF-S421
    // InvalidDropTarget), not a grammatical one — the parser records whatever expression
    // follows `drop`, and the semantic verifier rejects non-call targets.

    /// <summary>`drop x` on a local ends the binding: a later use is an error.</summary>
    [Fact]
    public void Analyze_DropVariable_EndsIt()
    {
        string source = """
                        routine test()
                            var x = 42
                            drop x
                            var y = x
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.InvalidDropTarget);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.UseAfterSteal);
    }

    /// <summary>Verifies that discarding an integer literal is a semantic error.</summary>
    [Fact]
    public void Analyze_DiscardLiteral_ReportsError()
    {
        string source = """
                        routine test()
                            drop 42
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.InvalidDropTarget);
    }

    /// <summary>Verifies that discarding a string literal is a semantic error.</summary>
    [Fact]
    public void Analyze_DiscardStringLiteral_ReportsError()
    {
        string source = """
                        routine test()
                            drop "hello"
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.InvalidDropTarget);
    }

    /// <summary>Verifies that discarding a member field access (not a call) is a semantic error.</summary>
    [Fact]
    public void Analyze_DiscardMemberAccess_ReportsError()
    {
        string source = """
                        record Wrapper
                            value: S32

                        routine test()
                            var w = Wrapper(value: 42)
                            drop w.value
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.InvalidDropTarget);
    }

    #endregion
}
