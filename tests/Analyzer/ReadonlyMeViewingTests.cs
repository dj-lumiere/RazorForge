using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// The <c>me</c> of a <c>@readonly</c> member routine follows the rule of a <c>Viewing</c> (C154): a write token
/// reached through <c>me</c> is a read token, and a value that holds a way to change an entity is read in place from
/// <c>me</c>, never copied out (RV-S657). Also the token a <c>for</c> loop over values hands out, which reads as the
/// value it is on where a value is wanted.
/// </summary>
public class ReadonlyMeViewingTests
{
    private const string Prelude = """
                                   import IO/Console

                                   entity Creature
                                       x: S64

                                   record Link
                                       target: Stamped[Creature]
                                       weight: S64

                                   entity Holder
                                       link: Link

                                   """;

    [Fact]
    public void Analyze_ReadonlyReadsStampInPlace_NoError()
    {
        string source = Prelude + """
                                  @readonly
                                  routine Holder.peek() -> S64
                                      return me.link.target.fetch().x + me.link.weight
                                  """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_ReadonlyFetchThroughMe_GivesReadToken()
    {
        string source = Prelude + """
                                  @readonly
                                  routine Holder.poke()
                                      me.link.target.fetch().x = 3
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: "only reads the value");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.WriteThroughReadOnlyWrapper);
    }

    [Fact]
    public void Analyze_ReadonlyCopiesStampOutOfMe_Errors()
    {
        string source = Prelude + """
                                  @readonly
                                  routine Holder.copy_out() -> S64
                                      var l = me.link
                                      return l.weight
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "can't be copied out of 'me' in a '@readonly' routine");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ViewedValueHoldsWayToChange);
    }

    [Fact]
    public void Analyze_ChangingRoutineCopiesStampOutOfMe_NoError()
    {
        string source = Prelude + """
                                  routine Holder.copy_out() -> S64
                                      var l = me.link
                                      return l.weight
                                  """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_ForValueToken_IndexesAndFollowsNoStaleSource()
    {
        string source = """
                        routine start()
                            var xs = List[S64]()
                            xs.add_last(value: 2)
                            var seen = List[Bool](count: 3, fill_value: false)
                            for x in xs
                                seen[x] = true
                            return
                        """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_ValueAssignedToTokenVariable_ReportsOnlyTheAssignment()
    {
        string source = """
                        routine start()
                            var xs = List[S64]()
                            xs.add_last(value: 2)
                            for w in xs
                                var x = w
                                var next: S64 = 1
                                x = next
                                var y = x + 1
                            return
                        """;

        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: "Cannot assign value of type");
        Assert.DoesNotContain(collection: result.Errors, filter: e => e.Message.Contains(value: "went out of scope"));
    }
}
