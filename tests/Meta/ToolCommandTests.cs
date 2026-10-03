using System.Diagnostics;
using System.Text;

namespace RazorForge.Tests.Meta;

/// <summary>
/// The tool as a user runs it, end to end: <c>run</c>, <c>test</c> and <c>lint</c>, and a crash out of a
/// <c>when</c> on a carrier that leaves the error unhandled.
/// </summary>
public sealed class ToolCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(path1: Path.GetTempPath(),
        path2: "rf-tool-" + Guid.NewGuid().ToString(format: "N"));

    public ToolCommandTests()
    {
        Directory.CreateDirectory(path: _dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(path: _dir, recursive: true); }
        catch
        {
            /* best effort */
        }
    }

    private const string Nope = """
                                crashable NopeError
                                    what: Text

                                routine NopeError.crash_message() -> Text
                                    return f"nope: {me.what}"

                                routine fails!(n: S64) -> S64
                                    if n > 0
                                        throw NopeError(what: f"too big {n}")
                                    return n

                                """;

    /// <summary>A <c>when</c> on a <c>Check</c> with no error arm, outside a failable routine, crashes loudly with
    /// the caught error's title and message.</summary>
    [Fact]
    public void UnhandledErrorArm_OutsideAFailableRoutine_Crashes()
    {
        string program = Write(name: "crash.rf", source: "module ToolCrash\nimport IO/Console\n\n" + Nope + """
            routine start()
                when grab fails(n: 1)
                    is S64 v =>
                        show(f"never {v}")
                show("not reached")
                return
            """);

        (int exit, string stdout, string stderr) = Tool("run", program);

        Assert.Equal(expected: 82, actual: exit);
        Assert.DoesNotContain(expectedSubstring: "not reached", actualString: stdout);
        Assert.Contains(expectedSubstring: "Nope error: nope: too big 1", actualString: stdout + stderr);
    }

    /// <summary><c>test</c> passes a program whose output matches its <c>.expected</c>, fails one that does not,
    /// and passes one whose build must fail with its <c>.error</c> text.</summary>
    [Fact]
    public void Test_ComparesEachProgramWithItsExpectations()
    {
        string tests = Path.Combine(path1: _dir, path2: "tests");
        Directory.CreateDirectory(path: tests);
        Write(name: Path.Combine(path1: "tests", path2: "good.rf"),
            source: "module ToolGood\nimport IO/Console\n\nroutine start()\n    show(\"hi\")\n    return\n");
        Write(name: Path.Combine(path1: "tests", path2: "good.expected"), source: "hi\n");
        Write(name: Path.Combine(path1: "tests", path2: "wrong.rf"),
            source: "module ToolWrong\nimport IO/Console\n\nroutine start()\n    show(\"bye\")\n    return\n");
        Write(name: Path.Combine(path1: "tests", path2: "wrong.expected"), source: "hi\n");
        Write(name: Path.Combine(path1: "tests", path2: "broken.rf"),
            source: "module ToolBroken\n\nroutine start()\n    var x: S64 = \"no\"\n    return\n");
        Write(name: Path.Combine(path1: "tests", path2: "broken.error"), source: "RF-S");

        (int exit, string stdout, _) = Tool("test", tests);

        Assert.Equal(expected: 1, actual: exit);
        Assert.Contains(expectedSubstring: "ok    good", actualString: stdout);
        Assert.Contains(expectedSubstring: "ok    broken", actualString: stdout);
        Assert.Contains(expectedSubstring: "FAIL  wrong", actualString: stdout);
        Assert.Contains(expectedSubstring: "2 passed, 1 failed", actualString: stdout);
    }

    /// <summary><c>lint</c> reports a file that is not in the canonical layout and passes one that is.</summary>
    [Fact]
    public void Lint_ReportsStyleErrors()
    {
        string messy = Write(name: "messy.rf",
            source: "module ToolMessy\n\nimport IO/Console\nroutine start()\n    show( \"hi\" )\n    return\n");
        string tidy = Write(name: "tidy.rf",
            source: "module ToolTidy\n\nimport IO/Console\nroutine start()\n    show(\"hi\")\n    return\n");

        (int messyExit, string messyOut, _) = Tool("lint", messy);
        (int tidyExit, string tidyOut, _) = Tool("lint", tidy);

        Assert.Equal(expected: 1, actual: messyExit);
        Assert.Contains(expectedSubstring: "error[style]", actualString: messyOut);
        Assert.Contains(expectedSubstring: "show(\"hi\")", actualString: messyOut);
        Assert.True(condition: tidyExit == 0, userMessage: tidyOut);
    }

    private string Write(string name, string source)
    {
        string path = Path.Combine(path1: _dir, path2: name);
        File.WriteAllText(path: path, contents: source);
        return path;
    }

    private (int Exit, string Stdout, string Stderr) Tool(params string[] args)
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
            Environment = { [key: "RF_NO_DAEMON"] = "1" }
        };
        psi.ArgumentList.Add(item: TestHelpers.ToolchainDll(sourcePath: Path.Combine(path1: _dir, path2: "x.rf")));
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(item: arg);
        }

        using Process p = Process.Start(startInfo: psi)!;
        Task<string> stdout = p.StandardOutput.ReadToEndAsync();
        Task<string> stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(milliseconds: 600_000))
        {
            p.Kill(entireProcessTree: true);
            Assert.Fail(message: $"'{string.Join(separator: ' ', values: args)}' did not finish");
        }

        return (p.ExitCode, stdout.Result, stderr.Result);
    }
}
