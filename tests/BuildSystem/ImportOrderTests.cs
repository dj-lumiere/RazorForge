using System.Diagnostics;
using System.Text;

namespace Revare.Tests.BuildSystem;

/// <summary>
/// The order of a file's imports never changes what a program means. Every test builds one project several times with
/// the same import lines in different orders (identity, reversed, rotated, shuffled) and requires the same result:
/// the same program output, or the same build error text.
/// </summary>
public sealed class ImportOrderTests
{
    private static readonly Dictionary<string, string> Modules = new()
    {
        [key: "config.toml"] = "[package]\nname = \"imports\"\nversion = \"0.0.1\"\n\n[target]\nexecutable = \"App\"\nmode = \"debug\"\n",
        [key: "Base.rvr"] = "module Base\n\nentity Counter\n    n: S64\n",
        // Adds a routine to a type of another module: it is seen where Ext is imported.
        [key: "Ext.rvr"] = "module Ext\n\nimport Base\n\nroutine Counter.plus_two() -> S64\n    return me.n + 2\n",
        [key: "A.rvr"] = "module A\n\nrecord Thing\n    v: S64\n\nroutine twin() -> S64\n    return 1\n\n" +
                        "routine describe() -> S64\n    return 10\n\nroutine pick(x: S64) -> S64\n    return x + 100\n",
        [key: "B.rvr"] = "module B\n\nroutine twin() -> S64\n    return 2\n\n" +
                        "routine describe() -> S64\n    return 20\n\nroutine pick(x: Text) -> S64\n    return 7\n",
        [key: "C.rvr"] = "module C\n\nrecord Gizmo\n    w: S64\n\nroutine gadget() -> S64\n    return 5\n\n" +
                        "routine twin() -> S64\n    return 6\n",
        [key: "D.rvr"] = "module D\n\nroutine describe() -> S64\n    return 30\n\nroutine only_d() -> S64\n    return 4\n",
        [key: "E.rvr"] = "module E\n\nroutine thing() -> S64\n    return 8\n"
    };

    // Whole-module imports, member imports (one name, several names, with an alias) and a module alias.
    private static readonly string[] Imports =
    [
        "import Base", "import Ext", "import A", "import B", "import C.[gadget, Gizmo]", "import E.thing as e_thing",
        "import D", "import IO/Console"
    ];

    private const string ModuleBody = """
                                      routine describe() -> S64
                                          return 99

                                      routine start()
                                          var c = Counter(n: 5)
                                          var g = Gizmo(w: 9)
                                          show(f"{A.twin()} {B.twin()} {C.twin()} {B.describe()} {A.describe()} {D.describe()}")
                                          show(f"{describe()} {only_d()} {g.w} {gadget()} {e_thing()} {E.thing()}")
                                          show(f"{c.plus_two()} {A.pick(x: 1)} {B.pick(x: "s")}")
                                          return
                                      """;

    private const string ExpectedOutput = "1 2 6 20 10 30\n99 4 9 5 8 8\n7 101 7\n";

    /// <summary>A module-qualified call works in every order, the file's own <c>describe</c> wins over the three
    /// imported ones, and the routine <c>Ext</c> adds to <c>Counter</c> is seen in every order.</summary>
    [Fact]
    public void ModuleFile_EveryImportOrder_RunsTheSame()
    {
        RunOrders(imports: Imports,
            source: order => "module App\n\n" + string.Join(separator: "\n", values: order) + "\n\n" + ModuleBody + "\n",
            verb: "run",
            assertResult: (exit, text) =>
            {
                Assert.True(condition: exit == 0, userMessage: text);
                Assert.Equal(expected: ExpectedOutput, actual: text);
            });
    }

    /// <summary>A script may write its imports among its statements, and a statement may use a name whose import
    /// comes later: the imports are one set, whatever the lines around them are.</summary>
    [Fact]
    public void Script_ImportsAmongStatements_EveryOrder_RunsTheSame()
    {
        string[] mixed = Imports.Where(predicate: i => i != "import IO/Console").ToArray();
        RunOrders(imports: mixed,
            source: order =>
            {
                var sb = new StringBuilder();
                sb.Append(value: "import IO/Console\n");
                sb.Append(value: $"{order[0]}\nshow(\"start\")\n{order[1]}\nvar c = Counter(n: 5)\n{order[2]}\n");
                sb.Append(value: $"var g = Gizmo(w: 9)\n{order[3]}\n{order[4]}\nvar total = c.plus_two() + g.w\n");
                sb.Append(value: $"{order[5]}\n{order[6]}\n");
                sb.Append(value: "routine describe() -> S64\n    return 99\n\n");
                sb.Append(value: "show(f\"{A.twin()} {B.twin()} {C.twin()} {B.describe()} {A.describe()} {D.describe()}\")\n");
                sb.Append(value: "show(f\"{describe()} {only_d()} {g.w} {gadget()} {e_thing()} {E.thing()}\")\n");
                sb.Append(value: "var word = \"s\"\nshow(f\"{total} {A.pick(x: 1)} {B.pick(x: word)}\")\n");
                return sb.ToString();
            },
            verb: "run",
            assertResult: (exit, text) =>
            {
                Assert.True(condition: exit == 0, userMessage: text);
                Assert.Equal(expected: "start\n1 2 6 20 10 30\n99 4 9 5 8 8\n16 101 7\n", actual: text);
            });
    }

