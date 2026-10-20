using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// The address type <c>Addr</c>: an address names a place, so two are compared with <c>===</c> / <c>!==</c>, and it
/// has no ordering or arithmetic besides <c>offset(bytes:)</c>. The old names <c>Address</c> and <c>CPtr</c> point to
/// it.
/// </summary>
public class AddrTests
{
    /// <summary>Two addresses compare by identity, and an address moves by a byte count.</summary>
    [Fact]
    public void Analyze_AddrIdentityAndOffset_NoError()
    {
        string source = """
                        routine probe(a: Addr, b: Addr) -> Bool
                            return a.offset(bytes: 8) === b or a !== Addr.hollow() or a.is_hollow()
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

    /// <summary>Ordering and arithmetic on an address are errors that name <c>offset(bytes:)</c>.</summary>
    [Theory]
    [InlineData("a < b")]
    [InlineData("(a +% b) === b")]
    public void Analyze_AddrOrderingOrArithmetic_PointsToOffset(string expression)
    {
        string source = $"""
                         routine probe(a: Addr, b: Addr) -> Bool
                             return {expression}
                         """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.OrderingNotSupported &&
                         e.Message.Contains(value: "offset(bytes:)"));
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
