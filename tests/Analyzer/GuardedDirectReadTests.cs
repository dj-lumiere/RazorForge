namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A <c>Guarded[T, ReadOnly]</c> has no lock, so its value is read directly (and only read). A Guarded under the
/// Exclusive or MultiRead policy is reached only inside a <c>using</c> that holds its lock.
/// </summary>
public class GuardedDirectReadTests
{
    private const string Prelude = """
                                   entity Stats
                                       name: Text
                                       open: Atomic[U64]

                                   @readonly
                                   routine Stats.label() -> Text
                                       return me.name

                                   routine Stats.rename(to: Text)
                                       me.name = to
                                       return

                                   """;

    [Fact]
    public void Analyze_ReadOnlyGuarded_ReadsDirectly()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           routine start()
                                               var g = Guarded[Stats, ReadOnly](from: Stats(name: "a", open: Atomic[U64](initial: 0)))
                                               var n = g.name
                                               var l = g.label()
                                               g.open.fetch_add(delta: 1)
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_ReadOnlyGuarded_WriteIsRejected()
    {
        AssertHasErrorSa(source: Prelude + """
                                           routine start()
                                               var g = Guarded[Stats, ReadOnly](from: Stats(name: "a", open: Atomic[U64](initial: 0)))
                                               g.rename(to: "b")
                                               return
                                           """,
            expectedErrorSubstring: "ReadOnly policy, so its value is only read");
    }

    [Fact]
    public void Analyze_ExclusiveGuarded_DirectReadIsRejected()
    {
        AssertHasErrorSa(source: Prelude + """
                                           routine start()
                                               var g = Guarded[Stats, Exclusive](from: Stats(name: "a", open: Atomic[U64](initial: 0)))
                                               var n = g.name
                                               return
                                           """,
            expectedErrorSubstring: "reached only while its lock is held");
    }

    [Fact]
    public void Analyze_MultiReadGuarded_DirectCallIsRejected()
    {
        AssertHasErrorSa(source: Prelude + """
                                           routine start()
                                               var g = Guarded[Stats, MultiRead](from: Stats(name: "a", open: Atomic[U64](initial: 0)))
                                               var l = g.label()
                                               return
                                           """,
            expectedErrorSubstring: "'using g.view() as v' to read it");
    }
}
