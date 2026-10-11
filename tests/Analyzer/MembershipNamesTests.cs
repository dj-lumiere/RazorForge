using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Membership is `has` / `lacks` on a container and `has_key` / `lacks_key` on a map; `have` / `lack` reach them.
/// The old `contains` names the new one.
/// </summary>
public class MembershipNamesTests
{
    [Fact]
    public void Analyze_HasAndHasKey_AndOperators_Pass()
    {
        AssertAnalyzesSa(source: """
                                 import IO/Console
                                 routine start()
                                     var xs = [1, 2, 3]
                                     var d = Dict[S64, S64]()
                                     d[1] = 10
                                     show(f"{xs.has(value: 2)} {xs.lacks(value: 9)} {d.has_key(key: 1)} {d.lacks_key(key: 2)}")
                                     show(f"{xs have 2} {d have 1} {d lack 3}")
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_Contains_NamesHas()
    {
        AnalysisResult result = AssertHasErrorSa(source: """
                                                         routine start()
                                                             var xs = [1, 2, 3]
                                                             var found = xs.contains(value: 2)
                                                             return
                                                         """,
            expectedErrorSubstring: "Membership is 'has' / 'lacks' now");
        Assert.NotEmpty(collection: result.Errors);
    }

    [Fact]
    public void Analyze_ContainsOnADict_NamesHasKey()
    {
        AssertHasErrorSa(source: """
                                 routine start()
                                     var d = Dict[S64, S64]()
                                     var found = d.contains(key: 2)
                                     return
                                 """,
            expectedErrorSubstring: "'has_key'");
    }
}
