using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// `==` and a hash come from the member variables: a record or entity whose member variable has no `==` has none, a
/// record whose member variable has no hash has none, and an entity hashes only when it declares `obeys Hashable`.
/// Each missing one is a build error where it is used, naming the member variable.
/// </summary>
public class ValueEqualityTests
{
    /// <summary>`==` on a record holding a routine value names that member variable.</summary>
    [Fact]
    public void Analyze_RecordWithRoutineValue_HasNoEquality()
    {
        string source = """
                        record Button
                            label: Text
                            on_click: Callable[(), None]

                        routine noop()
                            return

                        routine check_it(a: Button) -> Bool
                            return a == a
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Message.Contains(value: "'Button' has no '=='") &&
                         e.Message.Contains(value: "member variable 'on_click'"));
    }

    /// <summary>A record that has no `==` through a member variable deep inside names the whole path down to the member
    /// variable that really has none.</summary>
    [Fact]
    public void Analyze_EqualityMissingDeepInside_NamesThePath()
    {
        string source = """
                        record D
                            f: Callable[(), None]

                        record C
                            d: D

                        record B
                            c: C?

                        record A
                            b: B

                        routine check_it(x: A) -> Bool
                            return x == x
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Message.Contains(value: "'A' has no '=='") &&
                         e.Message.Contains(value: "its member variable 'b' is a 'B', which has none") &&
                         e.Message.Contains(value: "'b.c.d.f' is a 'Callable"));
    }

    /// <summary>A generic `needs T obeys Equatable` takes a plain record, and rejects a record whose member variable
    /// has no `==`.</summary>
    [Fact]
    public void Analyze_EquatableConstraint_TakesEveryTypeWithEquality()
    {
        string source = """
                        record Point
                            x: S64

                        record Button
                            on_click: Callable[(), None]

                        routine noop()
                            return

                        routine same[T](a: T, b: T) -> Bool
                        needs T obeys Equatable
                            return a == b

                        routine first() -> Bool
                            return same(a: Point(x: 1), b: Point(x: 1))

                        routine third() -> Bool
                            return same(a: Button(on_click: noop), b: Button(on_click: noop))
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Message.Contains(value: "'Button' has no '=='") &&
                         e.Message.Contains(value: "member variable 'on_click'"));
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Message.Contains(value: "Point"));
    }

    /// <summary>An entity without `obeys Hashable` is no Set element, and a record whose member variable has no hash
    /// has none.</summary>
    [Fact]
    public void Analyze_EntityWithoutHashable_IsNoSetElement()
    {
        string source = """
                        entity Player
                            name: Text

                        record Button
                            on_click: Callable[(), None]

                        routine make()
                            var players = Set[Player]()
                            var buttons = Set[Button]()
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Message.Contains(value: "'Player' is an entity without 'obeys Hashable'"));
        Assert.Contains(collection: result.Errors,
            filter: e => e.Message.Contains(value: "'Button' has no hash") &&
                         e.Message.Contains(value: "member variable 'on_click'"));
    }

    /// <summary>With `obeys Hashable` an entity is a Set element.</summary>
    [Fact]
    public void Analyze_EntityWithHashable_IsSetElement()
    {
        string source = """
                        entity Player
                        obeys Hashable
                            name: Text

                        routine make()
                            var players = Set[Player]()
                            var ada = Player(name: "Ada")
                            drop players.add(value: steal ada)
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }
}
