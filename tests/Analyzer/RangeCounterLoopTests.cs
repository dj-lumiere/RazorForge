using Builder.Verification.Results;
using SyntaxTree;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// An `each` over a range literal of an integer type counts with a local instead of walking a range emitter, so the
/// lowered body asks no emitter for values. A range over another type, and a range kept in a variable, still walk
/// one. What the loops yield is checked by tests/Fixtures/Stdlib/range_counter_api.rf.
/// </summary>
public sealed class RangeCounterLoopTests
{
    [Theory]
    [InlineData("0 til n")]
    [InlineData("0 to n")]
    [InlineData("n to 0 by -1")]
    [InlineData("0 til n by step")]
    public void IntegerRangeLiteral_Counts(string range)
    {
        RoutineDeclaration walk = AnalyzedWalk(range: range);
        Assert.DoesNotContain(collection: CalledMembers(body: walk.Body), expected: "emitter");
        Assert.DoesNotContain(collection: CalledMembers(body: walk.Body), expected: "emit");
    }

    [Theory]
    [InlineData("0.0 to 1.0 by 0.25")]
    public void FloatRangeLiteral_WalksEmitter(string range)
    {
        RoutineDeclaration walk = AnalyzedWalk(range: range);
        Assert.Contains(collection: CalledMembers(body: walk.Body), expected: "emitter");
    }

    [Fact]
    public void RangeInVariable_WalksEmitter()
    {
        string source = """
                        routine walk(n: S64)
                            var kept = 0 til n
                            each i in kept
                                nothing
                            return
                        """;
        (AnalysisResult result, Program program) = AnalyzeSaProgram(source: source);
        Assert.Empty(collection: result.Errors);
        Assert.Contains(collection: CalledMembers(body: GetDeclaration<RoutineDeclaration>(program: program).Body),
            expected: "emitter");
    }

    private static RoutineDeclaration AnalyzedWalk(string range)
    {
        string source = "routine walk(n: S64, step: S64)\n" +
                        $"    each i in {range}\n" +
                        "        nothing\n" +
                        "    return\n";
        (AnalysisResult result, Program program) = AnalyzeSaProgram(source: source);
        Assert.Empty(collection: result.Errors);
        return GetDeclaration<RoutineDeclaration>(program: program);
    }

    private static List<string> CalledMembers(Statement body)
    {
        var names = new List<string>();
        AstWalker.WalkExpressions(root: body, visit: e =>
        {
            if (e is CallExpression { Callee: MemberExpression member })
            {
                names.Add(item: member.MemberName);
            }
        });
        return names;
    }
}
