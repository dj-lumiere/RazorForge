using SyntaxTree;
using Builder.Diagnostics;
using Builder.Verification.Results;
using TypeModel.Symbols;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Contains tests for error handling validation.
/// </summary>
public class ErrorHandlingValidationTests
{
    #region Throw Needs Named Crashable Type

    /// <summary>
    /// Verifies semantic analysis behavior for throw entity no record error.
    /// </summary>
    [Fact]
    public void Analyze_ThrowEntity_NoRecordError()
    {
        string source = """
                        crashable BadError
                            message: Text
                        routine BadError.crash_message() -> Text
                            return "BadError"
                        failable routine test() -> S32
                            throw BadError(message: "oops")
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ThrowRequiresRecordType);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for throw record no record error.
    /// </summary>
    [Fact]
    public void Analyze_ThrowRecord_NoRecordError()
    {
        string source = """
                        crashable MyError
                            message: Text
                        routine MyError.crash_message() -> Text
                            return "MyError"
                        failable routine test() -> S32
                            throw MyError(message: "oops")
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ThrowRequiresRecordType);
    }

    #endregion

    #region Failable Without Throw or Absent

    /// <summary>
    /// Verifies semantic analysis behavior for failable without throw or absent and reports the expected warning.
    /// </summary>
    [Fact]
    public void Analyze_FailableWithoutThrow_ReportsWarning()
    {
        string source = """
                        failable routine useless() -> S32
                            return 42
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.FailableWithoutThrow);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for failable with throw without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_FailableWithThrow_NoError()
    {
        string source = """
                        crashable MyError
                            message: Text
                        routine MyError.crash_message() -> Text
                            return "MyError"
                        failable routine useful() -> S32
                            throw MyError(message: "bad")
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.FailableWithoutThrow);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for failable with absent without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_FailableWithAbsent_NoError()
    {
        string source = """
                        failable routine find() -> S32
                            throw AbsentValueError()
                            return 0
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.FailableWithoutThrow);
    }

    #endregion

    #region @crash_only Validation (#76)

    /// <summary>
    /// Verifies semantic analysis behavior for crash only on non failable and reports the expected error.
    /// </summary>
    [Fact]
    public void Analyze_CrashOnlyOnNonFailable_ReportsError()
    {
        string source = """
                        @crash_only
                        routine safe_routine() -> S32
                            return 42
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.CrashOnlyOnNonFailable);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for crash only on failable without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_CrashOnlyOnFailable_NoError()
    {
        string source = """
                        crashable MyError
                            message: Text
                        routine MyError.crash_message() -> Text
                            return "MyError"
                        @crash_only
                        failable routine crash_routine() -> S32
                            throw MyError(message: "fatal")
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.CrashOnlyOnNonFailable);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for crash only suppresses variant generation.
    /// </summary>
    [Fact]
    public void Analyze_CrashOnlySuppressesVariantGeneration()
    {
        string source = """
                        crashable MyError
                            message: Text
                        routine MyError.crash_message() -> Text
                            return "MyError"
                        @crash_only
                        failable routine crash_routine() -> S32
                            throw MyError(message: "fatal")
                        """;

        AnalysisResult result = AnalyzeSa(source: source);

        // Should NOT generate try or check variants
        Assert.Null(@object: result.Registry.FindRecoveryVariant(recovered: result.Registry.GetRoutine(name: "crash_routine")!, kind: RecoveryKind.Try));
        Assert.Null(@object: result.Registry.FindRecoveryVariant(recovered: result.Registry.GetRoutine(name: "crash_routine")!, kind: RecoveryKind.Check));
    }
    /// <summary>
    /// Verifies semantic analysis behavior for non crash only generates variants.
    /// </summary>
    [Fact]
    public void Analyze_NonCrashOnlyGeneratesVariants()
    {
        string source = """
                        crashable MyError
                            message: Text
                        routine MyError.crash_message() -> Text
                            return "MyError"
                        failable routine normal_routine() -> S32
                            throw MyError(message: "error")

                        routine trigger_variants() -> S32?
                            return try normal_routine()
                        """;

        AnalysisResult result = AnalyzeSa(source: source);

        // Without @crash_only, variants are synthesized ON DEMAND — the `try` keyword above generates it.
        RoutineInfo? tryVariant = result.Registry.FindRecoveryVariant(recovered: result.Registry.GetRoutine(name: "normal_routine")!, kind: RecoveryKind.Try);
        Assert.NotNull(@object: tryVariant);
    }

    #endregion

    #region Unhandled Crashable Call (#159)

    /// <summary>
    /// A bare failable call used as a statement is currently NOT flagged with the
    /// UnhandledCrashableCall warning — that advisory is intentionally not enforced.
    /// </summary>
    [Fact]
    public void Analyze_FailableCallAsStatement_InNonFailable_NotFlagged()
    {
        string source = """
                        crashable ParseError
                            message: Text
                        routine ParseError.crash_message() -> Text
                            return "ParseError"
                        failable routine parse(data: S32) -> S32
                            throw ParseError(message: "bad")
                        routine caller() -> S32
                            parse(data: 1)
                            return 0
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Warnings,
            filter: w => w.Code == SemanticWarningCode.UnhandledCrashableCall);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for failable call as statement in failable without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_FailableCallAsStatement_InFailable_NoError()
    {
        string source = """
                        crashable ParseError
                            message: Text
                        routine ParseError.crash_message() -> Text
                            return "ParseError"
                        failable routine parse(data: S32) -> S32
                            throw ParseError(message: "bad")
                        failable routine caller() -> S32
                            parse(data: 1)
                            return 0
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Warnings,
            filter: w => w.Code == SemanticWarningCode.UnhandledCrashableCall);
    }


    /// <summary>
    /// Verifies semantic analysis behavior for result copied from variable and reports the expected error.
    /// </summary>
    [Fact]
    public void Analyze_ResultCopiedFromVariable_ReportsError()
    {
        string source = """
                        crashable ParseError
                            message: Text
                        routine ParseError.crash_message() -> Text
                            return "ParseError"

                        @readonly
                        routine ParseError.crash_message() -> Text
                            return me.message

                        failable routine validate(value: S32) -> S32
                            if value < 0
                                throw ParseError(message: "negative")
                            return value

                        routine test()
                            var first = check validate(value: 1)
                            var second = first
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ErrorHandlingTypeStoredInVariable);
    }

    #endregion

    #region Throw target validation

    /// <summary>
    /// Verifies that throwing a primitive integer reports ThrowRequiresRecordType.
    /// </summary>
    [Fact]
    public void Analyze_ThrowPrimitive_ReportsError()
    {
        string source = """
                        failable routine test() -> S32
                            throw 42
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ThrowNotCrashable);
    }

    /// <summary>
    /// Verifies that a failable routine can throw different crashable types on different paths.
    /// </summary>
    [Fact]
    public void Analyze_MultipleCrashableTypes_AllThrow_NoError()
    {
        string source = """
                        crashable ErrA
                            message: Text
                        routine ErrA.crash_message() -> Text
                            return "ErrA"
                        crashable ErrB
                            code: S32
                        routine ErrB.crash_message() -> Text
                            return "ErrB"
                        failable routine test(flag: Bool) -> S32
                            if flag
                                throw ErrA(message: "a")
                            throw ErrB(code: 1)
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ThrowRequiresRecordType);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.FailableWithoutThrow);
    }

    /// <summary>
    /// Verifies that a failable routine calling another failable propagates failability without error.
    /// </summary>
    [Fact]
    public void Analyze_FailableCallingFailable_Propagates_NoError()
    {
        string source = """
                        crashable MyErr
                            message: Text
                        routine MyErr.crash_message() -> Text
                            return "MyErr"
                        failable routine inner(value: S32) -> S32
                            if value < 0
                                throw MyErr(message: "bad")
                            return value
                        failable routine outer(value: S32) -> S32
                            return inner(value: value)
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.FailableWithoutThrow);
    }

    #endregion

    // NOTE: #81 (Result/Lookup storage restriction) tests require multi-module test
    // infrastructure since check_/lookup_ variants are generated in Phase 5 but body
    // analysis happens in Phase 3. The implementation is in place and will be validated
    // when multi-module compilation support is available for tests.
}
