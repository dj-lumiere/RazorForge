using System.Text.RegularExpressions;
using Builder.Tokenizer;
using SyntaxTree;
using TypeModel.Enums;
using Builder.Verification;
using Builder.Verification.Results;
using Xunit.Abstractions;

namespace RazorForge.Tests.Perf;

/// <summary>
/// WARM/COLD PARITY over the real stdlib fixtures — the systematic guard against the recurring
/// "warm and cold emit different routine define-sets" divergence. A COLD compile (fresh full-stdlib
/// SA) and a WARM compile (restored captured stdlib + the same source) must emit the IDENTICAL set of
/// LLVM <c>define</c> headers for every fixture. Any difference is a restore-fidelity / demand-seeding
/// non-determinism bug — the exact class the pull collector must never reintroduce.
///
/// <para>The curated <see cref="RiskyFixtures"/> theory runs in CI (fast subset covering errors/throws,
/// entities, iterators, generics, locks, choices, variants, routine values).</para>
/// </summary>
public sealed partial class WarmColdFixtureParityTests
{
    private readonly ITestOutputHelper _out;
    public WarmColdFixtureParityTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static readonly string FixtureDir = Path.Combine(path1: LocateRepoRoot(),
        path2: "tests",
        path3: "Fixtures",
        path4: "Stdlib");

