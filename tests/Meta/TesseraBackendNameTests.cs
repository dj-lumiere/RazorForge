using System.Diagnostics;
using System.Text;

namespace RazorForge.Tests.Meta;

/// <summary>
/// The Tessera backend writes a value whose name is a Tessera keyword (<c>block</c>, <c>claim</c>, <c>local</c>,
/// <c>branch</c>) between backticks: a member variable, a parameter and a local. Builds and runs
/// <c>tests/Fixtures/TesseraBackendNames</c>, whose <c>config.toml</c> picks <c>backend = "tessera"</c>, and
/// <c>tests/Fixtures/TesseraBackendPresets</c> (presets made by a construction, and globals) the same way.
/// </summary>
public sealed class TesseraBackendNameTests
{
    private static readonly string RepoRoot = LocateRepoRoot();

    [Fact]
    public void KeywordNames_BuildAndRunOnTheTesseraBackend()
    {
        Assert.Equal(expected: "2 3 9", actual: BuildAndRun(fixture: "TesseraBackendNames"));
    }

    /// <summary>
    /// A preset made by a construction (<c>preset ORIGIN: Point = Point(x: 0, y: 0)</c>) and a <c>global</c>, read
    /// in <c>start()</c> and in another routine: each is a module-level cell <c>start()</c> fills first.
    /// </summary>
    [Fact]
    public void ConstructedPresetsAndGlobals_BuildAndRunOnTheTesseraBackend()
    {
        Assert.Equal(expected: "0 0 12 home 2", actual: BuildAndRun(fixture: "TesseraBackendPresets"));
    }

    /// <summary>
    /// A local or parameter <c>steal</c> handed over and <c>=</c> filled again, on the Tessera backend: the slot is
    /// emptied at the hand-over, so the refilled value is torn down once at the end of its scope and the value that
    /// left is never torn down there. Prints what the LLVM build of the same program prints.
    /// </summary>
    [Fact]
    public void StealThenRefill_TearsDownTheRefilledValueOnTheTesseraBackend()
    {
        string expected = File.ReadAllText(path: Path.Combine(paths:
                [RepoRoot, "tests", "Fixtures", "Stdlib", "steal_then_refill_api.expected.txt"]))
            .ReplaceLineEndings(replacementText: "\n")
            .Trim();
        Assert.Equal(expected: expected,
            actual: BuildAndRun(fixture: "TesseraBackendStealRefill").ReplaceLineEndings(replacementText: "\n"));
    }

    /// <summary>Builds and runs <c>tests/Fixtures/{fixture}/main.rf</c> and returns what it printed.</summary>
    private static string BuildAndRun(string fixture)
    {
        string rfPath = Path.Combine(paths: [RepoRoot, "tests", "Fixtures", fixture, "main.rf"]);
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { TestHelpers.ToolchainDll(sourcePath: rfPath), "buildandrun", rfPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path: rfPath)!,
            Environment =
            {
                [key: "DOTNET_gcServer"] = "0", [key: "DOTNET_GCConserveMemory"] = "9", [key: "RF_NO_DAEMON"] = "1"
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

            Assert.Fail(message: "the Tessera backend build did not finish.");
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

        throw new InvalidOperationException(
            message: "Could not locate RazorForge.csproj walking up from test assembly directory.");
    }
}
