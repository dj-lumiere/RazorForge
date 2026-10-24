using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// One error kind, and failures that rise only through failable routines (C81): a <c>throw</c> in a routine that is
/// not failable crashes where it stands, <c>try</c>/<c>check</c> over an expression that reaches no failable call is
/// RF-S760, a routine value that may fail goes only to a <c>failable Callable[...]</c> (RF-S761), and <c>pierce</c> is
/// not a word of the language (RF-G227).
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

    /// <summary><c>try</c> over a call of a routine that is not failable is RF-S760, pointing at the call.</summary>
    [Fact]
    public void Try_OverPlainCall_IsError()
    {
        string source = """
                        module L/Test
                        routine double(n: S64) -> S64
                            return n *% 2
                        routine start()
                            var a = try double(n: 1)
                            return
                        """;
        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.NothingToRecover && e.Message.Contains(value: "'double'"));
    }

    /// <summary><c>check</c> over a plain routine that throws inside is RF-S760 too: the throw crashes there.</summary>
    [Fact]
    public void Check_OverPlainRoutineThatThrows_IsError()
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
        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.NothingToRecover);
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

    /// <summary>A lambda that can fail handed to a <c>failable Callable</c> parameter makes the call recoverable:
    /// <c>try</c> over it builds.</summary>
    [Fact]
    public void FailableLambda_ToFailableParameter_IsRecoverable()
    {
        string source = """
                        module L/Test
                        import IterTools
                        routine apply(f: failable Callable[(S64,), S64], x: S64) -> S64
                            return f(x)
                        routine start()
                            var a = try apply(f: x => 10 // x, x: 0)
                            var xs = List[S64]()
                            var b = try xs.select(transform: x => 10 // x).to[List]()
                            return
                        """;
        AssertAnalyzesSa(source: source);
    }

    /// <summary>An adapter holding a lambda that cannot fail still holds a <c>failable Callable</c> member variable, so
    /// a keyword over its terminal builds (the terminal may fail through it).</summary>
    [Fact]
    public void Terminal_OverAdapter_IsRecoverable()
    {
        string source = """
                        module L/Test
                        import IterTools
                        routine start()
                            var xs = List[S64]()
                            var s = xs.select(transform: x => 10 // x)
                            var b = try s.to[List]()
                            return
                        """;
        AssertAnalyzesSa(source: source);
    }

    /// <summary>A lambda that can fail handed to a plain <c>Callable</c> parameter is RF-S761.</summary>
    [Fact]
    public void FailableLambda_ToPlainParameter_IsError()
    {
        string source = """
                        module L/Test
                        routine apply(f: Callable[(S64,), S64], x: S64) -> S64
                            return f(x)
                        routine start()
                            var a = apply(f: x => 10 // x, x: 2)
                            return
                        """;
        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.FailableValueToPlainParameter);
    }

    /// <summary>A lambda that cannot fail goes to a plain <c>Callable</c>, and <c>try</c> over a call handing only
    /// such a lambda to a routine that is not failable is RF-S760.</summary>
    [Fact]
    public void PlainLambda_ToPlainParameter_Builds_TryIsError()
    {
        string source = """
                        module L/Test
                        routine apply(f: failable Callable[(S64,), S64], x: S64) -> S64
                            return f(x)
                        routine start()
                            var a = apply(f: x => x *% 2, x: 2)
                            var b = try apply(f: x => x *% 2, x: 2)
                            return
                        """;
        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Single(collection: result.Errors, predicate: e => e.Code == SemanticDiagnosticCode.NothingToRecover);
    }

    /// <summary><c>pierce</c> is not a keyword: RF-G227, with the fix <c>throw</c> in a routine that is not
    /// failable.</summary>
    [Fact]
    public void Pierce_IsGrammarError()
    {
        string source = """
                        module L/Test
                        routine start()
                            pierce LogicBreachedError()
                            return
                        """;
        (_, Builder.Parser.Parser parser) = ParseWithErrors(source: source);
        Assert.True(condition: parser.HasErrors);
        Assert.Contains(collection: parser.GetErrors(), filter: e => e.Contains(value: "pierce"));
    }
}
