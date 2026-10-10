using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A record pattern tests and names member variables the way the record is built (<c>is Monster(hp: 0) as m</c>). An
/// arm that tests a member variable neither covers its type nor repeats another arm of that type.
/// </summary>
public class RecordPatternTests
{
    private const string Prelude = """
                                   record Monster
                                       hp: S64
                                       name: Text

                                   record Wall
                                       height: S64

                                   variant Cell
                                       Monster
                                       Wall

                                   """;

    [Fact]
    public void Analyze_ConditionalArmsOfOneType_AreNotDuplicates()
    {
        AnalysisResult result = AssertAnalyzesSa(source: Prelude + """
                                                                   routine label(c: Cell) -> Text
                                                                       when c
                                                                           is Monster(hp: 0) as m => return m.name
                                                                           is Monster(hp: < 5, name) => return name
                                                                           is Monster => return "monster"
                                                                           is Wall(height) => return f"{height}"
                                                                       return "?"

                                                                   routine start()
                                                                       return
                                                                   """);
        Assert.DoesNotContain(collection: result.Warnings,
            filter: w => w.Code == SemanticWarningCode.NonExhaustiveWhen);
    }

    [Fact]
    public void Analyze_OnlyConditionalArms_DoNotCoverTheType()
    {
        AnalysisResult result = AssertAnalyzesSa(source: Prelude + """
                                                                   routine label(c: Cell) -> Text
                                                                       when c
                                                                           is Monster(hp: 0) => return "dead"
                                                                           is Wall => return "wall"
                                                                       return "?"

                                                                   routine start()
                                                                       return
                                                                   """);
        Assert.Contains(collection: result.Warnings,
            filter: w => w.Code == SemanticWarningCode.NonExhaustiveWhen);
    }
}
