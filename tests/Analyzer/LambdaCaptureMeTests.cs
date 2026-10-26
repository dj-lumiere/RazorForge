namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A lambda written in a member routine may read <c>me</c> of a record (it keeps a copy). An entity's <c>me</c> is
/// reached for the call only, so capturing it is RF-S606, as for a token.
/// </summary>
public class LambdaCaptureMeTests
{
    [Fact]
    public void RecordMe_Captured_Analyzes()
    {
        AssertAnalyzes(source: """
                               record Offset
                                   n: S64

                               routine Offset.adder() -> Callable[(S64,), S64]
                                   return (k: S64) => me.n + k
                               """);
    }

    [Fact]
    public void EntityMe_Captured_IsRfS606()
    {
        AssertHasError(source: """
                               entity Counter
                                   n: S64

                               routine Counter.adder() -> Callable[(S64,), S64]
                                   return (k: S64) => me.n + k
                               """,
            expectedErrorSubstring: "You capture 'me' in this lambda");
    }
}
