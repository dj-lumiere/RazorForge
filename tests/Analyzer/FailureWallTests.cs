using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// One error kind, and failures that pass to the caller from every routine but an <c>infallible</c> one (C151, C152):
/// <c>try</c>/<c>check</c> take a failure from any depth below, a keyword over a value that cannot fail gives the value,
/// every routine value may go to any <c>Callable[...]</c>, <c>failable</c> is a grammar error, and <c>pierce</c> is an
/// ordinary name.
/// </summary>
public class FailureWallTests
{
    /// <summary>A <c>throw</c> in a routine that is not failable builds: it crashes there, so the routine stays plain
    /// and its callers call it bare.</summary>
    [Fact]
    public void Throw_InPlainRoutine_Builds()
    {
        string source = """
                        module L/Test
                        routine require_positive(n: S64) -> S64
                            if n <= 0
                                throw LogicBreachedError()
                            return n
                        routine start()
                            var a = require_positive(n: 1)
                            return
                        """;
        AssertAnalyzesSa(source: source);
    }

    /// <summary><c>try</c> over a call of a routine that cannot fail builds: it gives the value, present.</summary>
    [Fact]
    public void Try_OverCallThatCannotFail_Builds()
    {
        string source = """
                        module L/Test
                        routine double(n: S64) -> S64
                            return n *% 2
                        routine start()
                            var a = try double(n: 1)
                            var b = check 5
                            return
                        """;
        AssertAnalyzesSa(source: source);
    }

    /// <summary><c>check</c> over a routine that throws, written without any marker, builds: its failure passes on.</summary>
    [Fact]
    public void Check_OverUnmarkedRoutineThatThrows_Builds()
    {
        string source = """
                        module L/Test
                        routine require_positive(n: S64) -> S64
                            if n <= 0
                                throw LogicBreachedError()
                            return n
                        routine start()
                            var r = check require_positive(n: 0)
                            return
                        """;
        AssertAnalyzesSa(source: source);
    }

    /// <summary>Checked operators, subscripts and conversions are failable routines, so a keyword over them
    /// builds.</summary>
    [Fact]
    public void Try_OverCheckedOperatorSubscriptAndConversion_Builds()
    {
        string source = """
                        module L/Test
                        routine start()
                            var a: S64 = 7
                            var b: S64 = 0
                            var xs = List[S64]()
                            var big: S64 = 300
                            var q = try a // b
                            var s = try a + b
                            var e = try xs[9]
                            var c = check big.to[S8]()
                            return
                        """;
        AssertAnalyzesSa(source: source);
    }

    /// <summary>A lambda that can fail handed to a <c>Callable</c> parameter makes the call recoverable:
    /// <c>try</c> over it builds.</summary>
    [Fact]
    public void FailableLambda_ToFailableParameter_IsRecoverable()
    {
        string source = """
                        module L/Test
                        import EmitterTools
                        routine apply(f: Callable[(S64,), S64], x: S64) -> S64
                            return f(x)
                        routine start()
                            var a = try apply(f: x => 10 // x, x: 0)
                            var xs = List[S64]()
                            var b = try xs.select(transform: x => 10 // x).to[List]()
                            return
                        """;
        AssertAnalyzesSa(source: source);
    }

    /// <summary>An adapter holding a lambda that cannot fail still holds a <c>Callable</c> member variable, so
    /// a keyword over its terminal builds (the terminal may fail through it).</summary>
    [Fact]
    public void Terminal_OverAdapter_IsRecoverable()
    {
        string source = """
                        module L/Test
                        import EmitterTools
                        routine start()
                            var xs = List[S64]()
                            var s = xs.select(transform: x => 10 // x)
                            var b = try s.to[List]()
                            return
                        """;
        AssertAnalyzesSa(source: source);
    }

    /// <summary>A lambda that can fail goes to any <c>Callable</c> parameter: there is no RA-S761 any more.</summary>
    [Fact]
    public void LambdaThatCanFail_ToCallableParameter_Builds()
    {
        string source = """
                        module L/Test
                        routine apply(f: Callable[(S64,), S64], x: S64) -> S64
                            return f(x)
                        routine start()
                            var a = apply(f: x => 10 // x, x: 2)
                            return
                        """;
        AssertAnalyzesSa(source: source);
    }

    /// <summary><c>failable</c> is a grammar error that points to the new rule, and <c>infallible</c> marks a routine
    /// that promises not to fail.</summary>
    [Fact]
    public void FailableWord_IsGrammarError_InfallibleBuilds()
    {
        string failable = """
                          module L/Test
                          failable routine parse(n: S64) -> S64
                              return n
                          routine start()
                              return
                          """;
        (_, Builder.Parser.Parser parser) = ParseWithErrors(source: failable);
        Assert.Contains(collection: parser.GetErrors(), filter: e => e.Contains(value: "RA-G230") &&
                                                                   e.Contains(value: "infallible"));

        string infallible = """
                            module L/Test
                            infallible routine close(n: S64) -> S64
                                return n *% 2
                            routine start()
                                var a = try close(n: 3)
                                return
                            """;
        AssertAnalyzesSa(source: infallible);
    }

    /// <summary><c>pierce</c> is not a keyword: it names a variable, a routine and a field like any other
    /// name.</summary>
    [Fact]
    public void Pierce_IsAnOrdinaryName()
    {
        string source = """
                        module L/Test
                        import IO/Console
                        record Arrow
                            pierce: S64
                        routine pierce(n: S64) -> S64
                            return n + 1
                        routine aim(pierce: S64) -> S64
                            var a = Arrow(pierce: pierce)
                            return a.pierce
                        routine start()
                            var hit = pierce(n: 1)
                            var pierced = aim(pierce: hit)
                            show(f"{pierced}")
                            return
                        """;
        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }
}
