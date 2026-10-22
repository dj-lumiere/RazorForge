using System.Text;
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
                "    show(\"Atomic CircularList\")  # Atomic\n" +
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
                      "    show(\"Atomic CircularList\")  # Atomic\n" +
                      "    var atomic_hits = hits.load()\n" +
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
                      "    each i in n to 1\n" +
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
