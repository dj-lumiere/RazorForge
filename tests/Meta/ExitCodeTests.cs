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

    /// <summary>A recovery keyword recovers every failure beneath it except <c>pierce</c>: a <c>pierce</c> two
    /// calls under <c>try</c> still crashes with status 82, after the same call recovered a plain throw.</summary>
    [Fact]
    public void PierceUnderTry_CrashesLoudly()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "pierce_under_try.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "recovered throw: none", actualString: stdout);
        Assert.Contains(expectedSubstring: "value: 6", actualString: stdout);
        Assert.Contains(expectedSubstring: "piercing", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Broken invariant error: the ledger no longer balances",
            actualString: stdout + stderr);
    }

    /// <summary>A lambda's failure is recovered only beneath a recovery keyword: called with no keyword above
    /// the call (here by an <c>each</c> loop over a <c>select</c>) it crashes with status 82, and the loop does
    /// not quietly end early.</summary>
    [Fact]
    public void LambdaFailureWithoutKeyword_CrashesLoudly()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "lambda_bare_crash.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "recovered: none", actualString: stdout);
        Assert.Contains(expectedSubstring: "step 2", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "step 5", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Division by zero error", actualString: stdout + stderr);
    }

    /// <summary>A <c>pierce</c> inside a lambda another routine calls still crashes with status 82 beneath
    /// <c>try</c>, after the same call recovered the lambda's ordinary failure.</summary>
    [Fact]
    public void PierceInLambdaUnderTry_CrashesLoudly()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "pierce_in_lambda.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "recovered: none", actualString: stdout);
        Assert.Contains(expectedSubstring: "value: 25", actualString: stdout);
        Assert.Contains(expectedSubstring: "piercing", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Broken invariant error: the ledger no longer balances",
            actualString: stdout + stderr);
    }

    /// <summary>A range step held in a variable is checked when the range is iterated: a negative step counts down,
    /// and a step of zero crashes with status 82 instead of looping forever.</summary>
    [Fact]
    public void RangeStepZero_CrashesLoudly()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "range_step_zero.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "step 10", actualString: stdout);
        Assert.Contains(expectedSubstring: "step 1", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Range step zero error", actualString: stdout + stderr);
    }

    /// <summary>A thread reading a channel whose sender its own scope still holds, with no other work running, crashes
    /// with a deadlock report instead of hanging.</summary>
    [Fact]
    public void ChannelSelfWait_CrashesLoudly()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "channel_self_wait.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "got 1", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Deadlock error", actualString: stdout + stderr);
    }

    /// <summary><c>steal</c> on a handle that another strong handle still shares crashes with status 82.</summary>
    [Fact]
    public void SharedHandleSteal_CrashesLoudly()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "shared_handle_steal.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "before", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "2 handles share it", actualString: stdout + stderr);
    }

    /// <summary>A caught error thrown again crashes with the trace it was first thrown with: the routines between
    /// the first throw and the catcher had returned by then, and the report still names them, the first throw's
    /// line, and where the error was thrown again.</summary>
    [Fact]
    public void RethrownError_KeepsTheTraceOfItsFirstThrow()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "rethrown_keeps_trace.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "ok: 2", actualString: stdout);
        Assert.Contains(expectedSubstring: "caught: deep failure: n too big", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        string report = stdout + stderr;
        Assert.Contains(expectedSubstring: "innermost!(Core.S64) (", actualString: report);
        Assert.Contains(expectedSubstring: "rethrown_keeps_trace.rf:16:9)", actualString: report);
        Assert.Contains(expectedSubstring: "middle!(Core.S64) (", actualString: report);
        Assert.Contains(expectedSubstring: "Thrown again at", actualString: report);
        Assert.Contains(expectedSubstring: "rethrown_keeps_trace.rf:27:13", actualString: report);
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
