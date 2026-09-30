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
    public void Parse_ScriptStatements_SynthesizesStart()
    {
        Program program = ParseSuflae(source: """
                                              show("a")
                                              show("b")
                                              """);

        // No loose top-level statements survive — they went into start().
        Assert.DoesNotContain(collection: program.Declarations, filter: d => d is Statement);

        RoutineDeclaration? start = SynthesizedStart(program: program);
        Assert.NotNull(@object: start);
        BlockStatement body = Assert.IsType<BlockStatement>(@object: start!.Body);
        // Two show()s + a synthesized trailing return.
        Assert.Equal(expected: 2,
            actual: body.Statements
                        .OfType<ExpressionStatement>()
                        .Count());
        Assert.IsType<ReturnStatement>(@object: body.Statements[^1]);
    }

    [Fact]
    public void Parse_ScriptVarDecl_SweptIntoStart()
    {
        Program program = ParseSuflae(source: """
                                              var x = 5
                                              show(x)
                                              """);

        // The top-level `var` must become a start-LOCAL (a DeclarationStatement in the body), not a
        // module-level declaration — otherwise `show(x)` in start could not see it.
        Assert.DoesNotContain(collection: program.Declarations,
            filter: d => d is VariableDeclaration);

        RoutineDeclaration? start = SynthesizedStart(program: program);
        Assert.NotNull(@object: start);
        BlockStatement body = Assert.IsType<BlockStatement>(@object: start!.Body);
        Assert.Contains(collection: body.Statements,
            filter: s => s is DeclarationStatement { Declaration: VariableDeclaration });
        Assert.Contains(collection: body.Statements, filter: s => s is ExpressionStatement);
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

    [Fact]
    public void Analyze_SuflaeScriptVariableInRoutine_SuggestsGlobal()
    {
        AssertHasErrorSuflae(source: """
                                     var total = 0
                                     routine bump(n: Integer)
                                         total = total + n
                                         return
                                     bump(n: 3)
                                     """,
            expectedErrorSubstring: "global total");
    }

    [Fact]
    public void Parse_ScriptGlobal_StaysModuleLevel()
    {
        Program program = ParseSuflae(source: """
                                              global counter: S64 = 0
                                              counter = counter + 1
                                              """);

        // A `global` is module-level state, not a statement: it must stay a sibling declaration so the
        // globals synthesis moves it onto the shared singleton. Swept into start() it would become a
        // start-local that other routines cannot see.
        Assert.Contains(collection: program.Declarations,
            filter: d => d is VariableDeclaration { IsGlobal: true, Name: "counter" });

        RoutineDeclaration? start = SynthesizedStart(program: program);
        Assert.NotNull(@object: start);
        BlockStatement body = Assert.IsType<BlockStatement>(@object: start!.Body);
        Assert.DoesNotContain(collection: body.Statements,
            filter: s => s is DeclarationStatement { Declaration: VariableDeclaration });
    }

    [Fact]
    public void Parse_NormalModule_NoSynthesis()
    {
        // A file with an explicit start and NO loose statements is untouched — its start is the one the
        // user wrote, and no statements are swept.
        Program program = ParseSuflae(source: """
                                              module Test/Mod
                                              routine start()
                                                  show("hi")
                                                  return
                                              """);

        Assert.DoesNotContain(collection: program.Declarations, filter: d => d is Statement);
        RoutineDeclaration? start = SynthesizedStart(program: program);
        Assert.NotNull(@object: start);
        // The body is exactly what the user wrote (a show + a return), not a wrapper.
        BlockStatement body = Assert.IsType<BlockStatement>(@object: start!.Body);
        Assert.Equal(expected: 2, actual: body.Statements.Count);
    }

    [Fact]
    public void Parse_LooseStatementWithExplicitStart_ReportsError()
    {
        (Program _, Builder.Parser.Parser parser) = ParseSuflaeWithErrors(source: """
            show("loose")
            routine start()
                return
            """);

        Assert.True(condition: parser.HasErrors);
        Assert.Contains(collection: parser.GetErrors(),
            filter: e => e.Contains(value: "cannot mix top-level statements"));
    }
}