    private static string LocateRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(value: dir))
        {
            if (File.Exists(path: Path.Combine(path1: dir, path2: "RazorForge.csproj")))
            {
                return dir;
            }

            string? parent = Path.GetDirectoryName(path: dir);
            if (parent == null || parent == dir)
            {
                break;
            }

            dir = parent;
        }

        throw new InvalidOperationException(message: "Could not locate RazorForge.csproj.");
    }

    // Captured ONCE (the daemon's resident stdlib) and shared across all cases in this class.
    private static readonly SemanticVerifier.CompiledStdlibState Warm =
        SemanticVerifier.CaptureCompiledStdlib(language: Language.RazorForge);

    /// <summary>Risky-surface subset — the feature areas where demand-seeding has historically drifted
    /// between warm and cold (throw→crash_message/represent, entity self-free, iterator adapters, lock
    /// policies, choice/variant formatters, routine-value teardown).</summary>
    public static TheoryData<string, string> RiskyFixtures()
    {
        // Parity-clean fixtures — ENFORCED. Adding a fixture here locks its warm≡cold define-set forever.
        string[] names =
        [
            "error_paths_api", "crashable_api", "list_api", "dict_api", "set_api",
            "sorted_dict_api", "choice_api", "variant_api", "generic_routine_api",
            "guarded_api", "guarded_access_api", "fallible_lock_api", "work_api",
            "array_api", "range_api", "filesystem_api", "itertools_api", "builder_query_api",
            "bytes_methods_api", "cstr_decode_api", "ffi_extern_api"
        ];
        var data = new TheoryData<string, string>();
        foreach (string n in names)
        {
            string p = Path.Combine(path1: FixtureDir, path2: n + ".rf");
            if (File.Exists(path: p))
            {
                data.Add(p1: n, p2: p);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(memberName: nameof(RiskyFixtures))]
    public void RiskyFixture_WarmMatchesCold(string name, string path)
    {
        (int cold, int warm, string[] coldOnly, string[] warmOnly) = CompareDefines(path: path);
        _out.WriteLine(message: $"{name}: coldDefs={cold} warmDefs={warm}");
        if (coldOnly.Length > 0)
        {
            _out.WriteLine(
                message:
                $"  cold-only: {string.Join(separator: " | ", values: coldOnly.Take(count: 10))}");
        }

        if (warmOnly.Length > 0)
        {
            _out.WriteLine(
                message:
                $"  warm-only: {string.Join(separator: " | ", values: warmOnly.Take(count: 10))}");
        }

        Assert.True(condition: coldOnly.Length == 0 && warmOnly.Length == 0,
            userMessage:
            $"{name}: warm/cold define-set diverged — cold-only=[{string.Join(separator: ", ", value: coldOnly)}] " +
            $"warm-only=[{string.Join(separator: ", ", value: warmOnly)}]");
    }

    /// <summary>The daemon's second build of a program reuses the stdlib files the first one analyzed
    /// (<see cref="SemanticVerifier.CompiledStdlibState.AnalyzedFileCache"/>). That cache-hit build must still
    /// clone protocol defaults from analyzed bodies: `Iterable[T].List` reached through `SelectManyIterable` came
    /// out of the lowered cached program, its `each` already a `when` nobody had typed, and the LLVM emitter
    /// rejected the leftover `when`.</summary>
    [Theory]
    [InlineData("itertools_more_api")]
    [InlineData("itertools_api")]
    public void CacheHitBuild_MatchesCold(string name)
    {
        string path = Path.Combine(path1: FixtureDir, path2: name + ".rf");
        string src = File.ReadAllText(path: path);
        SemanticVerifier.CompiledStdlibState warm =
            SemanticVerifier.CaptureCompiledStdlib(language: Language.RazorForge);
        HashSet<string> coldDefs = DefineSet(ll: Codegen(
            r: new SemanticVerifier(language: Language.RazorForge).Analyze(
                program: Parse(src: src, path: path))));
        for (int build = 1; build <= 2; build++)
        {
            HashSet<string> warmDefs = DefineSet(ll: Codegen(
                r: new SemanticVerifier(language: Language.RazorForge, warm: warm).Analyze(
                    program: Parse(src: src, path: path))));
            string[] coldOnly = coldDefs.Except(second: warmDefs).OrderBy(keySelector: s => s).ToArray();
            string[] warmOnly = warmDefs.Except(second: coldDefs).OrderBy(keySelector: s => s).ToArray();
            Assert.True(condition: coldOnly.Length == 0 && warmOnly.Length == 0,
                userMessage:
                $"{name} warm build {build}: define-set diverged from cold, cold-only=[{string.Join(separator: ", ", values: coldOnly.Take(count: 6))}] " +
                $"warm-only=[{string.Join(separator: ", ", values: warmOnly.Take(count: 6))}]");
        }

        Assert.NotEmpty(collection: warm.AnalyzedFileCache);
    }

    /// <summary>A warm build that reuses a library file an EARLIER build of another program analyzed. That analysis
    /// made the recovery variants the file's own code reaches (`check w.retrieve()` inside the Work library) after
    /// the build's variant teardown had run, so they were cached without any scope-exit destroys, and the next
    /// build took them as lowered: `WorkState[S64].retrieve`'s check variant tore down none of its locals and the
    /// lifecycle hooks of the `Check[U32]` it holds were never reached.</summary>
    [Theory]
    [InlineData("work_api", "work_start_api")]
    public void BuildAfterAnotherProgram_MatchesCold(string first, string second)
    {
        string path = Path.Combine(path1: FixtureDir, path2: second + ".rf");
        string src = File.ReadAllText(path: path);
        HashSet<string> coldDefs = DefineSet(ll: Codegen(
            r: new SemanticVerifier(language: Language.RazorForge).Analyze(
                program: Parse(src: src, path: path))));

        SemanticVerifier.CompiledStdlibState warm =
            SemanticVerifier.CaptureCompiledStdlib(language: Language.RazorForge);
        string firstPath = Path.Combine(path1: FixtureDir, path2: first + ".rf");
        Codegen(r: new SemanticVerifier(language: Language.RazorForge, warm: warm).Analyze(
            program: Parse(src: File.ReadAllText(path: firstPath), path: firstPath)));
        HashSet<string> warmDefs = DefineSet(ll: Codegen(
            r: new SemanticVerifier(language: Language.RazorForge, warm: warm).Analyze(
                program: Parse(src: src, path: path))));

        string[] coldOnly = coldDefs.Except(second: warmDefs).OrderBy(keySelector: s => s).ToArray();
        string[] warmOnly = warmDefs.Except(second: coldDefs).OrderBy(keySelector: s => s).ToArray();
        Assert.True(condition: coldOnly.Length == 0 && warmOnly.Length == 0,
            userMessage:
            $"{second} after {first}: define-set diverged from cold, cold-only=[{string.Join(separator: ", ", values: coldOnly.Take(count: 6))}] " +
            $"warm-only=[{string.Join(separator: ", ", values: warmOnly.Take(count: 6))}]");
    }

    private static (int cold, int warm, string[] coldOnly, string[] warmOnly) CompareDefines(
        string path)
    {
        string src = File.ReadAllText(path: path);
        HashSet<string> coldDefs = DefineSet(ll: Codegen(
            r: new SemanticVerifier(language: Language.RazorForge).Analyze(
                program: Parse(src: src, path: path))));
        HashSet<string> warmDefs = DefineSet(ll: Codegen(
            r: new SemanticVerifier(language: Language.RazorForge, warm: Warm).Analyze(
                program: Parse(src: src, path: path))));
        return (coldDefs.Count, warmDefs.Count, coldDefs.Except(second: warmDefs)
                                                        .OrderBy(keySelector: s => s)
                                                        .ToArray(), warmDefs
           .Except(second: coldDefs)
           .OrderBy(keySelector: s => s)
           .ToArray());
    }

    private static Program Parse(string src, string path)
    {
        List<Token> tokens =
            Lexers.Tokenize(source: src, fileName: path, language: Language.RazorForge);
        return new Builder.Parser.Parser(tokens: tokens,
            language: Language.RazorForge,
            fileName: path).Parse();
    }

    private static string Codegen(AnalysisResult r)
    {
        Builder.Lowering.Passes.CancellationInstrumentationPass.Run(
            programs: r.Registry.UserPrograms,
            instantiatedBodies: r.InstantiatedGenericBodies,
            maySuspendKeys: r.MaySuspendRoutineKeys);
        var gen = new Builder.LlvmEmit.LlvmEmitter(userPrograms: r.Registry.UserPrograms,
            registry: r.Registry,
            options: new Builder.LlvmEmit.LlvmEmitterOptions
            {
                StdlibPrograms = r.Registry.StdlibPrograms,
                SynthesizedBodies = r.SynthesizedBodies,
                InstantiatedGenericBodies = r.InstantiatedGenericBodies,
                LiveRoutineKeys = r.LiveRoutineKeys,
                MaySuspendRoutineKeys = r.MaySuspendRoutineKeys
            });
        return gen.Generate();
    }

    [GeneratedRegex(pattern: @" !dbg ![0-9]+")]
    private static partial Regex DbgAnnotationRegex();

    private static HashSet<string> DefineSet(string ll)
    {
        return ll.Split(separator: '\n')
                 .Where(predicate: l =>
                      l.StartsWith(value: "define ", comparisonType: StringComparison.Ordinal))
                 .Select(selector: l => DbgAnnotationRegex()
                     .Replace(input: l.Split(separator: " {", count: 2)[0], replacement: ""))
                 .ToHashSet(comparer: StringComparer.Ordinal);
    }
}
