using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace RazorForge.Tests.Execution;

/// <summary>
/// End-to-end runs of the <c>mode = "debug-jit"</c> dev loop through <c>buildandrun</c>: the builder compiles the
/// program and runs it in its own process through LLVM's ORC JIT instead of linking an executable. Each test writes a
/// small project (a <c>config.toml</c> and one program) to a temporary folder and runs the toolchain on it as a child
/// process, so a JIT'd program's runtime threads and exit never touch the test host. The three tests cover the three
/// ways the dev loop runs a program: one whole module, the incremental lazy JIT with its per-routine IR cache, and the
/// warm daemon's base/delta split (the stdlib compiled once to an object, only the program's delta JIT'd).
/// The JIT has to start on every platform, macOS included: the build makes the macOS libLLVM load without Homebrew's
/// z3 and zstd (Anvila/PatchMacLibLlvm.targets), so a run that falls back to an AOT build fails these tests.
/// </summary>
public sealed partial class DebugJitRunTests
{
    /// <summary>A program that reaches common stdlib code (a list, a map, text formatting) and a routine of its own,
    /// so a run links program code against stdlib code.</summary>
    private const string Program = """
                                   module JitProbe
                                   import IO/Console

                                   routine total(upto: S64) -> S64
                                       var sum: S64 = 0
                                       each i in 1 to upto
                                           sum += i
                                       return sum

                                   routine start()
                                       var names = ["alpha", "beta"]
                                       names.add_last(value: "gamma")
                                       var d = Dict[Text, S64]()
                                       d["one"] = 1
                                       show(f"jit total {total(upto: 10)} names {names.count()} has one {d.has_key("one")}")
                                       return
                                   """;

    private const string ExpectedLine = "jit total 55 names 3 has one true";

    [GeneratedRegex(pattern: @"incremental JIT \(cache: (\d+) reused, (\d+) fresh")]
    private static partial Regex IncrementalCacheRegex();

    [GeneratedRegex(pattern: @"started one \(pid (\d+)")]
    private static partial Regex SpawnedDaemonPidRegex();

    /// <summary>Without <c>incremental</c> the whole program is one module, JIT'd and run.</summary>
    [Fact]
    public void WholeModule_RunsTheProgram()
    {
        using var project = new JitProject(incremental: false, useDaemon: false);
        Run run = project.BuildAndRun();

        AssertRan(run: run);
        Assert.Contains(expectedSubstring: "[timing] JIT compile + run:", actualString: run.Stderr);
    }

    /// <summary>With <c>incremental</c> and no daemon, each routine is built when the running program first reaches
    /// it, and its IR is kept in the per-routine cache: a second run of the same program builds none of them again.</summary>
    [Fact]
    public void IncrementalWithoutDaemon_SecondRunReusesEveryRoutine()
    {
        using var project = new JitProject(incremental: true, useDaemon: false);
        Run first = project.BuildAndRun();
        AssertRan(run: first);
        Run second = project.BuildAndRun();
        AssertRan(run: second);
        Assert.Matches(expectedRegex: IncrementalCacheRegex(), actualString: first.Stderr);
        Match counts = IncrementalCacheRegex().Match(input: second.Stderr);
        Assert.True(condition: counts.Success, userMessage: $"no cache line in the second run\n{second.Stderr}");
        int reused = int.Parse(s: counts.Groups[groupnum: 1].Value);
        int fresh = int.Parse(s: counts.Groups[groupnum: 2].Value);
        Assert.True(condition: reused > 0 && fresh == 0,
            userMessage: $"the second run should take every routine from the cache, got {reused} reused, {fresh} fresh");
    }

    /// <summary>The default dev loop (<c>incremental</c> with <c>use-daemon</c>): the first run starts a daemon on a
    /// pipe of this test's own, which compiles the stdlib base to an object once. Each run JITs only the program's
    /// delta against that object, and the second run uses the daemon the first one started.</summary>
    [Fact]
    public void DaemonBaseDelta_RunsTheProgramTwice()
    {
        using var project = new JitProject(incremental: true, useDaemon: true);
        try
        {
            Run first = project.BuildAndRun();
            AssertRan(run: first);
            Run second = project.BuildAndRun();
            AssertRan(run: second);
            Assert.Contains(expectedSubstring: "(daemon IR, base/delta)", actualString: first.Stderr);
            Assert.DoesNotContain(expectedSubstring: "resident-JIT base seed", actualString: first.Stderr);
            Assert.Contains(expectedSubstring: "(daemon IR, base/delta)", actualString: second.Stderr);
            Assert.DoesNotContain(expectedSubstring: "no daemon running", actualString: second.Stderr);
        }
        finally
        {
            project.StopDaemon();
        }
    }

    private static void AssertRan(Run run)
    {
        string all = $"exit {run.Exit}\n--- stdout ---\n{run.Stdout}\n--- stderr ---\n{run.Stderr}";
        Assert.True(condition: run.Exit == 0, userMessage: all);
        Assert.Contains(expectedSubstring: ExpectedLine, actualString: run.Stdout);
        Assert.DoesNotContain(expectedSubstring: "falling back to AOT", actualString: run.Stderr);
    }

    private sealed record Run(int Exit, string Stdout, string Stderr);

