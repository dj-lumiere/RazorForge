namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Locks down where <c>failable</c> goes: a routine whose failure goes to its caller is declared
/// <c>failable</c>, and its callers call it plainly. A throw in a routine that is not failable crashes there.
/// </summary>
public class FailableDeclarationTests
{
    /// <summary>A throwing routine declared without <c>failable</c> builds: the throw crashes there.</summary>
    [Fact]
    public void Throw_WithoutFailable_Builds()
    {
        string source = """
                        module L/Test
                        routine risky(n: S64) -> S64
                            if n == 0
                                throw DivisionByZeroError()
                            return n
                        """;
        AssertAnalyzesSa(source: source);
    }

    /// <summary>A routine that throws AbsentValueError declared without <c>failable</c> builds too.</summary>
    [Fact]
    public void Absent_WithoutFailable_Builds()
    {
        string source = """
                        module L/Test
                        routine find(n: S64) -> S64
                            if n == 0
                                throw AbsentValueError()
                            return n
                        """;
        AssertAnalyzesSa(source: source);
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
                                nothing
                            if (0 til 10).lacks(x)
                                nothing
                            return
                        """;
        AssertAnalyzesSa(source: source);
    }
}
