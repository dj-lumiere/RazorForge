using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// An emitter hands out each item once, so an <c>each</c> over a named emitter that has certainly run out would run zero
/// times: RF-S517. That is a second <c>each</c> after one that ended by itself on every path, or an <c>each</c> inside a
/// loop over an emitter made outside it. An emitter left with <c>break</c> goes on where it stopped, and anything that
/// may refill the name (a new value, a line naming it or its collection) makes it usable again.
/// </summary>
public sealed class EmitterReuseTests
{
    [Fact]
    public void Analyze_SecondEachAfterFullWalk_ReportsError()
    {
        AnalysisResult result = AnalyzeSa(source: Routine(body: """
                                                                    var it = xs.emitter()
                                                                    each x in it
                                                                        nothing
                                                                    each x in it
                                                                        nothing
                                                                """));
        SemanticError error = Assert.Single(collection: result.Errors,
            predicate: e => e.Code == SemanticDiagnosticCode.EmitterRunOut);
        Assert.Contains(expectedSubstring: "runs zero times: the each on line 4", actualString: error.Message);
        Assert.Contains(expectedSubstring: "var it = xs.emitter()", actualString: error.Message);
        Assert.Contains(expectedSubstring: "each x in xs", actualString: error.Message);
    }

    [Fact]
    public void Analyze_EachInLoopOverEmitterMadeOutside_ReportsError()
    {
        AnalysisResult result = AnalyzeSa(source: Routine(body: """
                                                                    var points = xs.emitter()
                                                                    each q in 0 til 3
                                                                        each point in points
                                                                            nothing
                                                                """));
        SemanticError error = Assert.Single(collection: result.Errors,
            predicate: e => e.Code == SemanticDiagnosticCode.EmitterRunOut);
        Assert.Contains(expectedSubstring: "on every pass of the loop on line 4 after the first", actualString: error.Message);
        Assert.Contains(expectedSubstring: "Make the emitter inside the loop", actualString: error.Message);
    }

    [Fact]
    public void Analyze_EachInUntilLoopOverEmitterMadeOutside_ReportsError()
    {
        AnalysisResult result = AnalyzeSa(source: Routine(body: """
                                                                    var steps = xs.emitter()
                                                                    var n: S64 = 0
                                                                    until n == 2
                                                                        n += 1
                                                                        each s in steps
                                                                            nothing
                                                                """));
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.EmitterRunOut);
    }

    [Theory]
    // Left with break: the emitter goes on where it stopped.
    [InlineData("""
                    var it = xs.emitter()
                    each x in it
                        if x == 2
                            break
                    each x in it
                        nothing
                """)]
    // A new emitter on each pass.
    [InlineData("""
                    each q in 0 til 2
                        var fresh = xs.emitter()
                        each x in fresh
                            nothing
                """)]
    // An emitter made in place on each pass, and the collection itself.
    [InlineData("""
                    each q in 0 til 2
                        each x in xs.emitter()
                            nothing
                        each x in xs
                            nothing
                """)]
    // Emptied on one path only.
    [InlineData("""
                    var it = xs.emitter()
                    if xs.count() > 10
                        each x in it
                            nothing
                    each x in it
                        nothing
                """)]
    // Each pass leaves with break, so the emitter is not empty on the next.
    [InlineData("""
                    var it = xs.emitter()
                    each q in 0 til 2
                        each x in it
                            break
                """)]
    // A new value for the name.
    [InlineData("""
                    var it = xs.emitter()
                    each x in it
                        nothing
                    it = xs.emitter()
                    each x in it
                        nothing
                """)]
    // A line naming the collection the emitter was made from may refill it.
    [InlineData("""
                    var it = xs.emitter()
                    each x in it
                        nothing
                    drop xs.count()
                    each x in it
                        nothing
                """)]
    public void Analyze_EmitterUsableAgain_NoError(string body)
    {
        AnalysisResult result = AnalyzeSa(source: Routine(body: body));
        Assert.DoesNotContain(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.EmitterRunOut);
    }

    /// <summary>A routine around <paramref name="body"/> (indented by four spaces), with a list <c>xs</c> made on
    /// line 2.</summary>
    private static string Routine(string body)
    {
        return "routine walk()\n" +
               "    var xs: List[S64] = [1, 2, 3]\n" +
               body + "\n" +
               "    return\n";
    }
}
