using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A named argument whose name no parameter has is one S505 that names the parameter to write, and no count error:
/// the standard library's <c>eq</c> routines take <c>you:</c>, as Equatable does.
/// </summary>
public class NamedArgumentNameTests
{
    /// <summary><c>a.eq(other: b)</c> on <c>Text.eq(you:)</c> names <c>you</c> and reports no "expects 1 argument(s)".</summary>
    [Fact]
    public void Analyze_MisnamedArgument_NamesTheParameterAndNoCount()
    {
        string source = """
                        routine start()
                            var a: Text = "x"
                            var b: Text = "y"
                            var same = a.eq(other: b)
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        SemanticError error = Assert.Single(collection: result.Errors,
            predicate: e => e.Code == SemanticDiagnosticCode.UnknownNamedArgument);
        Assert.Contains(expectedSubstring: "has no parameter named 'other': its parameter is 'you', so write 'you:' instead of 'other:'",
            actualString: error.Message);
        Assert.DoesNotContain(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.TooFewArguments);
    }

    /// <summary>The <c>eq</c> routines of Text, Bytes, Set and BitList take <c>you:</c>.</summary>
    [Fact]
    public void Analyze_StandardEqTakesYou_Builds()
    {
        string source = """
                        import Collections

                        routine start()
                            var a: Text = "x"
                            var t = a.eq(you: "x")
                            var b = Bytes(from_byte: Byte(1))
                            var u = b.eq(you: Bytes(from_byte: Byte(1)))
                            var s = Set[S64]()
                            var v = s.eq(you: Set[S64]())
                            var bits = BitList()
                            var w = bits.eq(you: BitList())
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code is SemanticDiagnosticCode.UnknownNamedArgument or SemanticDiagnosticCode.TooFewArguments);
    }

    /// <summary><c>Pair()</c> and a crashable built with nothing, whose member variables have no defaults, are S455
    /// naming each one and the whole call, not a call left for the emitter.</summary>
    [Fact]
    public void Analyze_ConstructionWithNoArguments_NamesTheMissingMemberVariables()
    {
        string source = """
                        record Pair
                            a: S64
                            b: S64

                        crashable OddError
                            n: S64

                        routine OddError.crash_message() -> Text
                            return "odd"

                        routine start()
                            var p = Pair()
                            var e = OddError()
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        List<SemanticError> missing = result.Errors
                                            .Where(predicate: e => e.Code == SemanticDiagnosticCode.MissingRequiredMemberVariable)
                                            .ToList();
        Assert.Contains(collection: missing, filter: e => e.Message.Contains(value: "leaves out 'a', 'b'") &&
                                                          e.Message.Contains(value: "'Pair(a: ..., b: ...)'"));
        Assert.Contains(collection: missing, filter: e => e.Message.Contains(value: "'OddError(n: ...)'"));
    }
}
