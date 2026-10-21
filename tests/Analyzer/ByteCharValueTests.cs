using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A byte is eight bits with bit operations only, and the number of a byte or a character is read with a named
/// routine (<c>b.value()</c>, <c>c.codepoint()</c>), not a conversion.
/// </summary>
public class ByteCharValueTests
{
    /// <summary>A bare number beside a byte, or where a byte goes, is a byte.</summary>
    [Fact]
    public void Analyze_NumberWhereAByteGoes_IsAByte()
    {
        string source = """
                        routine test(b: Byte) -> Bool
                            var c: Byte = 0x41
                            return (b & 0x7F) == c and (b | 1) != 0xFF
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    /// <summary>A number outside 0 to 255 where a byte goes is a build error.</summary>
    [Fact]
    public void Analyze_ByteNumberOutOfRange_Reports()
    {
        string source = """
                        routine test(b: Byte) -> Byte
                            return b & 256
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.IntegerLiteralOverflow && e.Message.Contains(value: "0 to 255"));
    }

    /// <summary><c>&gt;&gt;</c> on a byte points to <c>&gt;&gt;&gt;</c>.</summary>
    [Fact]
    public void Analyze_SignFillingShiftOnByte_PointsToZeroFilling()
    {
        string source = """
                        routine test(b: Byte) -> Byte
                            return b >> 1
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.BinaryOperatorNotFound && e.Message.Contains(value: ">>>"));
    }

    /// <summary>Arithmetic on a byte points to doing it on the byte's value.</summary>
    [Fact]
    public void Analyze_ArithmeticOnByte_PointsToValue()
    {
        string source = """
                        routine test(b: Byte) -> Byte
                            return b + b
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.BinaryOperatorNotFound &&
                         e.Message.Contains(value: "b.value()"));
    }

    /// <summary>The removed conversions name the routine that is written now.</summary>
    [Theory]
    [InlineData("routine test(b: Byte) -> U8\n    return b.to[U8]()", "b.value()")]
    [InlineData("routine test(c: Char) -> U32\n    return c.to[U32]()", "c.codepoint()")]
    [InlineData("routine test(v: U8) -> Byte\n    return v.to[Byte]()", "Byte(v)")]
    [InlineData("routine test(n: U32) -> Char\n    return n.to[Char]()", "Char.from_codepoint(n: v)")]
    public void Analyze_RemovedByteCharConversion_NamesTheRoutine(string source, string hint)
    {
        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.SpecializationNotFound && e.Message.Contains(value: hint));
    }
}
