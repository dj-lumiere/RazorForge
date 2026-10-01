using System.Diagnostics;
using System.Text;

namespace RazorForge.Tests.Meta;

/// <summary>
/// End-to-end check of the runtime use-after-steal net: a read of a binding that a loop moved out on an
/// earlier pass (the build cannot see it) crashes with UseAfterStealError naming the binding, instead of
/// using a stale entity. Builds and runs <c>tests/Fixtures/UseAfterSteal/*.rf</c> through <c>buildandrun</c>.
/// </summary>
public sealed class UseAfterStealTests
{
    private static readonly string RepoRoot = LocateRepoRoot();

    [Fact]
    public void StealInLoop_CrashesWithUseAfterSteal()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "steal_in_loop.rf");

        Assert.True(condition: exit != 0,
            userMessage: $"expected a crash, got exit 0\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "consumed 1", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "UseAfterStealError: 'a' was used after it was moved out with steal.",
            actualString: stderr);
    }

    private static (int Exit, string Stdout, string Stderr) RunFixture(string fixture)
    {
        string rfPath = Path.Combine(paths: [RepoRoot, "tests", "Fixtures", "UseAfterSteal", fixture]);
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { TestHelpers.ToolchainDll(sourcePath: rfPath), "buildandrun", rfPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = RepoRoot,
            Environment =
            {
                [key: "DOTNET_gcServer"] = "0", [key: "DOTNET_GCConserveMemory"] = "9"
            }
        };
        using var p = Process.Start(startInfo: psi)!;
        Task<string> outTask = p.StandardOutput.ReadToEndAsync();
        Task<string> errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(milliseconds: 300_000))
        {
            try { p.Kill(entireProcessTree: true); }
            catch
            {
                /* best effort */
            }

            Assert.Fail(message: $"{fixture} did not finish.");
        }

        return (p.ExitCode, outTask.Result, errTask.Result);
    }

    private static string LocateRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(value: dir))
        {
            if (File.Exists(path: Path.Combine(path1: dir, path2: "RazorForge.sln")))
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
            message: "Could not locate RazorForge.sln walking up from test assembly directory.");
    }
}
