using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Stamps reached through tokens and stamps whose type is inferred. The token `fetch()` gives comes from the stamp
/// table, so it has no source in the stamp, and a write through the fetched token ends nothing (RF-S643). Nothing that
/// changes comes out of a `Viewing`: a `fetch()` through a read token gives a read token (a write through it is
/// RF-S631), and a value that holds a way to change an entity (a stamp, a manager handle, a write token, a record or
/// tuple or Optional holding one) is read in place from a view, never copied out of it (RF-S657). A `stamp()` gives
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

    private const string LinkPrelude = Prelude + """
                                       record Link
                                           target: Stamped[Creature]
                                           weight: S64

                                       """;

    [Fact]
    public void Analyze_FetchThroughReadToken_GivesReadToken()
    {
        string source = LinkPrelude + """
                                      routine bump(stamps: Viewing[List[Stamped[Creature]]])
                                          for at in stamps
                                              at.fetch().y = 3
                                          return
                                      """;

        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: "only reads the value");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.WriteThroughReadOnlyWrapper);
    }

    [Fact]
    public void Analyze_FetchOfIndexedStampThroughReadToken_GivesReadToken()
    {
        string source = LinkPrelude + """
                                      routine bump(stamps: Viewing[List[Stamped[Creature]]])
                                          var c = stamps[0].fetch()
                                          c.y = 5
                                          return
                                      """;

        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: "only reads the value");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.WriteThroughReadOnlyWrapper);
    }

    [Fact]
    public void Analyze_StampInRecordReadInPlaceThroughReadToken_NoError()
    {
        string source = LinkPrelude + """
                                      routine total(links: Viewing[List[Link]]) -> S64
                                          var sum: S64 = links[0].target.fetch().x + links[0].weight
                                          for l in links
                                              sum += l.target.fetch().x
                                          return sum
                                      """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_RecordWithStampCopiedOutOfReadToken_Errors()
    {
        string source = LinkPrelude + """
                                      routine copy_out(links: Viewing[List[Link]])
                                          var l = links[0]
                                          return
                                      """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "'l' holds a way to change an entity (target: Stamped[");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ViewedValueHoldsWayToChange);
    }

    [Fact]
    public void Analyze_StampCopiedOutOfReadToken_Errors()
    {
        string source = LinkPrelude + """
                                      routine copy_out(links: Viewing[List[Link]])
                                          var s = links[0].target
                                          return
                                      """;

        AssertHasErrorSa(source: source, expectedErrorSubstring: "can't be copied out of 'links'");
    }

    [Fact]
    public void Analyze_ViewedHolderPassedReturnedOrCollected_Errors()
    {
        string source = LinkPrelude + """
                                      routine take(l: Link)
                                          return

                                      routine give(links: Viewing[List[Link]]) -> Link
                                          return links[0]

                                      routine pass(links: Viewing[List[Link]])
                                          take(l: links[0])
                                          return

                                      routine collect(links: Viewing[List[Link]]) -> List[Link]
                                          var kept: List[Link] = []
                                          kept.add_last(value: links[0])
                                          return kept
                                      """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "can't be copied out of 'links'");
        Assert.Equal(expected: 3,
            actual: result.Errors.Count(predicate: e => e.Code == SemanticDiagnosticCode.ViewedValueHoldsWayToChange));
    }

    [Fact]
    public void Analyze_ManagerHandleInRecordCopiedOutOfReadToken_Errors()
    {
        string source = Prelude + """
                                  record Holder
                                      keep: Retained[Creature]

                                  routine copy_out(holders: Viewing[List[Holder]])
                                      var h = holders[0]
                                      h.keep.modify().y = 99
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "'h' holds a way to change an entity (keep: Retained[");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ViewedValueHoldsWayToChange);
    }

    [Fact]
    public void Analyze_OptionalStampBoundByWhenOverReadToken_ReadsInPlace()
    {
        string source = Prelude + """
                                  routine peek_x(stamps: Viewing[List[Stamped[Creature]?]]) -> S64
                                      when stamps[0]
                                          is None => return -1
                                          else as at => return at.fetch().x
                                  """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_OptionalStampBoundByWhenOverReadToken_CopyErrors()
    {
        string source = Prelude + """
                                  routine copy_x(stamps: Viewing[List[Stamped[Creature]?]]) -> S64
                                      when stamps[0]
                                          is None => return -1
                                          else as at =>
                                              var kept = at
                                              return kept.fetch().x
                                  """;

        AssertHasErrorSa(source: source, expectedErrorSubstring: "holds a way to change an entity");
    }

    [Fact]
    public void Analyze_HolderCopiedOutOfWriteToken_NoError()
    {
        string source = LinkPrelude + """
                                      routine copy_out(links: Modifying[List[Link]]) -> Link
                                          var l = links[0]
                                          return l
                                      """;

        AssertAnalyzesSa(source: source);
    }

    [Fact]
    public void Analyze_PlainValueCopiedOutOfReadToken_NoError()
    {
        string source = LinkPrelude + """
                                      routine weights(links: Viewing[List[Link]]) -> S64
                                          var w = links[0].weight
                                          return w
                                      """;

        AssertAnalyzesSa(source: source);
    }

    /// <summary>A stamp is never empty: its member variables are secret, so a program can't build one (RF-S403).</summary>
    [Fact]
    public void Analyze_StampBuiltFromItsMemberVariables_IsReported()
    {
        string source = Prelude + """
                                  routine start()
                                      var empty = Stamped[Creature](slot: 0, generation: 0)
                                      return
                                  """;

        AssertHasErrorSa(source: source, expectedErrorSubstring: "'slot' is secret");
    }

    /// <summary>A call on a Stamped[E]? under try is allowed (a none link is an AbsentValueError the keyword receives).</summary>
    [Fact]
    public void Analyze_TryFetchOnOptionalStamp_NoError()
    {
        string source = Prelude + """
                                  routine x_of(link: Stamped[Creature]?) -> S64
                                      when try link.fetch()
                                          is None => return -1
                                          else as c => return c.x

                                  routine start()
                                      var a = Creature(x: 1, y: 1)
                                      show(x_of(link: a.stamp()))
                                      return
                                  """;

        AssertAnalyzesSa(source: source);
    }

    /// <summary>Without try or check a call on a T? is still an error.</summary>
    [Fact]
    public void Analyze_FetchOnOptionalStampWithoutKeyword_IsReported()
    {
        string source = Prelude + """
                                  routine x_of(link: Stamped[Creature]?) -> S64
                                      var c = link.fetch()
                                      return c.x
                                  """;

        AssertHasErrorSa(source: source, expectedErrorSubstring: "No routine 'fetch()' is defined on");
    }

    /// <summary>An entity inside the optional is not reached through it (a copy would be changed).</summary>
    [Fact]
    public void Analyze_TryCallOnOptionalEntity_IsReported()
    {
        string source = Prelude + """
                                  routine Creature.bump() -> S64
                                      return me.x

                                  routine start()
                                      var maybe: Creature? = none
                                      show(try maybe.bump())
                                      return
                                  """;

        AssertHasErrorSa(source: source, expectedErrorSubstring: "is not a record");
    }
}
