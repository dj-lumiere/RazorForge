using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A <c>var</c> may not reuse the name of a parameter or of a local of an enclosing block (one name is one
/// variable within a routine, as for pattern bindings). Before the ban, the local silently reused the hidden
/// variable's storage slot: a type-changing redeclaration wrote a wider value into the narrower slot.
/// </summary>
public class LocalShadowingTests
{
    /// <summary>A local named like a parameter is rejected, even with a different type.</summary>
    [Fact]
    public void Analyze_LocalHidesParameter_ReportsError()
    {
        string source = """
                        routine f(acc: Bool) -> S128
                            var acc = 5_s128
                            return acc
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.IdentifierShadowing && e.Message.Contains(value: "parameter"));
    }

    /// <summary>A local in an inner block named like a local of an enclosing block is rejected.</summary>
    [Fact]
    public void Analyze_LocalHidesOuterLocal_ReportsError()
    {
        string source = """
                        routine h(flag: Bool) -> S128
                            var v = true
                            var out = 0_s128
                            if flag
                                var v = 5_s128
                                out = v
                            return out
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.IdentifierShadowing && e.Message.Contains(value: "enclosing block"));
    }

    /// <summary>The same name in two sibling blocks is two separate variables, not a shadow.</summary>
    [Fact]
    public void Analyze_SameNameInSiblingBlocks_NoError()
    {
        string source = """
                        routine h(flag: Bool) -> S64
                            var out = 0_s64
                            if flag
                                var i = 1_s64
                                out = i
                            else
                                var i = 2_s64
                                out = i
                            return out
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.IdentifierShadowing);
    }
}
