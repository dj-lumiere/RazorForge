using System.Diagnostics;
using System.Text;

namespace Revare.Tests.Meta;

/// <summary>
/// The Lapila backend writes a value whose name is a Lapila keyword (<c>block</c>, <c>claim</c>, <c>local</c>,
/// <c>branch</c>) between backticks: a member variable, a parameter and a local. Builds and runs
/// <c>tests/Fixtures/LapilaBackendNames</c>, whose <c>config.toml</c> picks <c>backend = "lapila"</c>, and
/// <c>tests/Fixtures/LapilaBackendPresets</c> (presets made by a construction, and globals) the same way.
/// </summary>
public sealed class LapilaBackendNameTests
{
    private static readonly string RepoRoot = LocateRepoRoot();

    [Fact]
    public void KeywordNames_BuildAndRunOnTheLapilaBackend()
    {
        Assert.Equal(expected: "2 3 9", actual: BuildAndRun(fixture: "LapilaBackendNames"));
    }

    /// <summary>
    /// A preset made by a construction (<c>preset ORIGIN: Point = Point(x: 0, y: 0)</c>) and a <c>global</c>, read
    /// in <c>start()</c> and in another routine: each is a module-level cell <c>start()</c> fills first.
    /// </summary>
    [Fact]
    public void ConstructedPresetsAndGlobals_BuildAndRunOnTheLapilaBackend()
    {
        Assert.Equal(expected: "0 0 12 home 2", actual: BuildAndRun(fixture: "LapilaBackendPresets"));
    }

    /// <summary>
    /// A local or parameter <c>steal</c> handed over and <c>=</c> filled again, on the Lapila backend: the slot is
    /// emptied at the hand-over, so the refilled value is torn down once at the end of its scope and the value that
    /// left is never torn down there. Prints what the LLVM build of the same program prints.
    /// </summary>
    [Fact]
    public void StealThenRefill_TearsDownTheRefilledValueOnTheLapilaBackend()
    {
        string expected = File.ReadAllText(path: Path.Combine(paths:
                [RepoRoot, "tests", "Fixtures", "Stdlib", "steal_then_refill_api.expected.txt"]))
            .ReplaceLineEndings(replacementText: "\n")
            .Trim();
        Assert.Equal(expected: expected,
            actual: BuildAndRun(fixture: "LapilaBackendStealRefill").ReplaceLineEndings(replacementText: "\n"));
    }

    /// <summary>
    /// <c>gather()</c> on a <c>List[Work[None]]</c> of more jobs than a list first has room for, on the Lapila
    /// backend: <c>me[i].state</c> reads the Work through the token <c>modify_at</c> gives, and the list of the jobs'
    /// None results keeps no storage, so its address is zero and moving it by nothing must not stop the program.
    /// </summary>
    [Fact]
    public void GatherOfWorkNone_RunsOnTheLapilaBackend()
    {
        Assert.Equal(expected: "9", actual: BuildAndRun(fixture: "LapilaBackendGatherNone"));
    }

    /// <summary>Builds and runs <c>tests/Fixtures/{fixture}/main.rvr</c> and returns what it printed.</summary>
    private static string BuildAndRun(string fixture)
    {
        string rvPath = Path.Combine(paths: [RepoRoot, "tests", "Fixtures", fixture, "main.rvr"]);
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { TestHelpers.ToolchainDll(sourcePath: rvPath), "run", rvPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path: rvPath)!,
            Environment =
            {
                [key: "DOTNET_gcServer"] = "0", [key: "DOTNET_GCConserveMemory"] = "9", [key: "RV_NO_DAEMON"] = "1"
            }
        };
        using var p = Process.Start(startInfo: psi)!;
        Task<string> outTask = p.StandardOutput.ReadToEndAsync();
        Task<string> errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(milliseconds: TestHelpers.ChildTimeoutMs(baseMs: 300_000)))
        {
            try { p.Kill(entireProcessTree: true); }
            catch
            {
                /* best effort */
            }

            Assert.Fail(message: "the Lapila backend build did not finish.");
        }

        string stdout = outTask.Result;
        Assert.True(condition: p.ExitCode == 0,
            userMessage: $"exit {p.ExitCode}\n--- stdout ---\n{stdout}\n--- stderr ---\n{errTask.Result}");
        return stdout.Trim();
    }

    private static string LocateRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(value: dir))
        {
            if (File.Exists(path: Path.Combine(path1: dir, path2: "Revare.csproj")))
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

        throw new InvalidOperationException(
            message: "Could not locate Revare.csproj walking up from test assembly directory.");
    }
}
