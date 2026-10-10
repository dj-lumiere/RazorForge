namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>A routine marked <c>@infix</c> is written between its operands (<c>a dot b</c>); others are not.</summary>
public class InfixRoutineTests
{
    private const string Prelude = """
                                   record Vec
                                       x: S64

                                   @infix
                                   routine Vec.dot(you: Vec) -> S64
                                       return me.x * you.x

                                   routine Vec.times(you: Vec) -> S64
                                       return me.x * you.x

                                   """;

    [Fact]
    public void Analyze_InfixCall_Binds()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           routine start()
                                               var a = Vec(x: 2)
                                               var n = a dot a
                                               var big = a dot a > 3
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_InfixCallOfUnmarkedRoutine_IsRejected()
    {
        AssertHasErrorSa(source: Prelude + """
                                           routine start()
                                               var a = Vec(x: 2)
                                               var n = a times a
                                               return
                                           """,
            expectedErrorSubstring: "only a routine marked '@infix' is");
    }

    [Fact]
    public void Analyze_InfixOnTwoParameters_IsRejected()
    {
        AssertHasErrorSa(source: """
                                 record Vec
                                     x: S64

                                 @infix
                                 routine Vec.mix(you: Vec, by: S64) -> S64
                                     return me.x * you.x * by

                                 routine start()
                                     return
                                 """,
            expectedErrorSubstring: "takes a receiver and one parameter");
    }
}