    /// <summary>A temporary project folder with a <c>config.toml</c> in <c>debug-jit</c> mode and the probe program.
    /// With <c>use-daemon</c> it names a daemon pipe of its own, so it never reaches a developer's running daemon.</summary>
    private sealed class JitProject : IDisposable
    {
        private readonly string _dir;
        private readonly string _entry;
        private readonly bool _useDaemon;
        private readonly string _pipe;
        private readonly HashSet<int> _daemonPids = [];

        public JitProject(bool incremental, bool useDaemon)
        {
            _useDaemon = useDaemon;
            _pipe = "rf-jit-test-" + Guid.NewGuid().ToString(format: "N");
            _dir = Path.Combine(path1: Path.GetTempPath(), path2: _pipe);
            Directory.CreateDirectory(path: _dir);
            _entry = Path.Combine(path1: _dir, path2: "probe.rf");
            File.WriteAllText(path: _entry, contents: Program);
            File.WriteAllText(path: Path.Combine(path1: _dir, path2: "config.toml"),
                contents: $"""
                           [package]
                           name = "jitprobe"

                           [target]
                           executable = "probe.rf"
                           mode = "debug-jit"
                           incremental = {(incremental ? "true" : "false")}
                           use-daemon = {(useDaemon ? "true" : "false")}

                           [debug]
                           timing = true
                           """);
        }

        public Run BuildAndRun()
        {
            Run run = RunToolchain(verb: "buildandrun", withEntry: true);
            foreach (Match m in SpawnedDaemonPidRegex().Matches(input: run.Stderr))
            {
                _daemonPids.Add(item: int.Parse(s: m.Groups[groupnum: 1].Value));
            }

            return run;
        }

        /// <summary>Asks this project's daemon to stop, and kills any daemon it started that is still running after.</summary>
        public void StopDaemon()
        {
            if (!_useDaemon)
            {
                return;
            }

            try
            {
                RunToolchain(verb: "daemon-stop", withEntry: false);
            }
            catch (Exception)
            {
                // The kill below still ends the daemon.
            }

            foreach (int pid in _daemonPids)
            {
                try
                {
                    using var daemon = Process.GetProcessById(processId: pid);
                    if (!daemon.WaitForExit(milliseconds: 10_000))
                    {
                        daemon.Kill(entireProcessTree: true);
                    }
                }
                catch (ArgumentException)
                {
                    // Already gone.
                }
                catch (InvalidOperationException)
                {
                    // Already gone.
                }
            }

            TryDelete(path: Path.Combine(path1: Path.GetTempPath(), path2: _pipe + ".log"));
        }

        /// <summary>Runs the toolchain and returns once it exits and its output is read.</summary>
        private Run RunToolchain(string verb, bool withEntry)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                WorkingDirectory = _dir,
                Environment =
                {
                    [key: "DOTNET_gcServer"] = "0",
                    [key: "DOTNET_GCConserveMemory"] = "9",
                    [key: "RAZORFORGE_DAEMON_PIPE"] = _pipe,
                    // Warming the daemon and building its base run beside the rest of the suite, and on CI under
                    // coverage, so the client waits as long as the suite's other child limits allow.
                    [key: "RAZORFORGE_DAEMON_WAIT_SECONDS"] =
                        (TestHelpers.ChildTimeoutMs(baseMs: 120_000) / 1000).ToString(
                            provider: System.Globalization.CultureInfo.InvariantCulture)
                }
            };
            psi.ArgumentList.Add(item: TestHelpers.ToolchainDll(sourcePath: _entry));
            psi.ArgumentList.Add(item: verb);
            if (withEntry)
            {
                psi.ArgumentList.Add(item: _entry);
            }

            if (_useDaemon)
            {
                psi.Environment.Remove(key: "RF_NO_DAEMON");
            }
            else
            {
                psi.Environment[key: "RF_NO_DAEMON"] = "1";
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            using var p = new Process();
            p.StartInfo = psi;
            p.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    lock (stdout)
                    {
                        stdout.AppendLine(value: e.Data);
                    }
                }
            };
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    lock (stderr)
                    {
                        stderr.AppendLine(value: e.Data);
                    }
                }
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            if (!p.WaitForExit(milliseconds: TestHelpers.ChildTimeoutMs(baseMs: 300_000)))
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Exited meanwhile.
                }

                Assert.Fail(message: $"{verb} did not finish.\n{stderr}");
            }

            if (_useDaemon && withEntry)
            {
                // A daemon this run started holds the output pipes open, so their end never comes. Wait for the
                // run's last lines instead: the program's output and the client's JIT timing line.
                SpinWait.SpinUntil(condition: () =>
                {
                    lock (stdout)
                    lock (stderr)
                    {
                        return stdout.ToString().Contains(value: ExpectedLine) &&
                               stderr.ToString().Contains(value: "[timing] JIT compile + run");
                    }
                }, millisecondsTimeout: 10_000);
            }
            else
            {
                // Waits for the reader threads to reach the end of both pipes.
                p.WaitForExit();
            }

            lock (stdout)
            lock (stderr)
            {
                return new Run(Exit: p.ExitCode, Stdout: stdout.ToString(), Stderr: stderr.ToString());
            }
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(path: _dir, recursive: true);
            }
            catch (IOException)
            {
                // A temporary folder left behind is harmless.
            }
            catch (UnauthorizedAccessException)
            {
                // A temporary folder left behind is harmless.
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path: path);
            }
            catch (IOException)
            {
                // A log left behind is harmless.
            }
            catch (UnauthorizedAccessException)
            {
                // A log left behind is harmless.
            }
        }
    }
}
