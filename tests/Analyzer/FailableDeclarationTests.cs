namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Locks down where the failable <c>!</c> goes: a routine whose body throws or goes absent must be
/// declared with <c>!</c> (RF-S750 / RF-S751), and its callers call it plainly.
/// </summary>
public class FailableDeclarationTests
{
    /// <summary>A throwing routine declared without <c>!</c> is an error.</summary>
    [Fact]
    public void Throw_WithoutBangOnDeclaration_IsError()
    {
        string source = """
                        module L/Test
                        routine risky(n: S64) -> S64
                            if n == 0
                                throw DivisionByZeroError()
                            return n
                        """;
        AssertHasErrorSa(source: source, expectedErrorSubstring: "so it is declared 'failable'");
    }

    /// <summary>A routine that goes absent declared without <c>!</c> is an error.</summary>
    [Fact]
    public void Absent_WithoutBangOnDeclaration_IsError()
    {
        string source = """
                        module L/Test
                        routine find(n: S64) -> S64
                            if n == 0
                                throw AbsentValueError()
                            return n
                        """;
        AssertHasErrorSa(source: source, expectedErrorSubstring: "so it is declared 'failable'");
    }

    /// <summary>A throwing routine declared with <c>!</c>, called plainly and with <c>try</c>, is fine.</summary>
    [Fact]
    public void Throw_WithBangOnDeclaration_PlainCalls_OK()
    {
        string source = """
                        module L/Test
                        failable routine risky(n: S64) -> S64
                            if n == 0
                                throw DivisionByZeroError()
                            return n
                        routine start()
                            var a = risky(n: 1)
                            var b = try risky(n: 0)
                            return
                        """;
        AssertAnalyzesSa(source: source);
    }

    /// <summary><c>to</c> is inclusive for membership: <c>(0 to 10).has(10)</c> is a Bool test on the range.</summary>
    [Fact]
    public void RangeHave_AppliesToWholeRange()
    {
        string source = """
                        module L/Test
                        routine start()
                            var x = 10
                            if (0 to 10).has(x)
                                pass
                            if (0 til 10).lacks(x)
                                pass
                            return
                        """;
        AssertAnalyzesSa(source: source);
    }
}
