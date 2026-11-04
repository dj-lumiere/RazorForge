using System.Runtime.CompilerServices;
using Builder.Tokenizer;
using Builder.Declaration;
using Builder.Verification;
using Builder.Verification.Results;
using SyntaxTree;
using TypeModel.Enums;
using TypeModel.Symbols;
using TypeModel.Types;

namespace Revare.Tests;

/// <summary>
/// Helper memberRoutines for parsing and analyzing test code.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// A time limit for a child process the tests start (a build, a built program), scaled by the
    /// <c>RV_TEST_TIMEOUT_SCALE</c> environment variable (a whole number, 1 when unset). CI sets it on the coverage
    /// leg: the child runs the coverage-instrumented builder assemblies from the test output directory, which makes a
    /// build several times slower. The limit only catches hangs, so a slower run gets a longer one.
    /// </summary>
    public static int ChildTimeoutMs(int baseMs)
    {
        string? scale = Environment.GetEnvironmentVariable(variable: "RV_TEST_TIMEOUT_SCALE");
        return int.TryParse(s: scale, result: out int factor) && factor > 1 ? baseMs * factor : baseMs;
    }

    /// <summary>Registers both front ends' lexers with the builder core before any test runs (the
    /// command lines do this at startup).</summary>
    [ModuleInitializer]
    internal static void RegisterFrontends()
    {
        Revare.RevareFrontend.Register();
    }

    /// <summary>The toolchain that builds <paramref name="sourcePath"/>: <c>Revare.dll</c> from this test
    /// assembly's output directory.</summary>
    public static string ToolchainDll(string sourcePath)
    {
        return Path.Combine(path1: AppContext.BaseDirectory, path2: "Revare.dll");
    }

    private const string ExpectedAtLeastOneError = "Expected at least one error";

    #region Stdlib Snapshots

    // Captured once per process; each AnalyzeSa call restores in microseconds instead of ~0.5 s.
    private static readonly Lazy<TypeRegistry.StdlibSnapshot> _rfSnapshot =
        new(valueFactory: () =>
            SemanticVerifier.CaptureStdlibSnapshot(language: Language.Revare));

    private static readonly Lazy<TypeRegistry.StdlibSnapshot> _mieliaSnapshot =
        new(valueFactory: () => SemanticVerifier.CaptureStdlibSnapshot(language: Language.Mielia));

    #endregion

    #region Revare Helpers

    /// <summary>
    /// Tokenizes Revare source code.
    /// </summary>
    public static List<Token> Tokenize(string source, [CallerMemberName] string? fileName = null)
    {
        return Lexers.Tokenize(source: source,
            fileName: fileName ?? "test",
            language: Language.Revare);
    }

    /// <summary>
    /// Parses Revare source code into an AST.
    /// </summary>
    public static Program Parse(string source, [CallerMemberName] string? fileName = null)
    {
        List<Token> tokens = Tokenize(source: source, fileName: fileName);
        var parser = new Builder.Parser.Parser(tokens: tokens,
            language: Language.Revare,
            fileName: fileName);
        return parser.Parse();
    }

    /// <summary>
    /// Parses Revare source and returns the parser for error checking.
    /// </summary>
    public static (Program Program, Builder.Parser.Parser Parser) ParseWithErrors(string source,
        [CallerMemberName] string? fileName = null)
    {
        List<Token> tokens = Tokenize(source: source, fileName: fileName);
        var parser = new Builder.Parser.Parser(tokens: tokens,
            language: Language.Revare,
            fileName: fileName);
        Program program = parser.Parse();
        return (program, parser);
    }

    /// <summary>
    /// Asserts that parsing produces errors.
    /// </summary>
    public static void AssertParseError(string source, [CallerMemberName] string? fileName = null)
    {
        (Program _, Builder.Parser.Parser parser) =
            ParseWithErrors(source: source, fileName: fileName);
        Assert.True(condition: parser.HasErrors,
            userMessage: "Expected parse errors but none were found");
    }

    /// <summary>
    /// Parses and analyzes Revare source code.
    /// </summary>
    public static AnalysisResult Analyze(string source, [CallerMemberName] string? fileName = null)
    {
        Program program = Parse(source: source, fileName: fileName);
        var analyzer = new SemanticVerifier(language: Language.Revare);
        return analyzer.Analyze(program: program);
    }

    /// <summary>
    /// Asserts that parsing succeeds without errors.
    /// </summary>
    public static Program AssertParses(string source, [CallerMemberName] string? fileName = null)
    {
        (Program program, Builder.Parser.Parser parser) =
            ParseWithErrors(source: source, fileName: fileName);

        if (parser.HasErrors)
        {
            List<string> errors = parser.GetErrors();
            string errorMessages = string.Join(separator: "\n",
                values: errors.Select(selector: e => $"  - {e}"));
            Assert.Fail(
                message: $"Expected no parse errors but got {errors.Count}:\n{errorMessages}");
        }

        Assert.NotNull(@object: program);
        Assert.NotEmpty(collection: program.Declarations);
        return program;
    }

    /// <summary>
    /// Asserts that analysis succeeds without errors.
    /// </summary>
    public static AnalysisResult AssertAnalyzes(string source,
        [CallerMemberName] string? fileName = null)
    {
        AnalysisResult result = Analyze(source: source, fileName: fileName);
        if (result.Errors.Count > 0)
        {
            string errorMessages = string.Join(separator: "\n",
                values: result.Errors.Select(selector: e => $"  - {e.Message} at {e.Location}"));
            Assert.Fail(
                message: $"Expected no errors but got {result.Errors.Count}:\n{errorMessages}");
        }

        return result;
    }

    /// <summary>
    /// Asserts that analysis produces specific errors.
    /// </summary>
    public static AnalysisResult AssertHasError(string source, string expectedErrorSubstring,
        [CallerMemberName] string? fileName = null)
    {
        AnalysisResult result = Analyze(source: source, fileName: fileName);
        Assert.True(condition: result.Errors.Count > 0, userMessage: ExpectedAtLeastOneError);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Message.Contains(value: expectedErrorSubstring,
                comparisonType: StringComparison.OrdinalIgnoreCase));
        return result;
    }

    #endregion

    #region Mielia Helpers

    /// <summary>
    /// Tokenizes Mielia source code.
    /// </summary>
    public static List<Token> TokenizeMielia(string source,
        [CallerMemberName] string? fileName = null)
    {
        return Lexers.Tokenize(source: source,
            fileName: fileName ?? "test",
            language: Language.Mielia);
    }

    /// <summary>
    /// Parses Mielia source code into an AST.
    /// </summary>
    public static Program ParseMielia(string source, [CallerMemberName] string? fileName = null)
    {
        List<Token> tokens = TokenizeMielia(source: source, fileName: fileName);
        var parser = new Builder.Parser.Parser(tokens: tokens,
            language: Language.Mielia,
            fileName: fileName);
        return parser.Parse();
    }

    /// <summary>
    /// Parses Mielia source and returns the parser for error checking.
    /// </summary>
    public static (Program Program, Builder.Parser.Parser Parser) ParseMieliaWithErrors(
        string source, [CallerMemberName] string? fileName = null)
    {
        List<Token> tokens = TokenizeMielia(source: source, fileName: fileName);
        var parser = new Builder.Parser.Parser(tokens: tokens,
            language: Language.Mielia,
            fileName: fileName);
        Program program = parser.Parse();
        return (program, parser);
    }

    /// <summary>
    /// Parses and analyzes Revare source, stopping after Phase 5 (SA only).
    /// Skips monomorphization and lowering — use for tests that only check errors or type annotations.
    /// Uses a pre-analyzed stdlib snapshot so stdlib loading runs once per process, not per test.
    /// </summary>
    public static AnalysisResult AnalyzeSa(string source,
        [CallerMemberName] string? fileName = null)
    {
        Program program = Parse(source: source, fileName: fileName);
        var analyzer = new SemanticVerifier(
            language: Language.Revare,
            snapshot: _rfSnapshot.Value) { SaOnly = true };
        return analyzer.Analyze(program: program);
    }

    /// <summary>
    /// SA-only analysis that also hands back the analyzed program, for tests that look at the lowered body.
    /// </summary>
    public static (AnalysisResult Result, Program Program) AnalyzeSaProgram(string source,
        [CallerMemberName] string? fileName = null)
    {
        Program program = Parse(source: source, fileName: fileName);
        var analyzer = new SemanticVerifier(
            language: Language.Revare,
            snapshot: _rfSnapshot.Value) { SaOnly = true };
        return (analyzer.Analyze(program: program), program);
    }

    /// <summary>
    /// Asserts that SA-only analysis succeeds without errors.
    /// </summary>
    public static AnalysisResult AssertAnalyzesSa(string source,
        [CallerMemberName] string? fileName = null)
    {
        AnalysisResult result = AnalyzeSa(source: source, fileName: fileName);
        if (result.Errors.Count > 0)
        {
            string errorMessages = string.Join(separator: "\n",
                values: result.Errors.Select(selector: e => $"  - {e.Message} at {e.Location}"));
            Assert.Fail(
                message: $"Expected no errors but got {result.Errors.Count}:\n{errorMessages}");
        }

        return result;
    }

    /// <summary>
    /// Asserts that SA-only analysis produces the expected error.
    /// </summary>
    public static AnalysisResult AssertHasErrorSa(string source, string expectedErrorSubstring,
        [CallerMemberName] string? fileName = null)
    {
        AnalysisResult result = AnalyzeSa(source: source, fileName: fileName);
        Assert.True(condition: result.Errors.Count > 0, userMessage: ExpectedAtLeastOneError);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Message.Contains(value: expectedErrorSubstring,
                comparisonType: StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>
    /// Parses and analyzes Mielia source code.
    /// </summary>
    public static AnalysisResult AnalyzeMielia(string source,
        [CallerMemberName] string? fileName = null)
    {
        Program program = ParseMielia(source: source, fileName: fileName);
        var analyzer = new SemanticVerifier(language: Language.Mielia);
        return analyzer.Analyze(program: program);
    }

    /// <summary>
    /// Asserts that Mielia parsing succeeds without errors.
    /// </summary>
    public static Program AssertParsesMielia(string source,
        [CallerMemberName] string? fileName = null)
    {
        (Program program, Builder.Parser.Parser parser) =
            ParseMieliaWithErrors(source: source, fileName: fileName);

        if (parser.HasErrors)
        {
            List<string> errors = parser.GetErrors();
            string errorMessages = string.Join(separator: "\n",
                values: errors.Select(selector: e => $"  - {e}"));
            Assert.Fail(
                message: $"Expected no parse errors but got {errors.Count}:\n{errorMessages}");
        }

        Assert.NotNull(@object: program);
        Assert.NotEmpty(collection: program.Declarations);
        return program;
    }

    /// <summary>
    /// Asserts that Mielia analysis succeeds without errors.
    /// </summary>
    public static AnalysisResult AssertAnalyzesMielia(string source,
        [CallerMemberName] string? fileName = null)
    {
        AnalysisResult result = AnalyzeMielia(source: source, fileName: fileName);
        if (result.Errors.Count > 0)
        {
            string errorMessages = string.Join(separator: "\n",
                values: result.Errors.Select(selector: e => $"  - {e.Message} at {e.Location}"));
            Assert.Fail(
                message: $"Expected no errors but got {result.Errors.Count}:\n{errorMessages}");
        }

        return result;
    }

    /// <summary>
    /// Asserts that Mielia analysis produces specific errors.
    /// </summary>
    public static AnalysisResult AssertHasErrorMielia(string source, string expectedErrorSubstring,
        [CallerMemberName] string? fileName = null)
    {
        AnalysisResult result = AnalyzeMielia(source: source, fileName: fileName);
        Assert.True(condition: result.Errors.Count > 0, userMessage: ExpectedAtLeastOneError);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Message.Contains(value: expectedErrorSubstring,
                comparisonType: StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>
    /// Parses and analyzes Mielia source, stopping after Phase 5 (SA only).
    /// Uses a pre-analyzed stdlib snapshot so stdlib loading runs once per process, not per test.
    /// </summary>
    public static AnalysisResult AnalyzeSaMielia(string source,
        [CallerMemberName] string? fileName = null)
    {
        Program program = ParseMielia(source: source, fileName: fileName);
        var analyzer = new SemanticVerifier(
            language: Language.Mielia,
            snapshot: _mieliaSnapshot.Value) { SaOnly = true };
        return analyzer.Analyze(program: program);
    }

    /// <summary>
    /// Asserts that Mielia SA-only analysis succeeds without errors.
    /// </summary>
    public static AnalysisResult AssertAnalyzesSaMielia(string source,
        [CallerMemberName] string? fileName = null)
    {
        AnalysisResult result = AnalyzeSaMielia(source: source, fileName: fileName);
        if (result.Errors.Count > 0)
        {
            string errorMessages = string.Join(separator: "\n",
                values: result.Errors.Select(selector: e => $"  - {e.Message} at {e.Location}"));
            Assert.Fail(
                message: $"Expected no errors but got {result.Errors.Count}:\n{errorMessages}");
        }

        return result;
    }

    /// <summary>
    /// Asserts that Mielia SA-only analysis produces the expected error.
    /// </summary>
    public static AnalysisResult AssertHasErrorSaMielia(string source,
        string expectedErrorSubstring, [CallerMemberName] string? fileName = null)
    {
        AnalysisResult result = AnalyzeSaMielia(source: source, fileName: fileName);
        Assert.True(condition: result.Errors.Count > 0, userMessage: ExpectedAtLeastOneError);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Message.Contains(value: expectedErrorSubstring,
                comparisonType: StringComparison.OrdinalIgnoreCase));
        return result;
    }

    #endregion

    #region Common Helpers

    /// <summary>
    /// Gets a declaration of a specific type from the program.
    /// </summary>
    public static T GetDeclaration<T>(Program program) where T : ISyntaxTreeNode
    {
        T? decl = program.Declarations
                         .OfType<T>()
                         .FirstOrDefault();
        Assert.NotNull(@object: decl);
        return decl;
    }

    /// <summary>
    /// Gets all declarations of a specific type from the program.
    /// </summary>
    public static List<T> GetDeclarations<T>(Program program) where T : ISyntaxTreeNode
    {
        return program.Declarations
                      .OfType<T>()
                      .ToList();
    }

    #endregion
}

/// <summary>
/// Extension memberRoutines for TypeRegistry to make tests more readable.
/// </summary>
public static class TypeRegistryExtensions
{
    /// <summary>
    /// Gets a type by name (wrapper for LookupType).
    /// </summary>
    public static TypeSymbol? GetType(this TypeRegistry registry, string name)
    {
        return registry.LookupType(name: name);
    }

    /// <summary>
    /// Gets a routine by name (wrapper for LookupRoutine).
    /// </summary>
    public static RoutineInfo? GetRoutine(this TypeRegistry registry, string name)
    {
        return registry.LookupRoutine(fullName: name);
    }
}