    /// <summary>A name two imports both offer, used bare, is the same build error in every order, whether it is a
    /// routine with one signature or overloads that differ by argument type, and it names the modules the same.</summary>
    [Fact]
    public void BareNameInTwoImports_EveryOrder_IsTheSameBuildError()
    {
        RunOrders(imports: Imports,
            source: order => "module App\n\n" + string.Join(separator: "\n", values: order) + "\n\n" +
                             "routine start()\n    show(f\"{twin()}\")\n    show(f\"{pick(x: 1)}\")\n    return\n",
            verb: "check",
            assertResult: (exit, text) =>
            {
                Assert.NotEqual(expected: 0, actual: exit);
                Assert.Contains(expectedSubstring: "RV-S513", actualString: text);
                Assert.Contains(expectedSubstring: "'twin' is in", actualString: text);
                Assert.Contains(expectedSubstring: "'pick' is in", actualString: text);
            });
    }

    /// <summary>The own module wins over its imports in every order, and the same name read through a member import
    /// narrowed to one module stays that module's.</summary>
    [Fact]
    public void OwnModuleAndMemberImport_EveryOrder_PickTheSameRoutine()
    {
        string[] imports = ["import A", "import B", "import D", "import C.twin as c_twin", "import IO/Console"];
        RunOrders(imports: imports,
            source: order => "module App\n\n" + string.Join(separator: "\n", values: order) + "\n\n" +
                             "routine describe() -> S64\n    return 99\n\n" +
                             "routine start()\n    show(f\"{describe()} {c_twin()} {C.twin()}\")\n    return\n",
            verb: "run",
            assertResult: (exit, text) =>
            {
                Assert.True(condition: exit == 0, userMessage: text);
                Assert.Equal(expected: "99 6 6\n", actual: text);
            });
    }

    #region Harness

    /// <summary>Builds the project once per order of <paramref name="imports"/> and requires the same exit status and
    /// text from all of them, then hands the first result to <paramref name="assertResult"/>.</summary>
    private static void RunOrders(string[] imports, Func<string[], string> source, string verb,
        Action<int, string> assertResult)
    {
        var results = new List<(string Order, int Exit, string Text)>();
        foreach (string[] order in Orders(items: imports))
        {
            string dir = Path.Combine(path1: Path.GetTempPath(), path2: "rf-import-order-" + Guid.NewGuid().ToString(format: "N"));
            Directory.CreateDirectory(path: dir);
            try
            {
                foreach ((string name, string text) in Modules)
                {
                    File.WriteAllText(path: Path.Combine(path1: dir, path2: name), contents: text);
                }

                File.WriteAllText(path: Path.Combine(path1: dir, path2: "App.rvr"), contents: source(arg: order));
                (int exit, string output) = Run(verb: verb, dir: dir);
                results.Add(item: (string.Join(separator: " | ", value: order), exit, Normalize(text: output, dir: dir)));
            }
            finally
            {
                try { Directory.Delete(path: dir, recursive: true); }
                catch
                {
                    /* best-effort temp cleanup */
                }
            }
        }

        foreach ((string order, int exit, string text) in results)
        {
            Assert.True(condition: exit == results[index: 0].Exit && text == results[index: 0].Text,
                userMessage: $"order [{order}] differs from [{results[index: 0].Order}]\n--- first ---\n" +
                             $"{results[index: 0].Text}\n--- this ---\n{text}");
        }

        assertResult(arg1: results[index: 0].Exit, arg2: results[index: 0].Text);
    }

    /// <summary>The order itself, the reverse, a rotation and a fixed shuffle (the same four on every run).</summary>
    private static IEnumerable<string[]> Orders(string[] items)
    {
        var seen = new HashSet<string>();
        var shuffled = (string[])items.Clone();
        var random = new Random(Seed: 7);
        for (int i = shuffled.Length - 1; i > 0; i--)
        {
            int j = random.Next(maxValue: i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        string[][] candidates =
        [
            items, items.Reverse().ToArray(), items.Skip(count: 3).Concat(second: items.Take(count: 3)).ToArray(), shuffled
        ];
        foreach (string[] candidate in candidates)
        {
            if (seen.Add(item: string.Join(separator: "\n", values: candidate)))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>What the build printed, with the temp folder and the build's own timing removed.</summary>
    private static string Normalize(string text, string dir)
    {
        return text.Replace(oldValue: dir, newValue: "<dir>").Replace(oldValue: "\r\n", newValue: "\n");
    }

    private static (int Exit, string Output) Run(string verb, string dir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { TestHelpers.ToolchainDll(sourcePath: dir), verb, Path.Combine(path1: dir, path2: "App.rvr") },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = dir
        };
        using var p = Process.Start(startInfo: psi)!;
        Task<string> outTask = p.StandardOutput.ReadToEndAsync();
        Task<string> errTask = p.StandardError.ReadToEndAsync();
        int timeoutMs = TestHelpers.ChildTimeoutMs(baseMs: 300_000);
        if (!p.WaitForExit(milliseconds: timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); }
            catch
            {
                /* best effort */
            }

            Assert.Fail(message: $"{verb} did not finish in {timeoutMs / 1000} s");
        }

        return (p.ExitCode, outTask.Result + errTask.Result);
    }

    #endregion
}
