using Builder.Diagnostics;
using Builder.Verification.Results;
using SyntaxTree;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Script mode (RazorForge and Suflae): a file with loose top-level STATEMENTS needs no explicit entry point —
/// the parser folds those statements (and any top-level runtime <c>var</c> declarations) into an implicit
/// <c>routine start()</c>, in source order. These lock the AST transform: synthesis, var-sweeping, the
/// no-op on a normal module file, and the explicit-start conflict.
/// </summary>
public sealed class ScriptModeTests
{
    private static RoutineDeclaration? SynthesizedStart(Program program)
    {
        return program.Declarations
                      .OfType<RoutineDeclaration>()
                      .FirstOrDefault(predicate: r => r.Name == "start");
    }

    [Fact]
    public void Parse_RazorForgeScript_SynthesizesMarkedStart()
    {
        Program program = Parse(source: """
                                        import IO/Console
                                        var x = 5
                                        show(f"{x}")
                                        """);

        Assert.DoesNotContain(collection: program.Declarations, filter: d => d is Statement);
        RoutineDeclaration? start = SynthesizedStart(program: program);
        Assert.NotNull(@object: start);
        Assert.True(condition: start!.IsScriptEntry);
        Assert.Equal(expected: ["x"], actual: start.ScriptVariableNames!);
    }

    [Fact]
    public void Parse_RazorForgeLooseStatementWithExplicitStart_ReportsError()
    {
        (Program _, Builder.Parser.Parser parser) = ParseWithErrors(source: """
            show("loose")
            routine start()
                return
            """);

        Assert.True(condition: parser.HasErrors);
        Assert.Contains(collection: parser.GetErrors(),
            filter: e => e.Contains(value: "cannot mix top-level statements"));
    }

    [Fact]
    public void Analyze_RazorForgeScriptVariableInRoutine_ExplainsScope()
    {
        AnalysisResult result = AssertHasError(source: """
                                                      var total = 0
                                                      routine bump(n: S64)
                                                          total = total + n
                                                          return
                                                      bump(n: 3)
                                                      """,
            expectedErrorSubstring: "RazorForge has no module-level mutable state");

        // Reported once for `total = total + n`, not once per side.
        Assert.Single(collection: result.Errors,
            predicate: e => e.Code == SemanticDiagnosticCode.ScriptVariableNotVisibleInRoutine);
    }

}
