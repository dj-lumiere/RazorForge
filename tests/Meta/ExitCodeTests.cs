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

    /// <summary>A throw in a routine that is not failable crashes where it stands with its crashable's message (status
    /// 82), even when a failable routine calls it under <c>try</c>, after the same <c>try</c> received the failable
    /// routine's own throw.</summary>
    [Fact]
    public void GlobalReadByAnotherGlobalsDestruct_IsTornDownAfterIt()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "global_destruct_order.rf");

        Assert.True(condition: exit == 0,
            userMessage: $"expected exit 0, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        int reporter = stdout.IndexOf(value: "reporter 7 reads the log: 3 lines, first a line long enough",
            comparisonType: StringComparison.Ordinal);
        int log = stdout.IndexOf(value: "log destruct with 3 lines", comparisonType: StringComparison.Ordinal);
        Assert.True(condition: reporter >= 0 && log > reporter,
            userMessage: $"expected the reporter torn down before the log\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
    }

    [Fact]
    public void ReserveSizePastU64_CrashesInsteadOfWrapping()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "reserve_size_overflow.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "reserving", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Integer overflow error", actualString: stdout + stderr);
    }

    [Fact]
    public void ThrowInPlainRoutineUnderTry_CrashesWithItsMessage()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "throw_in_plain_under_try.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "recovered throw: none", actualString: stdout);
        Assert.Contains(expectedSubstring: "value: 6", actualString: stdout);
        Assert.Contains(expectedSubstring: "breaking", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Broken invariant error: the ledger no longer balances",
            actualString: stdout + stderr);
    }

    /// <summary>A failure no keyword receives crashes with status 82 and its own message, after a <c>check</c> on the
    /// same flow kept an earlier failure: the failure mold holds the new failure, and the kept <c>Check</c> its own.</summary>
    [Fact]
    public void BareFailureAfterKeptCheck_CrashesWithItsOwnMessage()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "mold_bare_crash.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "kept: the code was 1", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "the code was 9", actualString: stdout + stderr);
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

    /// <summary>Failures rise only through failable routines: <c>try</c> receives a lambda's failure handed through a
    /// <c>failable Callable</c> parameter and a failable routine's throw, but a division by zero inside a routine that
    /// is not failable crashes there (status 82), even under <c>try</c>.</summary>
    [Fact]
    public void FailureInsidePlainHelperUnderTry_CrashesLoudly()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "wall_under_try.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "recovered lambda: none", actualString: stdout);
        Assert.Contains(expectedSubstring: "value: 25", actualString: stdout);
        Assert.Contains(expectedSubstring: "recovered throw: none", actualString: stdout);
        Assert.Contains(expectedSubstring: "split: 5", actualString: stdout);
        Assert.Contains(expectedSubstring: "dividing by zero", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Division by zero error", actualString: stdout + stderr);
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

    /// <summary>Calls deeper than the main thread's stack holds crash with the stack overflow report and status 82
    /// (the fault handler runs on a stack of its own), not with a bare segmentation fault.</summary>
    [Fact]
    public void StackOverflow_MainThread_CrashesLoudly()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "stack_overflow_main.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "shallow 100", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Stack overflow error", actualString: stdout + stderr);
    }

    /// <summary>The same in a Work: the coroutine's stack runs out on a worker thread, which reports it too.</summary>
    [Fact]
    public void StackOverflow_InWork_CrashesLoudly()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "stack_overflow.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "work [100]", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Stack overflow error", actualString: stdout + stderr);
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

    /// <summary>A routine called inside a <c>modifying</c> block that opens the same <c>Guarded</c> again crashes with
    /// <c>ReenterDuringAccessError</c> (status 82) instead of waiting for itself.</summary>
    [Fact]
    public void AccessReenter_CrashesLoudly()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "access_reenter.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "opened", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Reenter during access error", actualString: stdout + stderr);
    }

    /// <summary>Two jobs that open two <c>Guarded</c> handles in opposite orders wait on each other: a debug build's
    /// deadlock check stops the program with a <c>DeadlockError</c> (status 82) that names both, instead of hanging.</summary>
    [Fact]
    public void AccessDeadlock_CrashesLoudly()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "access_deadlock.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "starting", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Deadlock error: These pieces of work wait on each other for access",
            actualString: stdout + stderr);
        Assert.Contains(expectedSubstring: "access_deadlock.rf:12", actualString: stdout + stderr);
    }

    /// <summary>A block whose two targets are the same <c>Guarded</c> under two names enters the first, and entering
    /// the second crashes with <c>ReenterDuringAccessError</c> (status 82) instead of waiting for itself.</summary>
    [Fact]
    public void AccessAliased_CrashesLoudly()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "access_aliased.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "moved", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Reenter during access error", actualString: stdout + stderr);
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
        Assert.Contains(expectedSubstring: "innermost(System.S64) (", actualString: report);
        Assert.Contains(expectedSubstring: "rethrown_keeps_trace.rf:16:9)", actualString: report);
        Assert.Contains(expectedSubstring: "middle(System.S64) (", actualString: report);
        Assert.Contains(expectedSubstring: "Thrown again at", actualString: report);
        Assert.Contains(expectedSubstring: "rethrown_keeps_trace.rf:27:13", actualString: report);
    }

    /// <summary>An error in a Work crashes where the Work is retrieved. The report names the work's
    /// frames, the Work by the routine it was made in (not the routines the builder wrote to run it), and the
    /// program's own `retrieve()` line as where the error was thrown again (not the standard library's).</summary>
    [Fact]
    public void WorkCrash_ReportsTheProgramsOwnPlaces()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "work_crash_report.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        string report = stdout + stderr;
        Assert.Contains(expectedSubstring: "boom(System.S64) (", actualString: report);
        Assert.Contains(expectedSubstring: "work in Tests/ExitCode/WorkCrashReport.start() (", actualString: report);
        Assert.DoesNotContain(expectedSubstring: "__work", actualString: report);
        Assert.Contains(expectedSubstring: "Thrown again at", actualString: report);
        Assert.Contains(expectedSubstring: "work_crash_report.rf:16:13", actualString: report);
    }

    /// <summary>A started Work nobody retrieves is cancelled and waited for at the end of its scope. A failure its
    /// work came to crashes there with status 82 and the failure's message (safety matrix C45, rf-fail-job-dropped):
    /// it used to vanish with only RF-W009.</summary>
    [Fact]
    public void DroppedWorkFailure_CrashesLoudly()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "work_dropped_failure.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "starting", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Verification failed error", actualString: stdout + stderr);
    }

    /// <summary>A Work the program cancelled before dropping it throws away what it came to, a failure included.</summary>
    [Fact]
    public void CancelledWorkDrop_DropsItsFailure()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "work_cancelled_drop.rf");

        Assert.True(condition: exit == 0,
            userMessage: $"expected exit 0, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "after the cancelled work", actualString: stdout);
    }

    /// <summary><c>destruct</c> is never failable, so a failure inside it crashes with status 82 even beneath
    /// <c>check</c> (safety matrix C45, rf-fail-destruct-callee).</summary>
    [Fact]
    public void FailureInsideDestruct_CrashesEvenUnderCheck()
    {
        (int exit, string stdout, string stderr) = RunFixture(fixture: "destruct_failure_under_check.rf");

        Assert.True(condition: exit == 82,
            userMessage: $"expected exit 82, got {exit}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        Assert.Contains(expectedSubstring: "making", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "recovered", actualString: stdout);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Verification failed error", actualString: stdout + stderr);
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
        if (!p.WaitForExit(milliseconds: TestHelpers.ChildTimeoutMs(baseMs: 300_000)))
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
