using Builder.Diagnostics;
using SyntaxTree;

namespace Revare.Tests.Parser;

using static TestHelpers;

/// <summary>
/// Loops named with <c>as</c> and the <c>break to</c> / <c>continue to</c> that name them: the parsed shape, the build
/// error for a name no loop has, and the warning for a name nothing uses.
/// </summary>
public class NamedLoopTests
{
    [Fact]
    public void ForLoopTakesAName_AndJumpsNameIt()
    {
        Program program = AssertParses(source: """
                                               routine test()
                                                   for y in 0 til 3 as rows
                                                       for x in 0 til 3
                                                           if x == y
                                                               break to rows
                                                           continue to rows
                                                   return
                                               """);
        RoutineDeclaration routine = GetDeclaration<RoutineDeclaration>(program: program);
        ForStatement outer = ((BlockStatement)routine.Body).Statements.OfType<ForStatement>().Single();
        Assert.Equal(expected: "rows", actual: outer.Name);
        Assert.Contains(collection: AllStatements(root: outer.Body), filter: s => s is BreakStatement { TargetName: "rows" });
        Assert.Contains(collection: AllStatements(root: outer.Body), filter: s => s is ContinueStatement { TargetName: "rows" });
    }

    [Fact]
    public void UntilAndLoopTakeAName()
    {
        Program program = AssertParses(source: """
                                               routine test()
                                                   var n = 0
                                                   until n > 3 as counting
                                                       n += 1
                                                       break to counting
                                                   loop as forever
                                                       break to forever
                                                   return
                                               """);
        RoutineDeclaration routine = GetDeclaration<RoutineDeclaration>(program: program);
        IEnumerable<Statement> all = AllStatements(root: routine.Body);
        Assert.Contains(collection: all, filter: s => s is WhileStatement { Name: "counting" });
        Assert.Contains(collection: all, filter: s => s is LoopStatement { Name: "forever" });
    }

    [Fact]
    public void JumpToAnUnknownName_IsABuildError()
    {
        (_, Builder.Parser.Parser parser) = ParseWithErrors(source: """
                                                                  routine test()
                                                                      for y in 0 til 3 as rows
                                                                          break to columns
                                                                      return
                                                                  """);
        GrammarException error = Assert.Single(collection: parser.GetStructuredErrors());
        Assert.Equal(expected: GrammarDiagnosticCode.UnknownLoopName, actual: error.Code);
        Assert.Contains(expectedSubstring: "'break to columns' names no loop around it", actualString: error.Message);
    }

    [Fact]
    public void ANameNothingUses_IsAWarning()
    {
        (_, Builder.Parser.Parser parser) = ParseWithErrors(source: """
                                                                  routine test()
                                                                      for y in 0 til 3 as rows
                                                                          show(y)
                                                                      return
                                                                  """);
        Builder.Parser.BuildWarning warning = Assert.Single(collection: parser.GetWarnings());
        Assert.Equal(expected: "RA-W010", actual: warning.WarningCode);
        Assert.Contains(expectedSubstring: "'rows'", actualString: warning.Message);
    }

    [Fact]
    public void ANameAnEnclosingLoopHas_IsABuildError()
    {
        (_, Builder.Parser.Parser parser) = ParseWithErrors(source: """
                                                                  routine test()
                                                                      for a in 0 til 2 as rows
                                                                          for b in 0 til 2 as rows
                                                                              break to rows
                                                                      return
                                                                  """);
        Assert.Contains(collection: parser.GetStructuredErrors(),
            filter: e => e.Code == GrammarDiagnosticCode.UnknownLoopName && e.Message.Contains(value: "already named"));
    }

    private static IEnumerable<Statement> AllStatements(Statement root)
    {
        yield return root;
        IEnumerable<Statement> children = root switch
        {
            BlockStatement b => b.Statements,
            ForStatement f => [f.Body, ..(f.ThenBranch is { } t ? new[] { t } : [])],
            WhileStatement w => [w.Body],
            LoopStatement l => [l.Body],
            IfStatement i => i.ElseStatement == null ? [i.ThenStatement] : [i.ThenStatement, i.ElseStatement],
            _ => []
        };
        foreach (Statement child in children)
        {
            foreach (Statement nested in AllStatements(root: child))
            {
                yield return nested;
            }
        }
    }
}
