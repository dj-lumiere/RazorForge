using System.Diagnostics;
using System.Text;

namespace RazorForge.Tests;

/// <summary>
/// What a hung child process was doing when a test gave up on it: the process tree under it (the builder,
/// or the opt/clang/linker or compiled program it started) and the thread stacks of each process. Taken
/// just before the test kills the tree, so an intermittent hang leaves its location in the CI log.
/// Managed stacks come from <c>dotnet-stack</c> and native ones from <c>gdb</c> (Linux); a tool that is
/// not installed is skipped, never failing the test for it.
/// </summary>
internal static class HangDiagnostics
{
    private const int ToolTimeoutMs = 60_000;

    /// <summary>The report for <paramref name="root"/> and every process under it.</summary>
    public static string Capture(Process root)
    {
        var report = new StringBuilder();
        report.AppendLine(value: "--- hang diagnostics ---");
        List<(int Pid, string Command)> tree;
        try
        {
            tree = ProcessTree(rootPid: root.Id);
        }
        catch (Exception ex)
        {
            report.AppendLine(value: $"(could not list the process tree: {ex.Message})");
            tree = [(root.Id, "dotnet")];
        }

        foreach ((int pid, string command) in tree)
        {
            report.AppendLine(value: $"[{pid}] {command}");
        }

        foreach ((int pid, string command) in tree)
        {
            bool managed = command.Contains(value: "dotnet", comparisonType: StringComparison.OrdinalIgnoreCase);
            report.AppendLine(value: $"--- stacks of [{pid}] ---");
            report.AppendLine(value: managed
                ? Run(file: "dotnet-stack", arguments: ["report", "--process-id", pid.ToString()])
                : OperatingSystem.IsLinux()
                    ? Run(file: "gdb", arguments: ["-batch", "-p", pid.ToString(), "-ex", "thread apply all bt"])
                    : "(native stacks are only taken on Linux)");
        }

        return report.ToString();
    }

    /// <summary>The root process and its descendants, each with its command line, root first.</summary>
    private static List<(int Pid, string Command)> ProcessTree(int rootPid)
    {
        var all = new List<(int Pid, int Parent, string Command)>();
        if (OperatingSystem.IsWindows())
        {
            string listing = Run(file: "powershell",
                arguments:
                [
                    "-NoProfile", "-Command",
                    "Get-CimInstance Win32_Process | ForEach-Object { \"$($_.ProcessId)`t$($_.ParentProcessId)`t$($_.CommandLine)\" }"
                ]);
            foreach (string line in listing.Split(separator: '\n'))
            {
                string[] parts = line.TrimEnd(trimChar: '\r').Split(separator: '\t', count: 3);
                if (parts.Length == 3 && int.TryParse(s: parts[0], result: out int pid) &&
                    int.TryParse(s: parts[1], result: out int parent))
                {
                    all.Add(item: (pid, parent, parts[2]));
                }
            }
        }
        else
        {
            string listing = Run(file: "ps", arguments: ["-A", "-o", "pid=,ppid=,args="]);
            foreach (string line in listing.Split(separator: '\n'))
            {
                string[] parts = line.Trim().Split(separator: ' ', count: 3,
                    options: StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(s: parts[0], result: out int pid) &&
                    int.TryParse(s: parts[1], result: out int parent))
                {
                    all.Add(item: (pid, parent, parts.Length == 3 ? parts[2] : ""));
                }
            }
        }

        var tree = new List<(int Pid, string Command)>();
        var queue = new Queue<int>(collection: [rootPid]);
        var seen = new HashSet<int>();
        while (queue.Count > 0)
        {
            int pid = queue.Dequeue();
            if (!seen.Add(item: pid))
            {
                continue;
            }

            string command = all.FirstOrDefault(predicate: p => p.Pid == pid).Command ?? "";
            tree.Add(item: (pid, command));
            foreach ((int child, int _, string _) in all.Where(predicate: p => p.Parent == pid))
            {
                queue.Enqueue(item: child);
            }
        }

        return tree;
    }

    /// <summary>The combined output of a diagnostic tool, or why it could not run.</summary>
    private static string Run(string file, string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = file, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in arguments)
            {
                psi.ArgumentList.Add(item: argument);
            }

            using Process tool = Process.Start(startInfo: psi)!;
            Task<string> stdout = tool.StandardOutput.ReadToEndAsync();
            Task<string> stderr = tool.StandardError.ReadToEndAsync();
            if (!tool.WaitForExit(milliseconds: ToolTimeoutMs))
            {
                try { tool.Kill(entireProcessTree: true); }
                catch
                {
                    /* best effort */
                }

                return $"({file} did not finish within {ToolTimeoutMs / 1000} s)";
            }

            return stdout.Result + stderr.Result;
        }
        catch (Exception ex)
        {
            return $"({file} is not available: {ex.Message})";
        }
    }
}
