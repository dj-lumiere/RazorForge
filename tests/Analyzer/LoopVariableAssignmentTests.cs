using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// The name a <c>for</c> loop binds each element to is bound again every round, so assigning it is RF-S251 with a
/// message that says it is the loop variable: over a collection (where the element is a token), over a range (a
/// counter loop), and for each name of a destructured element. A local declared in the body stays assignable.
/// </summary>
public class LoopVariableAssignmentTests
{
    [Fact]
    public void LoopVariable_Assigned_ReportedAsLoopVariable()
    {
        string source = """
                        record Pt
                            x: S64

                        routine test()
                            var pts = [Pt(x: 1), Pt(x: 2)]
                            for p in pts
                                p = Pt(x: 9)
                            for i in 0 til 3
                                i = 7
                            for (a, b) in [(1, 2), (3, 4)]
                                a = 0
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        List<SemanticError> loopErrors = result.Errors
                                               .Where(predicate: e => e.Code == SemanticDiagnosticCode.AssignmentToImmutable &&
                                                                      e.Message.Contains(value: "is the loop variable"))
                                               .ToList();
        Assert.Equal(expected: 3, actual: loopErrors.Count);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.AssignmentTypeMismatch);
    }

    [Fact]
    public void LocalInLoopBody_Assigned_NoErrors()
    {
        string source = """
                        routine test()
                            for i in 0 til 3
                                var j = i
                                j = 7
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.AssignmentToImmutable);
    }
}
