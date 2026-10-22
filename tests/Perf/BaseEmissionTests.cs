using Builder.Instantiation;
using Builder.LlvmEmit;
using Builder.Tokenizer;
using SyntaxTree;
using TypeModel.Enums;
using Builder.Verification;
using Builder.Verification.Results;
using Xunit.Abstractions;

namespace RazorForge.Tests.Perf;

/// <summary>
/// Resident-JIT incremental Phase 0a (see internal-wiki/RESIDENT-JIT-INCREMENTAL-V0.5.md §2A.5): validates
/// <see cref="LlvmEmitter.GenerateBase"/> — the NON-PRUNED precompiled stdlib base. The load-bearing
/// correctness property is that the base is a SUPERSET of any pruned build's stdlib defines: a delta module
/// only ever emits an extern <c>declare</c> for a symbol it doesn't define, so every such symbol MUST be
/// present in the base or JIT-link fails with an undefined symbol.
/// </summary>
public sealed class BaseEmissionTests
{
    private readonly ITestOutputHelper _out;
    public BaseEmissionTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private const string Trivial = """
                                   module Bench
                                   import IO/Console
                                   routine start()
                                       show("hi")
                                       return
                                   """;

    /// <summary>Parses <paramref name="src"/> and fails the test on any grammar error, so a source the parser
    /// recovered from (and dropped statements of) never reaches the analysis as if it were whole.</summary>
    private static Program Parse(string src, string file)
    {
        var parser = new Builder.Parser.Parser(
            tokens: Lexers.Tokenize(source: src, fileName: file, language: Language.RazorForge),
            language: Language.RazorForge,
            fileName: file) { PrintsErrors = false };
        Program program = parser.Parse();
        Assert.False(condition: parser.HasErrors,
            userMessage: $"{file} did not parse: {string.Join(separator: "\n", values: parser.GetErrors())}");
        return program;
    }

    /// <summary>The mangled symbol defined by each `define ...` line (the first <c>@"..."</c>/<c>@ident</c>).</summary>
    private static HashSet<string> DefinedSymbols(string ll)
    {
        return SymbolsOnLinesStartingWith(ll: ll, prefix: "define ");
    }

    private static HashSet<string> SymbolsOnLinesStartingWith(string ll, string prefix)
    {
        var set = new HashSet<string>(comparer: StringComparer.Ordinal);
        foreach (string line in ll.Split(separator: '\n'))
        {
            if (!line.StartsWith(value: prefix, comparisonType: StringComparison.Ordinal))
            {
                continue;
            }

            int at = line.IndexOf(value: '@');
            if (at < 0)
            {
                continue;
            }

            string sym;
            if (at + 1 < line.Length && line[index: at + 1] == '"')
            {
                int end = line.IndexOf(value: '"', startIndex: at + 2);
                if (end < 0)
                {
                    continue;
                }

                sym = line.Substring(startIndex: at + 1,
                    length: end - at); // includes surrounding quotes
            }
            else
            {
                int end = line.IndexOf(value: '(', startIndex: at);
                if (end < 0)
                {
                    continue;
                }

                sym = line.Substring(startIndex: at + 1, length: end - at - 1)
                          .Trim();
            }

            set.Add(item: sym);
        }

        return set;
    }

    /// <summary>The normal whole-program (pruned) build — what ships today.</summary>
    private static string PrunedBuild(AnalysisResult r)
    {
        return new LlvmEmitter(userPrograms: r.Registry.UserPrograms,
            registry: r.Registry,
            options: new LlvmEmitterOptions
            {
                StdlibPrograms = r.Registry.StdlibPrograms,
                SynthesizedBodies = r.SynthesizedBodies,
                InstantiatedGenericBodies = r.InstantiatedGenericBodies,
                LiveRoutineKeys = r.LiveRoutineKeys,
                MaySuspendRoutineKeys = r.MaySuspendRoutineKeys
            }).Generate();
    }

