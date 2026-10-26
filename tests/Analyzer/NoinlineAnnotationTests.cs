using Builder.Diagnostics;
using Builder.LlvmEmit;
using Builder.Targeting;
using Builder.Verification;
using Builder.Verification.Results;
using SyntaxTree;
using TypeModel.Enums;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// <c>@noinline</c> keeps a routine out of its callers: the LLVM emitter writes <c>noinline</c> on its definition and
/// no <c>optnone</c>, so its own body is still optimized. Together with <c>@inline</c> it is RF-G227.
/// </summary>
public sealed class NoinlineAnnotationTests
{
    [Fact]
    public void Parse_Noinline_IsKept()
    {
        Program program = AssertParses(source: """
                                               @noinline
                                               routine cold(x: S64) -> S64
                                                   return x * 3
                                               """);
        RoutineDeclaration routine = GetDeclaration<RoutineDeclaration>(program: program);
        Assert.Contains(expected: "noinline", collection: routine.Annotations);
    }

    [Theory]
    [InlineData("@inline\n@noinline\n")]
    [InlineData("@noinline\n@inline\n")]
    [InlineData("@[inline, noinline]\n")]
    public void Parse_InlineAndNoinline_IsG227(string annotations)
    {
        (_, Builder.Parser.Parser parser) = ParseWithErrors(source: annotations +
                                                                    "routine cold(x: S64) -> S64\n    return x * 3\n");
        Assert.Single(collection: parser.GetStructuredErrors()
                                        .Where(predicate: e => e.Code == GrammarDiagnosticCode.InlineAndNoinline));
        Assert.Contains(collection: parser.GetErrors(), filter: e => e.Contains(value: "RF-G227"));
    }

    [Fact]
    public void Emit_Noinline_WritesNoinlineWithoutOptnone()
    {
        string ir = GenerateIr(source: """
                                       @noinline
                                       routine cold(x: S64) -> S64
                                           return x * 3

                                       @no_optimize
                                       routine frozen(x: S64) -> S64
                                           return x * 5

                                       routine start()
                                           var a = cold(x: 4)
                                           var b = frozen(x: a)
                                           return
                                       """);
        string cold = DefineLine(ir: ir, routine: "cold(");
        Assert.Contains(expectedSubstring: " noinline", actualString: cold);
        Assert.DoesNotContain(expectedSubstring: "optnone", actualString: cold);
        Assert.DoesNotContain(expectedSubstring: "alwaysinline", actualString: cold);
        Assert.Contains(expectedSubstring: "noinline optnone", actualString: DefineLine(ir: ir, routine: "frozen("));
    }

    private static string DefineLine(string ir, string routine)
    {
        return ir.Split(separator: '\n')
                 .Single(predicate: l => l.StartsWith(value: "define", comparisonType: StringComparison.Ordinal) &&
                                         l.Contains(value: routine, comparisonType: StringComparison.Ordinal));
    }

    private static string GenerateIr(string source)
    {
        Program program = Parse(source: source);
        var analyzer = new SemanticVerifier(language: Language.RazorForge, buildMode: RfBuildMode.Debug);
        AnalysisResult result = analyzer.Analyze(program: program);
        Assert.Empty(collection: result.Errors);
        var generator = new LlvmEmitter(program: program,
            registry: result.Registry,
            options: new LlvmEmitterOptions
            {
                StdlibPrograms = result.Registry.StdlibPrograms,
                BuildMode = RfBuildMode.Debug,
                SynthesizedBodies = result.SynthesizedBodies,
                InstantiatedGenericBodies = result.InstantiatedGenericBodies,
                LiveRoutineKeys = result.LiveRoutineKeys,
                MaySuspendRoutineKeys = result.MaySuspendRoutineKeys
            });
        return generator.Generate();
    }
}
