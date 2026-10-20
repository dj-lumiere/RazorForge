using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// The address type <c>Addr</c>: an address names a place, so two are compared with <c>===</c> / <c>!==</c>. It has no
/// ordering, it moves by a <c>ByteSize</c> with <c>+%</c> / <c>-%</c>, and <c>b -% a</c> is the distance from <c>a</c>
/// to <c>b</c>. The old names <c>Address</c> and <c>CPtr</c> point to it.
/// </summary>
public class AddrTests
{
    /// <summary>Two addresses compare by identity, an address moves by a ByteSize with <c>+%</c> / <c>-%</c>, and
    /// <c>-%</c> between two addresses is their distance.</summary>
    [Fact]
    public void Analyze_AddrIdentityAndWrappingMoves_NoError()
    {
        string source = """
                        routine probe(a: Addr, b: Addr) -> Bool
                            var step: ByteSize = b -% a
                            var back: Addr = b -% 8b
                            return a +% 8b === b or back +% step !== a or a !== Addr.hollow() or a.is_hollow()
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    /// <summary><c>==</c> between addresses is an error that names <c>===</c>.</summary>
    [Fact]
    public void Analyze_AddrValueEquality_PointsToIdentity()
    {
        string source = """
                        routine probe(a: Addr, b: Addr) -> Bool
                            return a == b
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.OrderingNotSupported && e.Message.Contains(value: "'==='"));
    }

    /// <summary>Plain <c>+</c> / <c>-</c> on an address are errors that name the wrapping operator to write.</summary>
    [Theory]
    [InlineData("(a + 8b) === b", "'+%'")]
    [InlineData("(b - 8b) === a", "'-%'")]
    [InlineData("(b - a) == 8b", "'-%'")]
    public void Analyze_AddrPlainArithmetic_PointsToWrapping(string expression, string named)
    {
        string source = $"""
                         routine probe(a: Addr, b: Addr) -> Bool
                             return {expression}
                         """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.OrderingNotSupported &&
                         e.Message.Contains(value: $"Write {named} here"));
    }

    /// <summary>Ordering, and arithmetic other than the wrapping moves, are errors that name <c>+%</c> / <c>-%</c>.</summary>
    [Theory]
    [InlineData("a < b")]
    [InlineData("(a +% b) === b")]
    public void Analyze_AddrOrderingOrOtherArithmetic_PointsToWrapping(string expression)
    {
        string source = $"""
                         routine probe(a: Addr, b: Addr) -> Bool
                             return {expression}
                         """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.OrderingNotSupported &&
                         e.Message.Contains(value: "'+%' / '-%'"));
    }

    /// <summary>The old names of the address type name <c>Addr</c>.</summary>
    [Theory]
    [InlineData("Address")]
    [InlineData("CPtr")]
    public void Analyze_OldAddressTypeName_NamesAddr(string oldName)
    {
        string source = $"""
                         routine probe(a: {oldName})
                             return
                         """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.UnknownType && e.Message.Contains(value: "'Addr'"));
    }
}