    /// <summary>The delta build: user code with the base's symbols marked resident (⇒ declare, not define).</summary>
    private static string DeltaBuild(AnalysisResult r, IReadOnlyCollection<string> residentSymbols)
    {
        return new LlvmEmitter(userPrograms: r.Registry.UserPrograms,
            registry: r.Registry,
            options: new LlvmEmitterOptions
            {
                StdlibPrograms = r.Registry.StdlibPrograms,
                SynthesizedBodies = r.SynthesizedBodies,
                InstantiatedGenericBodies = r.InstantiatedGenericBodies,
                LiveRoutineKeys = r.LiveRoutineKeys,
                MaySuspendRoutineKeys = r.MaySuspendRoutineKeys,
                ResidentSymbols = residentSymbols
            }).Generate();
    }

    /// <summary>
    /// Resident-JIT incremental (B) lazy-on-demand M1 SPIKE — the "emit ONE routine on demand" primitive.
    /// Proves an LlvmEmitter fed a SINGLE instantiated body (+ residentSymbols = every OTHER emitted symbol)
    /// produces a valid module that DEFINES exactly that one routine and DECLARES its callees extern — the
    /// per-symbol module an ORC custom definition generator will materialize on an unresolved-symbol callback.
    /// </summary>
    [Fact]
    public void EmitOneRoutine_DefinesTargetDeclaresCallees()
    {
        AnalysisResult r =
            new SemanticVerifier(language: Language.RazorForge).Analyze(
                program: Parse(src: Trivial, file: "bench.rf"));
        Assert.Empty(collection: r.Errors);
        Builder.Lowering.Passes.CancellationInstrumentationPass.Run(
            programs: r.Registry.UserPrograms,
            instantiatedBodies: r.InstantiatedGenericBodies,
            maySuspendKeys: r.MaySuspendRoutineKeys);

        // The full reachable set (bare mangled names) — the "everything already materialized" universe.
        (string _, IReadOnlyCollection<string> allDefs) = new LlvmEmitter(
            userPrograms: new List<(Program, string, string)>(),
            registry: r.Registry,
            options: new LlvmEmitterOptions
            {
                StdlibPrograms = r.Registry.StdlibPrograms,
                SynthesizedBodies = r.SynthesizedBodies,
                InstantiatedGenericBodies = r.InstantiatedGenericBodies
            }).GenerateBase();
        var allDefsSet = new HashSet<string>(collection: allDefs, comparer: StringComparer.Ordinal);

        // Pick one real (non-synthesized, non-sentinel) stdlib body to materialize alone.
        KeyValuePair<string, MonomorphizedBody> target = r.InstantiatedGenericBodies.First(
            predicate: kv => !kv.Value.IsSynthesized &&
                             kv.Value.Ast.Body is BlockStatement { Statements.Count: > 0 } &&
                             kv.Value.Info.OwnerType is not { IsGenericDefinition: true } &&
                             allDefsSet.Contains(item: LlvmEmitter.MangleRoutineName(routine: kv.Value.Info)));
        string targetName = LlvmEmitter.MangleRoutineName(routine: target.Value.Info);

        // residentSymbols = every OTHER emitted symbol ⇒ this module defines ONLY the target.
        var resident = new HashSet<string>(collection: allDefs, comparer: StringComparer.Ordinal);
        resident.Remove(item: targetName);

        string oneIr = new LlvmEmitter(userPrograms: new List<(Program, string, string)>(),
            registry: r.Registry,
            options: new LlvmEmitterOptions
            {
                StdlibPrograms = r.Registry.StdlibPrograms,
                SynthesizedBodies = r.SynthesizedBodies,
                InstantiatedGenericBodies =
                    new Dictionary<string, MonomorphizedBody> { [key: target.Key] = target.Value },
                ResidentSymbols = resident
            }).Generate();

        HashSet<string> defs = DefinedSymbols(ll: oneIr);
        // RF-mangled defines are quoted (`@"[member] …"`); bare-name defines (_rf_trace_push, …) are the
        // shared-runtime shadow-stack HELPERS the emitter inlines into every module regardless of the body
        // set. Those are the GLOBALS-analogue-of-C4 concern (define once in the main module, extern in the
        // per-routine on-demand modules) — handled at the ORC wiring step, NOT part of the one-routine body set.
        var rfDefs = defs.Where(predicate: d => d.StartsWith(value: '"')).ToList();
        _out.WriteLine(message: $"target={targetName}");
        _out.WriteLine(message: $"one-routine module: rf-defines={rfDefs.Count} (bare-runtime={defs.Count - rfDefs.Count}), chars={oneIr.Length}");
        _out.WriteLine(message: "RF-DEFINES:\n  " + string.Join(separator: "\n  ", values: rfDefs));

        // The primitive defines EXACTLY the target RF routine, nothing resident re-defined, no @main.
        Assert.DoesNotContain(expectedSubstring: "define i32 @main(", actualString: oneIr);
        Assert.Single(collection: rfDefs);
        Assert.Equal(expected: targetName.Trim(trimChar: '"'),
            actual: rfDefs[index: 0].Trim(trimChar: '"'));
    }

