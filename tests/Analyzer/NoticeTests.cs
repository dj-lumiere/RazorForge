using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Notices (likely slips that build as written, shown by check, lint and the language server, never by a build), code
/// that never runs, and the fixes diagnostics propose.
/// </summary>
public class NoticeTests
{
    [Fact]
    public void Analyze_UnusedResult_ProposesDrop()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine get_value() -> S32
                                                      return 42

                                                  routine test()
                                                      get_value()
                                                      return
                                                  """);
        SemanticWarning warning = Assert.Single(collection: result.Warnings,
            predicate: w => w.Code == SemanticWarningCode.UnusedRoutineReturnValue);
        TextEdit edit = Assert.Single(collection: warning.Fix!.Edits);
        Assert.Equal(expected: "drop ", actual: edit.NewText);
        Assert.Equal(expected: (5, 5), actual: (edit.StartLine, edit.StartColumn));
    }

    [Fact]
    public void Analyze_UntilFalse_NoticeProposesLoop()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine test()
                                                      var n: S64 = 0
                                                      until false
                                                          n += 1
                                                          if n > 3
                                                              break
                                                      return
                                                  """);
        SemanticWarning notice = Assert.Single(collection: result.Notices,
            predicate: n => n.Code == SemanticWarningCode.EndlessUntil);
        TextEdit edit = Assert.Single(collection: notice.Fix!.Edits);
        Assert.Equal(expected: ("loop", 3, 5, 16), actual: (edit.NewText, edit.StartLine, edit.StartColumn, edit.EndColumn));
    }

    [Fact]
    public void Analyze_CodeAfterLoopWithoutBreak_NeverRuns()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine test(n: S64) -> S64
                                                      var i: S64 = 0
                                                      loop
                                                          if i > n
                                                              return i
                                                          i += 1
                                                      return 0
                                                  """);
        SemanticError error = Assert.Single(collection: result.Errors,
            predicate: e => e.Code == SemanticDiagnosticCode.UnreachableStatement);
        Assert.Equal(expected: 7, actual: error.Location.Line);
    }

    [Fact]
    public void Analyze_LoopOnlyLeftByReturn_NeedsNoReturnAfter()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine test(n: S64) -> S64
                                                      loop with i: S64 = 0
                                                          if i > n
                                                              return i
                                                          continue with i: i + 1
                                                  """);
        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void Analyze_UntilTrue_BodyNeverRuns()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine test()
                                                      var n: S64 = 0
                                                      until true
                                                          n += 1
                                                          n += 2
                                                      return
                                                  """);
        SemanticWarning notice = Assert.Single(collection: result.Notices,
            predicate: n => n.Code == SemanticWarningCode.UnreachableLoopBody);
        Assert.True(condition: notice.NeverRuns);
        Assert.Equal(expected: (4, 5), actual: (notice.Location.Line, notice.EndLine));
    }

    [Fact]
    public void Analyze_NamedBoolCondition_HasNoNotice()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine test()
                                                      var done = false
                                                      until done
                                                          done = true
                                                      return
                                                  """);
        Assert.Empty(collection: result.Notices);
    }

    [Fact]
    public void Analyze_CodeAfterReturn_NeverRuns()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine test() -> S64
                                                      return 1
                                                      var a: S64 = 2
                                                      var b: S64 = 3
                                                  """);
        SemanticError error = Assert.Single(collection: result.Errors,
            predicate: e => e.Code == SemanticDiagnosticCode.UnreachableStatement);
        Assert.True(condition: error.NeverRuns);
        Assert.Equal(expected: (3, 4), actual: (error.Location.Line, error.EndLine));
    }

    [Fact]
    public void Analyze_MixedNumberNames_ProposeConversions()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine test(a: S32, b: U32) -> S64
                                                      return a + b
                                                  """);
        SemanticError error = Assert.Single(collection: result.Errors,
            predicate: e => e.Code == SemanticDiagnosticCode.FixedWidthTypeMismatch);
        Assert.Equal(expected: [".to[S64]()", ".to[S64]()"], actual: error.Fix!.Edits.Select(selector: e => e.NewText));
    }
}
