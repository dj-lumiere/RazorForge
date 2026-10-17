using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A walk that starts read-only stays read-only: an <c>each</c> loop over a <c>Viewing</c> container, over an adapter
/// made from one, or over <c>me</c>'s container in a <c>@readonly</c> routine binds its elements as <c>Viewing</c>, so
/// writing one is RF-S631, compound assignment included. A walk from a <c>Modifying</c> start may write.
/// </summary>
public class ReadOnlyWalkTests
{
    private const string Box = """
                               import IterTools
                               entity Box
                                   n: S64
                               entity Shelf
                                   boxes: List[Box]

                               """;

    [Theory]
    [InlineData("""
                routine bump(boxes: Viewing[List[Box]])
                    each b in boxes
                        b.n += 1
                    return
                """)]
    [InlineData("""
                routine bump(boxes: Viewing[List[Box]])
                    each b in boxes
                        b.n = 0
                    return
                """)]
    [InlineData("""
                routine bump(boxes: Viewing[List[Box]])
                    each b in boxes.where(x => x.n > 1)
                        b.n = 0
                    return
                """)]
    [InlineData("""
                @readonly
                routine Shelf.clear_all()
                    each b in me.boxes
                        b.n = 0
                    return
                """)]
    [InlineData("""
                routine bump(b: Viewing[Box])
                    b.n += 1
                    return
                """)]
    public void Analyze_WriteThroughReadOnlyWalk_ReportsWriteThroughReadOnly(string routine)
    {
        AnalysisResult result = AnalyzeSa(source: Box + routine);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.WriteThroughReadOnlyWrapper);
    }

    [Fact]
    public void Analyze_WriteThroughModifyingWalk_NoError()
    {
        AnalysisResult result = AnalyzeSa(source: Box + """
                                                       routine bump(boxes: Modifying[List[Box]])
                                                           each b in boxes
                                                               b.n += 1
                                                           return
                                                       """);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.WriteThroughReadOnlyWrapper);
    }
}