    [Fact]
    public void GenerateBase_And_Delta_CoverPrunedBuild_WithTinyDelta()
    {
        // Cold analyze retains StdlibPrograms (the warm/snapshot path drops them).
        AnalysisResult r =
            new SemanticVerifier(language: Language.RazorForge).Analyze(
                program: Parse(src: Trivial, file: "bench.rf"));
        Assert.Empty(collection: r.Errors);
        Builder.Lowering.Passes.CancellationInstrumentationPass.Run(
            programs: r.Registry.UserPrograms,
            instantiatedBodies: r.InstantiatedGenericBodies,
            maySuspendKeys: r.MaySuspendRoutineKeys);

        // BASE: empty user programs + null live set (⇒ non-pruned) + stdlib present. Runs to completion
        // over the full stdlib (the non-pruned-emission risk) and emits no @main.
        var baseGen = new LlvmEmitter(userPrograms: new List<(Program, string, string)>(),
            registry: r.Registry,
            options: new LlvmEmitterOptions
            {
                StdlibPrograms = r.Registry.StdlibPrograms,
                SynthesizedBodies = r.SynthesizedBodies,
                InstantiatedGenericBodies = r.InstantiatedGenericBodies
            });
        (string baseIr, IReadOnlyCollection<string> baseSyms) = baseGen.GenerateBase();

        Assert.False(condition: string.IsNullOrWhiteSpace(value: baseIr));
        Assert.DoesNotContain(expectedSubstring: "define i32 @main(", actualString: baseIr);
        Assert.True(condition: baseSyms.Count > 100,
            userMessage:
            $"base should emit hundreds of stdlib symbols non-pruned, got {baseSyms.Count}");

        // DELTA: user build with base symbols resident. C4 makes it declare (not define) resident symbols;
        // if the over-prune tripwire misfired on a resident symbol this would THROW here.
        string deltaIr = DeltaBuild(r: r, residentSymbols: baseSyms);

        HashSet<string> baseDefs = DefinedSymbols(ll: baseIr);
        HashSet<string> deltaDefs = DefinedSymbols(ll: deltaIr);
        HashSet<string> prunedDefs = DefinedSymbols(ll: PrunedBuild(r: r));

        // Correctness: base ∪ delta covers everything the pruned whole-program build defines.
        HashSet<string> union = new(collection: baseDefs, comparer: StringComparer.Ordinal);
        union.UnionWith(other: deltaDefs);
        var uncovered = prunedDefs.Except(second: union)
                                  .OrderBy(keySelector: s => s)
                                  .ToList();
        _out.WriteLine(
            message:
            $"base={baseDefs.Count}  delta={deltaDefs.Count}  pruned={prunedDefs.Count}  uncovered={uncovered.Count}");
        _out.WriteLine(message: "DELTA defines:\n  " + string.Join(separator: "\n  ",
            values: deltaDefs.OrderBy(keySelector: s => s)));
        if (uncovered.Count > 0)
        {
            _out.WriteLine(message: "UNCOVERED:\n  " +
                                    string.Join(separator: "\n  ",
                                        values: uncovered.Take(count: 40)));
        }

        Assert.True(condition: uncovered.Count == 0,
            userMessage: $"{uncovered.Count} pruned define(s) in NEITHER base nor delta: " +
                         string.Join(separator: ", ", values: uncovered.Take(count: 10)));

        // The payoff: the delta is tiny — user routines + the handful of user-triggered stdlib variants
        // (e.g. the show(value,end) overload the call site synthesizes), NOT the whole stdlib.
        Assert.True(condition: deltaDefs.Count < 40,
            userMessage:
            $"delta should be tiny (user + user-triggered variants), got {deltaDefs.Count}");
    }

