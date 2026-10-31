using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Diagnostics fixed before the 0.5 release: a variant member that already holds None, a doubled <c>?</c>, a
/// <c>posted</c> routine, a repeated <c>== 1</c> arm and a Work that nothing starts.
/// </summary>
public class ReleaseDiagnosticsTests
{
    [Fact]
    public void Analyze_VariantMemberThatHoldsNone_IsRejected()
    {
        AssertHasErrorSa(source: """
                                variant Bad
                                    S64?
                                    None

                                routine start()
                                    return
                                """,
            expectedErrorSubstring: "already holds None");
    }

    [Fact]
    public void Parse_DoubledQuestionMark_NamesTheType()
    {
        (_, Builder.Parser.Parser parser) = ParseWithErrors(source: """
                                                                    routine f(x: S64??) -> S64
                                                                        return 1
                                                                    """);
        Assert.True(condition: parser.HasErrors);
        Assert.Contains(collection: parser.GetErrors(),
            filter: e => e.Contains(value: "A type takes one '?'"));
    }

    [Fact]
    public void Parse_PostedRoutine_IsRejected()
    {
        AssertParseError(source: """
                                 posted routine foo() -> S64
                                     return 1
                                 """);
    }

    [Fact]
    public void Analyze_RepeatedComparisonArm_IsADuplicatePattern()
    {
        const string Source = """
                              routine pick(x: S64) -> S64
                                  return when x
                                      == 1 => 10
                                      == 1 => 20
                                      else => 0
                              """;
        AnalysisResult rf = AnalyzeSa(source: Source);
        Assert.Contains(collection: rf.Errors, filter: e => e.Code == SemanticDiagnosticCode.DuplicatePattern);
    }

    [Fact]
    public void Analyze_OverlappingEqualsArm_IsADuplicatePattern()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine pick(x: S64) -> S64
                                                      return when x
                                                          == 1, 2 => 10
                                                          == 2 => 20
                                                          else => 0
                                                  """);
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.DuplicatePattern);
    }

    [Fact]
    public void Analyze_WorkBoundToAVariableThatNothingStarts_WarnsW008()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine work(x: S64) -> S64
                                                      return x + 1

                                                  routine start()
                                                      var job = work.to[Work](x: 3)
                                                      return
                                                  """);
        Assert.Contains(collection: result.Warnings,
            filter: w => w.Code == SemanticWarningCode.AsyncWorkNeverLaunched);
    }

    [Fact]
    public void Analyze_WorkThatIsStartedAndCollected_DoesNotWarnW008()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine work(x: S64) -> S64
                                                      return x + 1

                                                  routine start()
                                                      var job = work.to[Work](x: 3)
                                                      job.start_now()
                                                      drop job.retrieve()
                                                      return
                                                  """);
        Assert.DoesNotContain(collection: result.Warnings,
            filter: w => w.Code == SemanticWarningCode.AsyncWorkNeverLaunched);
    }
}
