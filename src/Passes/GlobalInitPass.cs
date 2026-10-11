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
        MakeConstructedPresetsGlobal(orderedFiles: orderedFiles);

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

    /// <summary>
    /// A preset made by a construction (<c>preset BOX: Box = Box(n: 5)</c>, or the construction a computed preset came
    /// to) is one value, made once: it becomes a read-only global, so every use is the same object (an entity has one
    /// identity, <c>BOX === BOX</c>) instead of a new one built where it is used.
    /// </summary>
    private static void MakeConstructedPresetsGlobal(List<(SyntaxTree.Program Program, string FilePath)> orderedFiles)
    {
        foreach ((SyntaxTree.Program program, string _) in orderedFiles)
        {
            for (int i = 0; i < program.Declarations.Count; i++)
            {
                if (program.Declarations[index: i] is PresetDeclaration
                    {
                        Value: CallExpression { Callee: IdentifierExpression { Name: [var first, ..] } }
                    } preset && char.IsUpper(c: first))
                {
                    program.Declarations[index: i] = new VariableDeclaration(Name: preset.Name,
                        Type: preset.Type,
                        Initializer: preset.Value,
                        Visibility: preset.IsSecret
                            ? VisibilityModifier.Secret
                            : VisibilityModifier.Open,
                        Location: preset.Location,
                        IsGlobal: true);
                }
            }
        }
    }
}
