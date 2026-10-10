using Builder.Desugaring;
using Builder.Diagnostics;
using SyntaxTree;

namespace RazorForge.Passes;

/// <summary>
/// RazorForge <c>global</c> initialization, run on the user programs at the start of semantic analysis. Each global
/// keeps its own module-level cell (its type is one threads may share, checked in analysis), and its initializer
/// becomes an assignment at the top of <c>start()</c>, in the order <see cref="ModuleGlobalOrder"/> gives. The
/// assignments are marked <see cref="AssignmentStatement.IsGlobalInit"/>, so each global is torn down at every exit of
/// <c>start()</c>: its lifetime is the program's.
/// </summary>
internal static class GlobalInitPass
{
    /// <summary>Returns false after reporting RF-S436 / RF-S438 when the globals cannot be initialized.</summary>
    internal static bool Run(List<(SyntaxTree.Program Program, string FilePath)> orderedFiles,
        Action<SemanticDiagnosticCode, string, SourceLocation> report)
    {
        if (!ModuleGlobalOrder.TryOrder(orderedFiles: orderedFiles, report: report,
                out List<(string Name, TypeExpression Type, Expression Init, SourceLocation Loc)> globals,
                out List<HashSet<int>> _, out List<int> order))
        {
            return false;
        }

        if (globals.Count == 0)
        {
            return true;
        }

        if (ModuleGlobalOrder.FindStart(orderedFiles: orderedFiles, report: report, at: globals[index: 0].Loc) is not
            (_, { } startBlock))
        {
            return false;
        }

        var inits = order.Select(selector: i => (Statement)new AssignmentStatement(
                              Target: new IdentifierExpression(Name: globals[index: i].Name,
                                  Location: globals[index: i].Loc),
                              Value: globals[index: i].Init,
                              Location: globals[index: i].Loc) { IsGlobalInit = true })
                         .ToList();
        startBlock.Statements.InsertRange(index: 0, collection: inits);
        return true;
    }
}
