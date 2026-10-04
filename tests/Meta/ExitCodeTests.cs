using System.Diagnostics;
using System.Text;

namespace RazorForge.Tests.Meta;

/// <summary>
/// End-to-end check of <c>set_exit_code</c>: a program that returns from <c>start</c> normally ends with the status
/// it set last, which the builder's <c>main</c> returns after <c>start</c>. Builds and runs
/// <c>tests/Fixtures/ExitCode/set_exit_code.rf</c> through <c>buildandrun</c>. Also holds the fixtures that must
/// crash (status 82), which the one-program stdlib harness cannot run.
/// </summary>
public sealed class ExitCodeTests
{
    private static readonly string RepoRoot = LocateRepoRoot();

    [Fact]
    public void SetExitCode_LastCallIsTheExitStatus()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "set_exit_code.rf");

        Assert.True(condition: exit == 42,
            userMessage: $"expected exit 42, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "returning", actualString: stdout);
    }

    /// <summary>A bare <c>Real</c> division by zero is not recovered, so the program crashes with status 82 and the
    /// <c>DivisionByZeroError</c> instead of printing an infinity.</summary>
    [Fact]
    public void RealDivisionByZero_CrashesLoudly()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "real_divide_by_zero.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "dividing by zero", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "DivisionByZeroError: You tried to divide by zero", actualString: stdout + stderr);
    }

    private static (int Exit, string Stdout, string Stderr) RunFixture(string fixture)
    {
        string rfPath = Path.Combine(paths: [RepoRoot, "tests", "Fixtures", "ExitCode", fixture]);
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
