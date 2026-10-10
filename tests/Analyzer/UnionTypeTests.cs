using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A variant written in place, <c>A | B</c>: one type per set of types in any order, a value of one of its types goes
/// in as it is, a smaller one is held as a larger one, and <c>define</c> names it.
/// </summary>
public class UnionTypeTests
{
    [Fact]
    public void Analyze_SameTypesInAnyOrder_AreOneType()
    {
        AssertAnalyzesSa(source: """
                                 routine pick(flag: Bool) -> S64 | Text
                                     if flag
                                         return "x"
                                     return 1

                                 routine start()
                                     var v: Text | S64 = pick(flag: true)
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_SmallerUnion_WidensToLarger()
    {
        AssertAnalyzesSa(source: """
                                 routine pick(flag: Bool) -> S64 | Text
                                     return 1

                                 routine start()
                                     var wide: S64 | Text | Bool = pick(flag: true)
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_LargerUnion_DoesNotNarrow()
    {
        AssertHasErrorSa(source: """
                                 routine pick(flag: Bool) -> S64 | Text | Bool
                                     return 1

                                 routine start()
                                     var narrow: S64 | Text = pick(flag: true)
                                     return
                                 """,
            expectedErrorSubstring: "Cannot assign value of type");
    }

    [Fact]
    public void Analyze_DefineNamesAUnion()
    {
        AssertAnalyzesSa(source: """
                                 define S64 | Text as Cell

                                 routine describe(c: S64 | Text) -> Text
                                     when c
                                         is S64 as n => return f"{n}"
                                         is Text as t => return t
                                     return "?"

                                 routine start()
                                     var c: Cell = "x"
                                     var shown = describe(c: steal c)
                                     return
                                 """);
    }
}
