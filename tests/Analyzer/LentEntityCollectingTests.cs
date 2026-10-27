using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Tests for RF-S413 at a terminal that would keep entities a walk only reaches where their container keeps them
/// (<c>xs.where(...).to[List]()</c> over a <c>List[Crate]</c>): the new collection, or the one entity given back,
/// would keep each entity in a second place. A walk that hands its items over (<c>select</c> making new entities,
/// <c>(steal xs).where(...)</c> taking the list apart) may be collected.
/// </summary>
public class LentEntityCollectingTests
{
    private const string Prelude = """
                                   import IO/Console
                                   import IterTools

                                   entity Crate
                                       n: S64

                                   routine start()
                                       var xs: List[Crate] = [Crate(n: 1), Crate(n: 2), Crate(n: 3)]

                                   """;

    private static bool IsKeptEntityError(SemanticError e)
    {
        return e.Code == SemanticDiagnosticCode.BareEntityAssignment;
    }

    private static IEnumerable<SemanticError> ErrorsFor(string body)
    {
        return AnalyzeSa(source: Prelude + body).Errors;
    }

    [Theory]
    [InlineData("    var big = xs.where(c => c.n > 1).to[List]()\n    show(big.count())\n    return\n")]
    [InlineData("    var back = xs.reverse().to[List]()\n    show(back.count())\n    return\n")]
    [InlineData("    var top = xs.where(c => c.n > 1).first()\n    show(top.n)\n    return\n")]
    [InlineData("    var pairs = xs.enumerate().to[List]()\n    show(pairs.count())\n    return\n")]
    public void Analyze_CollectingLentEntities_Errors(string body)
    {
        Assert.Contains(filter: IsKeptEntityError, collection: ErrorsFor(body: body));
    }

    [Fact]
    public void Analyze_CollectingLentEntities_PointsToTheFixes()
    {
        SemanticError error = Assert.Single(collection: ErrorsFor(body: """
                                                                    var big = xs.where(c => c.n > 1).to[List]()
                                                                    show(big.count())
                                                                    return
                                                                """).Where(predicate: IsKeptEntityError));
        Assert.Contains(expectedSubstring: "'.select(e => e.duplicate())'", actualString: error.Message);
        Assert.Contains(expectedSubstring: "'(steal xs).where(...)'", actualString: error.Message);
    }

    [Theory]
    [InlineData("    var made = xs.select(transform: c => Crate(n: c.n * 10)).to[List]()\n    show(made.count())\n    return\n")]
    [InlineData("    var big = (steal xs).where(c => c.n > 1).to[List]()\n    show(big.count())\n    return\n")]
    [InlineData("    var sizes = xs.select(transform: c => c.n).to[List]()\n    show(sizes.count())\n    return\n")]
    public void Analyze_CollectingHandedOverItems_IsAllowed(string body)
    {
        Assert.DoesNotContain(filter: IsKeptEntityError, collection: ErrorsFor(body: body));
    }
}
