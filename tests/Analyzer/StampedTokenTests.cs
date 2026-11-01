using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Stamps reached through tokens and stamps whose type is inferred. The token `fetch()` gives comes from the stamp
/// table, so it has no source in the stamp: a read token on a list of stamps still fetches (RF-S456 is for a write
/// into what the read token reaches), and a write through the fetched token ends nothing (RF-S643). A `stamp()` gives
/// an entity type of the program its slot whether the stamp's type is written or inferred (RF-S656 stays for a
/// standard library entity).
/// </summary>
public class StampedTokenTests
{
    private const string Prelude = """
                                   import IO/Console

                                   entity Creature
                                       x: S64
                                       y: S64

                                   """;

    [Fact]
    public void Analyze_FieldWriteThroughFetchedToken_NoError()
    {
        string source = Prelude + """
                                  routine nudge(stamps: Modifying[List[Stamped[Creature]]])
                                      for at in stamps
                                          var c = at.fetch()
                                          c.y = 5
                                          c.x = c.y + 2
                                      return

                                  routine start()
                                      var a = Creature(x: 1, y: 1)
                                      var stamps: List[Stamped[Creature]] = [a.stamp()]
                                      nudge(stamps: stamps)
                                      show(a.x)
                                      return
                                  """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_FetchThroughReadToken_NoError()
    {
        string source = Prelude + """
                                  routine total(stamps: Viewing[List[Stamped[Creature]]]) -> S64
                                      var sum: S64 = 0
                                      for at in stamps
                                          sum += at.fetch().x
                                      return sum

                                  routine start()
                                      var a = Creature(x: 4, y: 0)
                                      var stamps: List[Stamped[Creature]] = [a.stamp()]
                                      show(total(stamps: stamps))
                                      return
                                  """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_InferredStampType_NoError()
    {
        string source = Prelude + """
                                  routine start()
                                      var a = Creature(x: 4, y: 0)
                                      var at = a.stamp()
                                      var all = [a.stamp(), at]
                                      show(all.count())
                                      return
                                  """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_StampsCompareByIdentity_NoError()
    {
        string source = Prelude + """
                                  routine start()
                                      var a = Creature(x: 4, y: 0)
                                      var s = a.stamp()
                                      var t = a.stamp()
                                      show(s === t)
                                      show(s !== t)
                                      return
                                  """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_StampValueEquality_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var a = Creature(x: 4, y: 0)
                                      var s = a.stamp()
                                      var t = a.stamp()
                                      show(s == t)
                                      return
                                  """;

        AssertHasErrorSa(source: source, expectedErrorSubstring: "a 'Stamped' names an entity");
    }

    [Fact]
    public void Analyze_StampOfStandardLibraryEntity_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var xs: List[S64] = []
                                      var at = xs.stamp()
                                      return
                                  """;

        // A program file (a source extension) is where a stamp needs its slot.
        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: "has no slot for stamps",
            fileName: "stamp_of_stdlib_entity.rf");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.StampOfTypeWithoutSlot);
    }
}
