using System.Text;
using System.Text.RegularExpressions;
using Builder;
using Builder.Execution;

namespace RazorForge.Tests.Execution;

/// <summary>
/// Drives the builder CLI (<c>src/Execution/Program.cs</c>) in-process via <see cref="Program.Main"/> for
/// the non-spawning verbs — <c>version</c>, <c>help</c>, <c>tokenize</c>, <c>parse</c>, <c>codegen</c>,
/// <c>check</c>, <c>validate-stdlib</c> — plus the arg-handling and error branches. These verbs run entirely
/// in the test host (no clang/opt/link subprocess), so they are coverage-instrumented; <c>build</c> /
/// <c>buildandrun</c> are intentionally excluded (they spawn a native toolchain in an uninstrumented child).
///
/// All tests share one class (xUnit runs a class's tests sequentially) because <see cref="Program.Main"/>
/// mutates process-global console encoding and the tests redirect <see cref="Console.Out"/>.
/// </summary>
public sealed class ProgramCliTests : IDisposable
{
    private readonly string _dir;
    private readonly string _rf;

    public ProgramCliTests()
    {
        _dir = Path.Combine(path1: Path.GetTempPath(),
            path2: "rf-cli-tests-" + Guid.NewGuid().ToString(format: "N"));
        Directory.CreateDirectory(path: _dir);
        _rf = Path.Combine(path1: _dir, path2: "sample.rf");
        File.WriteAllText(path: _rf,
            contents:
            "module Test/Cli\n" +
            "import IO/Console\n" +
            "\n" +
            "routine sum_ints(a: S32, b: S32) -> S32\n" +
            "    return a + b\n" +
            "\n" +
            "routine start()\n" +
            "    var total = sum_ints(a: 1, b: 2)\n" +
            "    show(f\"{total}\")\n" +
            "    return\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(path: _dir, recursive: true); }
        catch
        {
            /* best-effort temp cleanup */
        }
    }

    /// <summary>Invokes <see cref="Program.Main"/> with console output captured; returns (exitCode, stdout).</summary>
    private static (int Code, string Out) RunMain(params string[] args)
    {
        TextWriter savedOut = Console.Out;
        Encoding savedOutEnc = Console.OutputEncoding;
        Encoding savedInEnc = Console.InputEncoding;
        var sw = new StringWriter();
        try
        {
            Console.SetOut(newOut: sw);
            int code = Program.Run(args: args, cliLanguage: global::TypeModel.Enums.Language.RazorForge);
            return (code, sw.ToString());
        }
        finally
        {
            Console.SetOut(newOut: savedOut);
            // Main forces UTF-8 on the console encodings; restore so we don't perturb other tests.
            try
            {
                Console.OutputEncoding = savedOutEnc;
                Console.InputEncoding = savedInEnc;
            }
            catch
            {
                /* encoding restore is best-effort (redirected consoles reject it) */
            }
        }
    }

    [Fact]
    public void NoArgs_PrintsUsage_ReturnsZero()
    {
        (int code, string outp) = RunMain();
        Assert.Equal(expected: 0, actual: code);
        Assert.False(condition: string.IsNullOrWhiteSpace(value: outp));
    }

    [Fact]
    public void Version_PrintsVersion_ReturnsZero()
    {
        (int code, string outp) = RunMain("version");
        Assert.Equal(expected: 0, actual: code);
        Assert.False(condition: string.IsNullOrWhiteSpace(value: outp));
    }

    [Fact]
    public void Help_PrintsUsage_ReturnsZero()
    {
        (int code, string outp) = RunMain("help");
        Assert.Equal(expected: 0, actual: code);
        Assert.False(condition: string.IsNullOrWhiteSpace(value: outp));
    }

    [Fact]
    public void Tokenize_ValidFile_ReturnsZero()
    {
        (int code, _) = RunMain("tokenize", _rf);
        Assert.Equal(expected: 0, actual: code);
    }

    [Fact]
    public void Parse_ValidFile_ReturnsZero()
    {
        (int code, _) = RunMain("parse", _rf);
        Assert.Equal(expected: 0, actual: code);
    }

    [Fact]
    public void BareFile_IsNotACommand_ReturnsOne()
    {
        (int code, _) = RunMain(_rf);
        Assert.Equal(expected: 1, actual: code);
    }

    [Fact]
    public void LlvmEmitter_ValidFile_WritesIrAndReturnsZero()
    {
        string outLl = Path.Combine(path1: _dir, path2: "sample.ll");
        (int code, _) = RunMain("codegen", _rf, outLl);
        Assert.Equal(expected: 0, actual: code);
        Assert.True(condition: File.Exists(path: outLl), userMessage: "codegen should write the .ll file");
        Assert.False(condition: string.IsNullOrWhiteSpace(value: File.ReadAllText(path: outLl)));
    }

    /// <summary>
    /// An <c>Int</c> literal outside the <c>S64</c> range is a <c>Guarded[List[U64], ReadOnly]</c> the builder lays down
    /// as module data: the manager and the list are written field by field, and each field must get what the
    /// declarations in <c>System/Memory/GuardManager.rf</c> and <c>System/List.rf</c> say it holds. The emitted IR names
    /// each struct's member variables in order, so this reads the order from there and checks every value: the share
    /// count starts at 1, the weak count and the policy state at 0, the manager's <c>data</c> is the list, and the
    /// list's <c>data</c>, <c>count</c> and <c>capacity</c> are the limbs and their number. A use shares it once more.
    /// A field the builder does not know stops the build, which fails this test too.
    /// </summary>
    [Fact]
    public void LlvmEmitter_BigIntLiteral_LaysTheListOutAsGuardManagerAndListDeclareIt()
    {
        string rf = Path.Combine(path1: _dir, path2: "big.rf");
        File.WriteAllText(path: rf,
            contents:
            "module Test/BigLiteral\n" +
            "import IO/Console\n" +
            "\n" +
            "routine start()\n" +
            "    var big: Int = 340282366920938463463374607431768211457\n" +
            "    show(big)\n" +
            "    return\n");
        string outLl = Path.Combine(path1: _dir, path2: "big.ll");
        (int code, string output) = RunMain("codegen", rf, outLl);
        Assert.True(condition: code == 0, userMessage: output);
        string ir = File.ReadAllText(path: outLl);

        Dictionary<string, string> Fields(string globalSuffix, string structPrefix)
        {
            Match global = Regex.Match(input: ir,
                pattern: $@"^(@\.shared\.\d+)\.{globalSuffix} = private global (%""{Regex.Escape(str: structPrefix)}[^""]*"") \{{ (.*) \}}$",
                options: RegexOptions.Multiline);
            Assert.True(condition: global.Success, userMessage: $"no {globalSuffix} global in:\n{ir}");
            string structName = global.Groups[groupnum: 2].Value;
            Match names = Regex.Match(input: ir,
                pattern: $@"^; {Regex.Escape(str: structName)} member variables: (.*)$", options: RegexOptions.Multiline);
            Assert.True(condition: names.Success, userMessage: $"no member variable list for {structName}");
            string[] fieldNames = names.Groups[groupnum: 1].Value.Split(separator: ", ")
                                       .Select(selector: n => n[(n.IndexOf(value: '=') + 1)..]).ToArray();
            string[] values = global.Groups[groupnum: 3].Value.Split(separator: ", ");
            Assert.Equal(expected: fieldNames.Length, actual: values.Length);
            var byName = new Dictionary<string, string>();
            for (int i = 0; i < fieldNames.Length; i++)
            {
                byName[key: fieldNames[i]] = values[i].Replace(oldValue: global.Groups[groupnum: 1].Value, newValue: "@");
            }

            return byName;
        }

        // 2^128 + 1 is three limbs: 1, 0, 1.
        Assert.Matches(expectedRegexPattern: @"@\.shared\.\d+\.data = private unnamed_addr constant \[3 x i64\] \[i64 1, i64 0, i64 1\]",
            actualString: ir);
        Dictionary<string, string> list = Fields(globalSuffix: "list", structPrefix: "Entity.System.List[");
        Assert.Equal(expected: 3, actual: list.Count);
        Assert.Equal(expected: "ptr @.data", actual: list[key: "data"]);
        Assert.Equal(expected: "i64 3", actual: list[key: "count"]);
        Assert.Equal(expected: "i64 3", actual: list[key: "capacity"]);
        Dictionary<string, string> manager = Fields(globalSuffix: "manager", structPrefix: "Entity.System.GuardManager[");
        Assert.Equal(expected: 4, actual: manager.Count);
        Assert.Equal(expected: "i64 1", actual: manager[key: "strong_count"]);
        Assert.Equal(expected: "i64 0", actual: manager[key: "weak_count"]);
        Assert.Equal(expected: "ptr @.list", actual: manager[key: "data"]);
        Assert.EndsWith(expectedEndString: "zeroinitializer", actualString: manager[key: "access"]);
        Assert.Matches(expectedRegexPattern: @"atomicrmw add ptr %\S+, i64 1 seq_cst", actualString: ir);
    }

    [Fact]
    public void Check_ValidFile_ReturnsZero()
    {
        (int code, _) = RunMain("check", _rf);
        Assert.Equal(expected: 0, actual: code);
    }

    [Fact]
    public void ValidateStdlib_RazorForge_ReturnsZero()
    {
        (int code, _) = RunMain("validate-stdlib", "rf");
        Assert.Equal(expected: 0, actual: code);
    }

    [Fact]
    public void Tokenize_MissingPathArg_ReturnsError()
    {
        (int code, _) = RunMain("tokenize");
        Assert.NotEqual(expected: 0, actual: code);
    }

    [Fact]
    public void Parse_MissingPathArg_ReturnsError()
    {
        (int code, _) = RunMain("parse");
        Assert.NotEqual(expected: 0, actual: code);
    }

    [Fact]
    public void Check_SourceWithError_ReturnsNonZero()
    {
        string bad = Path.Combine(path1: _dir, path2: "bad.rf");
        File.WriteAllText(path: bad,
            contents:
            "module Test/Bad\n" +
            "routine start()\n" +
            "    var x = undefined_symbol_here\n" +
            "    return\n");
        (int code, _) = RunMain("check", bad);
        Assert.NotEqual(expected: 0, actual: code);
    }

    /// <summary>A previous-release project in its own folder: <paramref name="files"/> by name, and a manifest
    /// named <c>razorforge.toml</c> when <paramref name="oldManifest"/>.</summary>
    private string OldProject(string name, bool oldManifest, params (string File, string Text)[] files)
    {
        string dir = Path.Combine(path1: _dir, path2: name);
        Directory.CreateDirectory(path: dir);
        foreach ((string file, string text) in files)
        {
            File.WriteAllText(path: Path.Combine(path1: dir, path2: file), contents: text);
        }

        if (oldManifest)
        {
            File.WriteAllText(path: Path.Combine(path1: dir, path2: "razorforge.toml"),
                contents: "[package]\nname = \"old\"\nversion = \"0.0.1\"\n\n[target]\nmode = \"debug\"\n");
        }

        return dir;
    }

    [Fact]
    public void MigrateIndent_DoublesTwoSpaceIndent_LeavesTextLiteralLines()
    {
        string dir = OldProject(name: "indent", oldManifest: false, files:
        [
            ("old.rf",
                "module Test/Indent\n" +
                "routine start()\n" +
                "  var t = \"first\n" +
                "  second\"\n" +
                "  if true\n" +
                "    # a comment\n" +
                "    var x = (1,\n" +
                "       2)\n" +
                "  return\n"),
            ("new.rf", "module Test/Indented\nroutine start()\n    return\n")
        ]);

        (int code, string output) = RunMain("migrate", "--step", "indent", dir);
        Assert.Equal(expected: 0, actual: code);
        Assert.Contains(expectedSubstring: "1 file(s) changed", actualString: output);
        Assert.Equal(
            expected: "module Test/Indent\n" +
                      "routine start()\n" +
                      "    var t = \"first\n" +
                      "  second\"\n" +
                      "    if true\n" +
                      "        # a comment\n" +
                      "        var x = (1,\n" +
                      "              2)\n" +
                      "    return\n",
            actual: File.ReadAllText(path: Path.Combine(path1: dir, path2: "old.rf")));
        Assert.Equal(expected: "module Test/Indented\nroutine start()\n    return\n",
            actual: File.ReadAllText(path: Path.Combine(path1: dir, path2: "new.rf")));
    }

    [Fact]
    public void MigrateFor_RewritesEachLoops_InCodeOnly()
    {
        string dir = OldProject(name: "for", oldManifest: false, files:
        [
            ("old.rf",
                "module Test/ForRf\n" +
                "routine start()\n" +
                "    each x in xs  # each x in xs\n" +
                "        show(\"each {x}\")\n" +
                "    each (i, x) in xs.enumerate()\n" +
                "        nothing\n" +
                "    each i in 0 til 10 by 2\n" +
                "        nothing\n" +
                "    return\n"),
            ("old.sf",
                "module Test/ForSf\n" +
                "routine start()\n" +
                "    each k: U64 in 0 til n\n" +
                "        show(k)\n")
        ]);

        (int code, string output) = RunMain("migrate", "--step", "for", dir);
        Assert.Equal(expected: 0, actual: code);
        Assert.Contains(expectedSubstring: "2 file(s) changed", actualString: output);
        Assert.Equal(
            expected: "module Test/ForRf\n" +
                      "routine start()\n" +
                      "    for x in xs  # each x in xs\n" +
                      "        show(\"each {x}\")\n" +
                      "    for (i, x) in xs.enumerate()\n" +
                      "        nothing\n" +
                      "    for i in 0 til 10 by 2\n" +
                      "        nothing\n" +
                      "    return\n",
            actual: File.ReadAllText(path: Path.Combine(path1: dir, path2: "old.rf")));
        Assert.Equal(
            expected: "module Test/ForSf\nroutine start()\n    for k: U64 in 0 til n\n        show(k)\n",
            actual: File.ReadAllText(path: Path.Combine(path1: dir, path2: "old.sf")));
    }

    [Fact]
    public void MigrateWork_RenamesAgent_KeepsTheStartVerbs()
    {
        string dir = OldProject(name: "work", oldManifest: false, files:
        [
            ("old.rf",
                "module Test/WorkRf\n" +
                "routine start()\n" +
                "    var job: Agent[S64] = f.to[Agent](x: 1)\n" +
                "    job.start_here()\n" +
                "    show(\"Agent\")  # Agent\n" +
                "    var e: AgentStartError = AgentStartError(reason: \"\")\n" +
                "    return\n")
        ]);

        (int code, string output) = RunMain("migrate", "--step", "work", dir);
        Assert.Equal(expected: 0, actual: code);
        Assert.Contains(expectedSubstring: "1 file(s) changed", actualString: output);
        Assert.Equal(
            expected: "module Test/WorkRf\n" +
                      "routine start()\n" +
                      "    var job: Work[S64] = f.to[Work](x: 1)\n" +
                      "    job.start_here()\n" +
                      "    show(\"Agent\")  # Agent\n" +
                      "    var e: WorkStartError = WorkStartError(reason: \"\")\n" +
                      "    return\n",
            actual: File.ReadAllText(path: Path.Combine(path1: dir, path2: "old.rf")));
    }

    [Fact]
    public void MigrateRandom_NamesTheDrawnType_InCodeAndHoles()
    {
        string dir = OldProject(name: "random", oldManifest: false, files:
        [
            ("old.rf",
                "module Test/RandomRf\n" +
                "routine start()\n" +
                "    var a = rng.next_u64()\n" +
                "    var b = rng.next_u32() + 1  # rng.next_u32()\n" +
                "    show(f\"{rng.next_b64()} and next_b64()\")\n" +
                "    show(\"rng.next_u64()\")\n" +
                "    var e = Entropy.next_u64()\n" +
                "    return\n")
        ]);

        (int code, string output) = RunMain("migrate", "--step", "random", dir);
        Assert.Equal(expected: 0, actual: code);
        Assert.Contains(expectedSubstring: "1 file(s) changed", actualString: output);
        Assert.Equal(
            expected: "module Test/RandomRf\n" +
                      "routine start()\n" +
                      "    var a = rng.next[U64]()\n" +
                      "    var b = rng.next[U32]() + 1  # rng.next_u32()\n" +
                      "    show(f\"{rng.next[B64]()} and next_b64()\")\n" +
                      "    show(\"rng.next_u64()\")\n" +
                      "    var e = Entropy.next[U64]()\n" +
                      "    return\n",
            actual: File.ReadAllText(path: Path.Combine(path1: dir, path2: "old.rf")));
    }

    [Fact]
    public void MigrateNumbers_DecidesEachCallFromTheTypesAnalysisGave()
    {
        const string source = "    var a = U8(x)\n    var b = U8(200)\n    var c = B32(n * 2) + U8(7)\n    var d = Decimal(x)\n";
        int At(string text) => source.IndexOf(value: text, comparisonType: StringComparison.Ordinal);

        (string? a, int aStart, int aLength, _) =
            Program.DecideNumberCall(source: source, position: At(text: "U8(x)"), target: "U8", from: "S64", literal: false);
        Assert.Equal(expected: "x.to[U8]()", actual: a);
        Assert.Equal(expected: "U8(x)".Length, actual: aLength);
        Assert.Equal(expected: At(text: "U8(x)"), actual: aStart);

        (string? b, _, _, _) =
            Program.DecideNumberCall(source: source, position: At(text: "U8(200)"), target: "U8", from: "S64", literal: true);
        Assert.Equal(expected: "    var b: U8 = 200", actual: b);

        (string? c, _, _, _) =
            Program.DecideNumberCall(source: source, position: At(text: "B32("), target: "B32", from: "S64", literal: false);
        Assert.Equal(expected: "(n * 2).to[B32]()", actual: c);

        (string? seven, _, _, string sevenWhy) =
            Program.DecideNumberCall(source: source, position: At(text: "U8(7)"), target: "U8", from: "S64", literal: true);
        Assert.Null(@object: seven);
        Assert.Contains(expectedSubstring: "'var n: U8 = 7'", actualString: sevenWhy);

        (string? d, _, _, string dWhy) =
            Program.DecideNumberCall(source: source, position: At(text: "Decimal("), target: "Decimal", from: "S64", literal: false);
        Assert.Null(@object: d);
        Assert.Contains(expectedSubstring: "decide which one you meant", actualString: dWhy);
    }

    [Fact]
    public void MigratePierce_RewritesTheOldStatement_LeavesPierceAsAName()
    {
        string dir = OldProject(name: "pierce", oldManifest: false, files:
        [
            ("old.rf",
                "module Test/PierceRf\n" +
                "routine start()\n" +
                "    var pierce = 1\n" +
                "    pierce = pierce + 1\n" +
                "    if pierce > 5\n" +
                "        pierce LogicBreachedError()\n" +
                "    return\n")
        ]);

        (int code, string output) = RunMain("migrate", "--step", "pierce", dir);
        Assert.Equal(expected: 0, actual: code);
        Assert.Contains(expectedSubstring: "1 file(s) changed", actualString: output);
        Assert.Equal(
            expected: "module Test/PierceRf\n" +
                      "routine start()\n" +
                      "    var pierce = 1\n" +
                      "    pierce = pierce + 1\n" +
                      "    if pierce > 5\n" +
                      "        throw LogicBreachedError()\n" +
                      "    return\n",
            actual: File.ReadAllText(path: Path.Combine(path1: dir, path2: "old.rf")));
    }

    [Fact]
    public void MigrateTypeNames_RenamesAtomicAndCircularList_KeepsStringsAndComments()
    {
        string dir = OldProject(name: "type-names", oldManifest: false, files:
        [
            ("old.rf",
                "module Test/TypeNamesRf\n" +
                "import Collections\n" +
                "record Cell[T]\n" +
                "needs T obeys AtomicType\n" +
                "    value: Atomic[T]\n" +
                "\n" +
                "routine start()\n" +
                "    var hits = Atomic[U64](initial: 0)\n" +
                "    var ring: CircularList[S64] = [1, 2]\n" +
                "    var big: Integer = 7.to[Integer]()\n" +
                "    show(\"Atomic CircularList Integer\")  # Atomic Integer\n" +
                "    var atomic_hits = hits.load()\n" +
                "    return\n")
        ]);

        (int code, string output) = RunMain("migrate", "--step", "type-names", dir);
        Assert.Equal(expected: 0, actual: code);
        Assert.Contains(expectedSubstring: "1 file(s) changed", actualString: output);
        Assert.Equal(
            expected: "module Test/TypeNamesRf\n" +
                      "import Collections\n" +
                      "record Cell[T]\n" +
                      "needs T obeys UnbrokenType\n" +
                      "    value: Unbroken[T]\n" +
                      "\n" +
                      "routine start()\n" +
                      "    var hits = Unbroken[U64](initial: 0)\n" +
                      "    var ring: RotatableList[S64] = [1, 2]\n" +
                      "    var big: Int = 7.to[Int]()\n" +
                      "    show(\"Atomic CircularList Integer\")  # Atomic Integer\n" +
                      "    var atomic_hits = hits.load()\n" +
                      "    return\n",
            actual: File.ReadAllText(path: Path.Combine(path1: dir, path2: "old.rf")));
    }

    [Fact]
    public void MigrateTypeNames_Maybe_BecomesOptional()
    {
        string dir = OldProject(name: "type-names-optional", oldManifest: false, files:
        [
            ("old.rf",
                "module Test/TypeNamesOptional\n" +
                "entity Node\n" +
                "    next: Maybe[Node]\n" +
                "\n" +
                "routine start()\n" +
                "    var count: Maybe[S64] = none\n" +
                "    var head: Maybe[Node] = none  # Maybe[Node]\n" +
                "    return\n")
        ]);

        (int code, string output) = RunMain("migrate", "--step", "type-names", dir);
        Assert.Equal(expected: 0, actual: code);
        Assert.Contains(expectedSubstring: "1 file(s) changed", actualString: output);
        Assert.Equal(
            expected: "module Test/TypeNamesOptional\n" +
                      "entity Node\n" +
                      "    next: Optional[Node]\n" +
                      "\n" +
                      "routine start()\n" +
                      "    var count: Optional[S64] = none\n" +
                      "    var head: Optional[Node] = none  # Maybe[Node]\n" +
                      "    return\n",
            actual: File.ReadAllText(path: Path.Combine(path1: dir, path2: "old.rf")));
    }

    [Fact]
    public void MigrateDefault_IndentsFirst_RewritesReleaseSpellings_RenamesManifest()
    {
        string dir = OldProject(name: "release", oldManifest: true, files:
        [
            ("old.rf",
                "module Test/Release\n" +
                "record Pt\n" +
                "  x: S64\n" +
                "\n" +
                "routine Pt.create(x: S64) -> Pt\n" +
                "  return Pt(x: x)\n" +
                "\n" +
                "failable routine halve(n: S64) -> S64\n" +
                "  return n // 2\n" +
                "\n" +
                "routine spin(z: C64) -> C128\n" +
                "  return z\n" +
                "\n" +
                "routine start()\n" +
                "  var p = Pt.create(x: 1)\n" +
                "  var h = try_halve(n: 4)\n" +
                "  var c = p.check_size()\n" +
                "  var w = -p.x ** 2\n" +
                "  var n = 5_s64 + -2_s64 ** 2\n" +
                "  var hits = Atomic[S64](initial: 0)\n" +
                "  each i in n to 1\n" +
                "    nothing\n" +
                "  return\n")
        ]);

        (_, string output) = RunMain("migrate", dir);
        Assert.Equal(
            expected: "module Test/Release\n" +
                      "record Pt\n" +
                      "    x: S64\n" +
                      "\n" +
                      "routine Pt(x: S64) -> Pt\n" +
                      "    return Pt(x: x)\n" +
                      "\n" +
                      "failable routine halve(n: S64) -> S64\n" +
                      "    return n // 2\n" +
                      "\n" +
                      "routine spin(z: C128) -> C256\n" +
                      "    return z\n" +
                      "\n" +
                      "routine start()\n" +
                      "    var p = Pt(x: 1)\n" +
                      "    var h = try halve(n: 4)\n" +
                      "    var c = check p.size()\n" +
                      "    var w = (-p.x) ** 2\n" +
                      "    var n = 5_s64 + (-2_s64) ** 2\n" +
                      "    var hits = Unbroken[S64](initial: 0)\n" +
                      "    for i in n to 1\n" +
                      "        nothing\n" +
                      "    return\n",
            actual: File.ReadAllText(path: Path.Combine(path1: dir, path2: "old.rf")));
        Assert.Contains(expectedSubstring: "a range counts up only now", actualString: output);
        Assert.Contains(expectedSubstring: "run migrate --step suffix", actualString: output);
        Assert.False(condition: File.Exists(path: Path.Combine(path1: dir, path2: "razorforge.toml")));
        Assert.Contains(expectedSubstring: "razorforge-version = \"0.5.0\"",
            actualString: File.ReadAllText(path: Path.Combine(path1: dir, path2: "config.toml")));
    }

    [Fact]
    public void MigrateDefault_CurrentManifest_MovesOnlyTheFileThatDoesNotReadAsThisRelease()
    {
        string dir = OldProject(name: "marked", oldManifest: false, files:
        [
            ("old.rf",
                "module Test/MarkedOld\n" +
                "routine start()\n" +
                "    var xs = [1, 2]\n" +
                "    each x in xs\n" +
                "        nothing\n" +
                "    return\n"),
            ("new.rf",
                "module Test/MarkedNew\n" +
                "routine run()\n" +
                "    return\n")
        ]);
        File.WriteAllText(path: Path.Combine(path1: dir, path2: "config.toml"),
            contents: "[package]\nname = \"marked\"\nrazorforge-version = \"0.5.0\"\n");
        string untouched = File.ReadAllText(path: Path.Combine(path1: dir, path2: "new.rf"));

        (_, string output) = RunMain("migrate", dir);
        Assert.Contains(expectedSubstring: "    for x in xs\n",
            actualString: File.ReadAllText(path: Path.Combine(path1: dir, path2: "old.rf")));
        Assert.Equal(expected: untouched, actual: File.ReadAllText(path: Path.Combine(path1: dir, path2: "new.rf")));
        Assert.Contains(expectedSubstring: "does not read as it", actualString: output);
        Assert.Contains(expectedSubstring: "1 file(s) skipped", actualString: output);
    }

    [Fact]
    public void MigrateDefault_WithoutManifest_ListsComplexNames_KeepsDeclaredPrefixedRoutines()
    {
        string dir = OldProject(name: "nomanifest", oldManifest: false, files:
        [
            ("new.rf",
                "module Test/NoManifest\n" +
                "routine try_hard(n: S64) -> S64\n" +
                "    return n\n" +
                "\n" +
                "routine spin(z: C64) -> C64\n" +
                "    return z\n" +
                "\n" +
                "routine start()\n" +
                "    var a = try_hard(n: 1)\n" +
                "    return\n")
        ]);

        string before = File.ReadAllText(path: Path.Combine(path1: dir, path2: "new.rf"));
        (_, string output) = RunMain("migrate", dir);
        Assert.Equal(expected: before, actual: File.ReadAllText(path: Path.Combine(path1: dir, path2: "new.rf")));
        Assert.Contains(expectedSubstring: "'C64' is two B32 parts now", actualString: output);
    }

    [Fact]
    public void Check_OldManifestName_IsABuildErrorNamingConfigToml()
    {
        string dir = OldProject(name: "oldmanifest", oldManifest: true, files:
        [
            ("main.rf", "module Test/OldManifest\nroutine start()\n    return\n")
        ]);

        (int code, string output) = RunMain("check", Path.Combine(path1: dir, path2: "main.rf"));
        Assert.NotEqual(expected: 0, actual: code);
        Assert.Contains(expectedSubstring: "named config.toml now", actualString: output);
    }
}
