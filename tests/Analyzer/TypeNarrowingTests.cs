using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Contains tests for type narrowing.
/// </summary>
public class TypeNarrowingTests
{
    #region Guard Clause Narrowing (unless / if-return)

    /// <summary>
    /// Verifies semantic analysis behavior for unless is none narrows maybe to value.
    /// </summary>
    [Fact]
    public void Analyze_UnlessIsNone_NarrowsMaybeToValue()
    {
        // unless desugars to: if Not(value is None) { body }
        // The body runs when value is NOT None, so value
        // is narrowed from Optional[S32] to S32 inside the block
        string source = """
                        routine process(value: S32?) -> S32
                            if not (value is None)
                                return value
                            return 0
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for if is none return narrows maybe to value.
    /// </summary>
    [Fact]
    public void Analyze_IfIsNoneReturn_NarrowsMaybeToValue()
    {
        // if value is None { return 0 }
        // After the if (guard clause), value narrows til S32
        string source = """
                        routine process(value: S32?) -> S32
                            if value is None
                                return 0
                            return value
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for if is not none narrows in then branch.
    /// </summary>
    [Fact]
    public void Analyze_IfIsNotNone_NarrowsInThenBranch()
    {
        // if value isnot None { return value }
        // The then branch narrows value to S32
        string source = """
                        routine process(value: S32?) -> S32
                            if value isnot None
                                return value
                            return 0
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for if is none with else narrows in else branch.
    /// </summary>
    [Fact]
    public void Analyze_IfIsNoneWithElse_NarrowsInElseBranch()
    {
        // if value is None { ... } else { return value }
        // The else branch narrows value til S32
        string source = """
                        routine process(value: S32?) -> S32
                            if value is None
                                return 0
                            else
                                return value
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    /// <summary>
    /// Verifies semantic analysis behavior for if is none without exit does not narrow after if.
    /// </summary>
    [Fact]
    public void Analyze_IfIsNoneWithoutExit_DoesNotNarrowAfterIf()
    {
        // A non-exiting if branch must not narrow the remainder of the scope.
        string source = """
                        routine process(value: S32?) -> S32
                            if value is None
                                show("missing")
                            return value
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReturnTypeMismatch);
    }

    #endregion

    #region When Statement Narrowing

    /// <summary>
    /// Verifies semantic analysis behavior for when maybe else binds narrowed type.
    /// </summary>
    [Fact]
    public void Analyze_WhenMaybe_ElseBindsNarrowedType()
    {
        // when value { is None => ... | else v => ... }
        // After handling None, else v should be S32
        string source = """
                        routine process(value: S32?) -> S32
                            when value
                                is None => return 0
                                else as v => return v
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    #endregion

    #region Multi-Parameter Guard Narrowing

    /// <summary>
    /// Verifies that two independent guard clauses narrow two separate Optional parameters.
    /// </summary>
    [Fact]
    public void Analyze_TwoGuardClauses_BothNarrowed_NoError()
    {
        string source = """
                        routine process(a: S32?, b: S32?) -> S32
                            if a is None
                                return 0
                            if b is None
                                return 0
                            return a + b
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    #endregion

    #region Narrowing Does Not Escape Conditional Scope

    /// <summary>
    /// Verifies that narrowing inside an if-isnot-None branch does not persist after the branch.
    /// </summary>
    [Fact]
    public void Analyze_NarrowingInsideBranch_DoesNotEscapeScope_ReportsError()
    {
        string source = """
                        routine process(value: S32?) -> S32
                            if value isnot None
                                nothing
                            return value
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReturnTypeMismatch);
    }

    /// <summary>
    /// Verifies that narrowing inside an unless block narrows the unless body but not after.
    /// </summary>
    [Fact]
    public void Analyze_UnlessIsNone_NarrowedInsideOnly_AfterStillMaybe_ReportsError()
    {
        // The unless body executes when value is NOT None, so value is S32 inside.
        // After the unless block (if execution reaches there), value is still S32?.
        string source = """
                        routine process(value: S32?) -> S32
                            if not (value is None)
                                return value
                            return value
                        """;

        // The second return sees value as S32? — return type mismatch expected.
        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReturnTypeMismatch);
    }

    #endregion

    #region No Narrowing for Non-Error Types

    /// <summary>
    /// Verifies semantic analysis behavior for non error handling type no narrowing crash.
    /// </summary>
    [Fact]
    public void Analyze_NonErrorHandlingType_NoNarrowingCrash()
    {
        // Ensure narrowing logic doesn't break for non-error-handling types
        string source = """
                        routine process(value: S32) -> S32
                            if value isnot None
                                return value
                            return 0
                        """;

        // Should not crash -> the is/isnot check may produce warnings
        // but should not cause an internal error
        AnalysisResult result = AnalyzeSa(source: source);
        Assert.NotNull(@object: result);
    }

    #endregion

    #region Member path narrowing

    private const string PathPrelude = """
                                       record Leaf
                                           name: Text

                                       record Mid
                                           leaf: Leaf?

                                       record Top
                                           mid: Mid?
                                           note: Text?

                                       """;

    /// <summary>A field path left by `if a.b is None` has the payload type, at any depth.</summary>
    [Fact]
    public void Analyze_MemberPathGuardClause_NarrowsAtAnyDepth()
    {
        string source = PathPrelude + """
                                      routine deep(t: Top) -> Text
                                          if t.mid is None
                                              return "no mid"
                                          if t.mid.leaf is None
                                              return "no leaf"
                                          return t.mid.leaf.name
                                      """;

        Assert.Empty(collection: AnalyzeSa(source: source).Errors);
    }

    /// <summary>The `isnot None` branch narrows the path, and a `when` whose `is None` arm leaves does too.</summary>
    [Fact]
    public void Analyze_MemberPathIsNotNoneAndWhen_NarrowsPath()
    {
        string source = PathPrelude + """
                                      routine inside(t: Top) -> Text
                                          if t.note isnot None
                                              return t.note
                                          return ""

                                      routine after_when(t: Top) -> Text
                                          when t.note
                                              is None => return "n"
                                          return t.note
                                      """;

        Assert.Empty(collection: AnalyzeSa(source: source).Errors);
    }

    /// <summary>A call in between does not end the narrowing.</summary>
    [Fact]
    public void Analyze_MemberPathNarrowing_SurvivesCalls()
    {
        string source = PathPrelude + """
                                      routine other(t: Top) -> S64
                                          return 1

                                      routine keep(t: Top) -> Text
                                          if t.note is None
                                              return "n"
                                          drop other(t: t)
                                          return t.note
                                      """;

        Assert.Empty(collection: AnalyzeSa(source: source).Errors);
    }

    /// <summary>Assigning the path, or a name in front of it, ends the narrowing.</summary>
    [Fact]
    public void Analyze_MemberPathAssigned_EndsNarrowing()
    {
        string path = PathPrelude + """
                                    routine again(t: Top) -> Text
                                        var u = t
                                        if u.note is None
                                            return "n"
                                        u.note = none
                                        return u.note
                                    """;
        string prefix = PathPrelude + """
                                      routine again(t: Top) -> Text
                                          var u = t
                                          if u.note is None
                                              return "n"
                                          u = Top(mid: none, note: none)
                                          return u.note
                                      """;

        Assert.Contains(collection: AnalyzeSa(source: path).Errors, filter: e => e.Code == SemanticDiagnosticCode.ReturnTypeMismatch || e.Message.Contains(value: "Optional"));
        Assert.Contains(collection: AnalyzeSa(source: prefix).Errors, filter: e => e.Code == SemanticDiagnosticCode.ReturnTypeMismatch || e.Message.Contains(value: "Optional"));
    }

    #endregion
}