    /// <summary>
    /// AOT the standalone base IR to a native object via <see cref="BaseObjectCache"/> (opt + clang -c).
    /// clang runs as an EXTERNAL tool (no in-process libLLVM). It validates the
    /// base-object AOT primitive AND, on failure, clang's stderr pinpoints the exact malformed base IR
    /// (the (a') latent stdlib-internal codegen tail). clang -c tolerates undefined symbols (the ~102
    /// represent/serialize gap is resolved by the delta at JIT-link), so only genuinely malformed IR fails.
    /// </summary>
    [Fact]
    public void GenerateBase_Standalone_CompilesToObject()
    {
        // REALISTIC-SEED base (matches CompileDaemon.EnsureBaseArtifact): analyze a representative program that
        // exercises the COMMON generic instances (List/Dict/Set + display), NOT SeedAllStdlibRoutines. The demand
        // pipeline materializes exactly the reached closure into InstantiatedGenericBodies (no speculative
        // Hijacked[…] explosion) and we feed the FULL set to the base; a program's own uncommon instances fall to
        // the per-run delta. The seed's own `start` lives in UserPrograms, so passing empty userPrograms below
        // keeps it out of the base (@main is gated off in base mode regardless).
        var baseSa = new SemanticVerifier(language: Language.RazorForge);
        AnalysisResult baseR = baseSa.Analyze(
            program: Parse(src: Builder.Execution.BaseObjectCache.SeedProgramSource, file: "base.rf"));
        Assert.Empty(collection: baseR.Errors);
        Builder.Lowering.Passes.CancellationInstrumentationPass.Run(
            programs: baseR.Registry.UserPrograms,
            instantiatedBodies: baseR.InstantiatedGenericBodies,
            maySuspendKeys: baseR.MaySuspendRoutineKeys);

        var baseGen = new LlvmEmitter(userPrograms: new List<(Program, string, string)>(),
            registry: baseR.Registry,
            options: new LlvmEmitterOptions
            {
                StdlibPrograms = baseR.Registry.StdlibPrograms,
                SynthesizedBodies = baseR.SynthesizedBodies,
                InstantiatedGenericBodies = baseR.InstantiatedGenericBodies
            });
        (string baseIr, IReadOnlyCollection<string> baseSyms) = baseGen.GenerateBase();

        var cache = new Builder.Execution.BaseObjectCache(
            dir: Path.Combine(path1: Path.GetTempPath(), path2: "rf_base_cache_test"));
        // Fresh fingerprint per run so we always exercise the compile (not a stale hit).
        string fp = "test-" + baseIr.Length;
        File.Delete(path: cache.ObjectPath(fingerprint: fp));
        string? obj = cache.GetOrBuild(fingerprint: fp,
            baseIr: baseIr,
            buildMode: Builder.Targeting.RfBuildMode.Debug,
            wasCached: out bool cached);

        _out.WriteLine(
            message:
            $"standalone base: syms={baseSyms.Count} chars={baseIr.Length} obj={(obj ?? "<compile failed>")} cached={cached}");
        Assert.False(condition: cached);
        Assert.NotNull(@object: obj);
        Assert.True(condition: new FileInfo(fileName: obj!).Length > 0,
            userMessage: "base object file is empty");
    }
}
