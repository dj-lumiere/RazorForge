using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Stamps reached through tokens and stamps whose type is inferred. The token `fetch()` gives comes from the stamp
/// table, so it has no source in the stamp, and a write through the fetched token ends nothing (RV-S643). Nothing that
/// changes comes out of a `Viewing`: a `fetch()` through a read token gives a read token (a write through it is
/// RV-S631), and a value that holds a way to change an entity (a stamp, a manager handle, a write token, a record or
/// tuple or Optional holding one) is read in place from a view, never copied out of it (RV-S657). A stamp is made by
/// `PoolManager.add`, which moves an entity into the pool, and a Work on another thread can't reach the pool (RV-S448).
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
                                      var pool = PoolManager.set_up()
                                      var a = PoolManager.add(value: Creature(x: 1, y: 1))
                                      var stamps: List[Stamped[Creature]] = [a]
                                      nudge(stamps: stamps)
                                      show(a.fetch().x)
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
                                      var pool = PoolManager.set_up()
                                      var a = PoolManager.add(value: Creature(x: 4, y: 0))
                                      var stamps: List[Stamped[Creature]] = [a]
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
                                      var pool = PoolManager.set_up()
                                      var at = PoolManager.add(value: Creature(x: 4, y: 0))
                                      var all = [at, at]
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
                                      var pool = PoolManager.set_up()
                                      var s = PoolManager.add(value: Creature(x: 4, y: 0))
                                      var t = s
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
                                      var pool = PoolManager.set_up()
                                      var s = PoolManager.add(value: Creature(x: 4, y: 0))
                                      var t = s
                                      show(s == t)
                                      return
                                  """;

        AssertHasErrorSa(source: source, expectedErrorSubstring: "a 'Stamped' names an entity");
    }

    [Fact]
    public void Analyze_OptionalStampValueEquality_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var pool = PoolManager.set_up()
                                      var s: Stamped[Creature]? = PoolManager.add(value: Creature(x: 4, y: 0))
                                      var t: Stamped[Creature]? = s
                                      show(s == t)
                                      return
                                  """;

        AssertHasErrorSa(source: source, expectedErrorSubstring: "a 'Stamped' names an entity");
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

    /// <summary>A stamp is never empty: it has no member variables to build one from, so only `add` makes one.</summary>
    [Fact]
    public void Analyze_StampBuiltFromItsMemberVariables_IsReported()
    {
        string source = Prelude + """
                                  routine start()
                                      var empty = Stamped[Creature](slot: 0, generation: 0)
                                      return
                                  """;

        AssertHasErrorSa(source: source,
            expectedErrorSubstring: "does not have a member variable named 'slot'");
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
                                      var pool = PoolManager.set_up()
                                      show(x_of(link: PoolManager.add(value: Creature(x: 1, y: 1))))
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

    /// <summary>An entity inside the optional is reached where it is kept (a write token), not through a copy.</summary>
    [Fact]
    public void Analyze_TryCallOnOptionalEntity_ReachesTheOriginal()
    {
        string source = Prelude + """
                                  routine Creature.bump() -> S64
                                      return me.x

                                  routine start()
                                      var maybe: Creature? = none
                                      show(try maybe.bump())
                                      return
                                  """;

        AssertAnalyzesSa(source: source);
    }

    /// <summary>A routine a Work runs on another thread, or one it calls, can't reach the pool (RV-S448).</summary>
    [Fact]
    public void Analyze_PoolReachedByWorkThroughACall_Errors()
    {
        string source = Prelude + """
                                  routine read(s: Stamped[Creature]) -> S64
                                      return s.fetch().x

                                  routine job(s: Stamped[Creature]) -> S64
                                      return read(s: s)

                                  routine start()
                                      var pool = PoolManager.set_up()
                                      var at = PoolManager.add(value: Creature(x: 1, y: 1))
                                      var w = job.to[Work](s: at)
                                      w.start_apart()
                                      show(w.retrieve())
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "'read', which 'job' calls, uses the PoolManager");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.GlobalChangedAcrossThreads);
    }

    /// <summary>A Work on another thread that does not touch the pool analyzes clean next to a pool.</summary>
    [Fact]
    public void Analyze_WorkThatLeavesThePoolAlone_NoError()
    {
        string source = Prelude + """
                                  routine job(n: S64) -> S64
                                      return n + 1

                                  routine start()
                                      var pool = PoolManager.set_up()
                                      var at = PoolManager.add(value: Creature(x: 1, y: 1))
                                      var w = job.to[Work](n: 4)
                                      w.start_apart()
                                      show(w.retrieve())
                                      show(at.fetch().x)
                                      return
                                  """;

        AssertAnalyzesSa(source: source);
    }
}
