using System.Diagnostics;
using System.Text;

namespace RazorForge.Tests.Meta;

/// <summary>
/// The Tessera backend writes a value whose name is a Tessera keyword (<c>block</c>, <c>claim</c>, <c>local</c>,
/// <c>branch</c>) between backticks: a member variable, a parameter and a local. Builds and runs
/// <c>tests/Fixtures/TesseraBackendNames</c>, whose <c>config.toml</c> picks <c>backend = "tessera"</c>.
/// </summary>
public sealed class TesseraBackendNameTests
{
    private static readonly string RepoRoot = LocateRepoRoot();

    [Fact]
    public void KeywordNames_BuildAndRunOnTheTesseraBackend()
    {
        string rfPath = Path.Combine(paths: [RepoRoot, "tests", "Fixtures", "TesseraBackendNames", "main.rf"]);
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
        Assert.Equal(expected: "2 3 9", actual: stdout.Trim());
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
