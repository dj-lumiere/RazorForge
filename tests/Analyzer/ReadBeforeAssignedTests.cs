using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A <c>lateinit var</c> is read only where every path to the read has given it a value (RF-S202). The check follows
/// branches like a <c>steal</c> does: a path that leaves (<c>return</c>, <c>break</c>) doesn't count, and a loop is left
/// with what was unassigned at its <c>break</c>s.
/// </summary>
public class ReadBeforeAssignedTests
{
    [Fact]
    public void Analyze_AssignedOnEveryPath_Builds()
    {
        AssertAnalyzesSa(source: """
                                 routine pick(n: S64) -> Text
                                     lateinit var label: Text
                                     if n > 0
                                         label = "pos"
                                     else
                                         label = "neg"
                                     return label

                                 routine early(n: S64) -> Text
                                     lateinit var t: Text
                                     if n > 0
                                         t = "a"
                                     else
                                         return "none"
                                     return t

                                 routine scan() -> S64
                                     lateinit var found: S64
                                     var i: S64 = 0
                                     loop
                                         i += 1
                                         if i == 3
                                             found = i
                                             break
                                     return found

                                 routine start()
                                     drop pick(n: 1)
                                     drop early(n: 1)
                                     drop scan()
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_WhenArmsAssignOrThrow_Builds()
    {
        AssertAnalyzesSa(source: """
                                 crashable UnknownWordError
                                     word: Text

                                 routine UnknownWordError.crash_message() -> Text
                                     return f"'{me.word}' is not a word"

                                 failable routine size(word: Text) -> S64
                                     lateinit var n: S64
                                     when word
                                         == "one" => n = 1
                                         == "two" => n = 2
                                         else => throw UnknownWordError(word: word)
                                     return n

                                 routine start()
                                     drop try size(word: "one")
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_WhenWithoutElseAssigningInArms_IsRejected()
    {
        AssertHasErrorSa(source: """
                         routine size(word: Text) -> S64
                             lateinit var n: S64
                             when word
                                 == "one" => n = 1
                                 == "two" => n = 2
                             return n

                         routine start()
                             drop size(word: "one")
                             return
                         """,
            expectedErrorSubstring: "'n' was declared without a value");
    }

    [Fact]
    public void Analyze_ReadBeforeAnyAssignment_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: """
                                                         routine start()
                                                             lateinit var name: Text
                                                             var copy = name
                                                             name = "x"
                                                             return
                                                         """,
            expectedErrorSubstring: "'name' was declared without a value");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.ReadBeforeAssigned);
    }

    [Fact]
    public void Analyze_AssignedOnOnePath_IsRejected()
    {
        AssertHasErrorSa(source: """
                         routine label(n: S64) -> Text
                             lateinit var t: Text
                             if n > 0
                                 t = "pos"
                             return t

                         routine start()
                             drop label(n: 1)
                             return
                         """,
            expectedErrorSubstring: "a path to this line doesn't give it one");
    }

    [Fact]
    public void Analyze_AssignedOnlyInsideAnUntil_IsRejected()
    {
        AssertHasErrorSa(source: """
                         routine first(n: S64) -> S64
                             lateinit var v: S64
                             var i: S64 = 0
                             until i >= n
                                 v = i
                                 i += 1
                             return v

                         routine start()
                             drop first(n: 3)
                             return
                         """,
            expectedErrorSubstring: "'v' was declared without a value");
    }

    [Fact]
    public void Analyze_NumberFittingTwoVariantMembers_IsRejected()
    {
        AssertHasErrorSa(source: """
                         variant Num
                             S64
                             S32

                         routine start()
                             var n: Num = 7
                             return
                         """,
            expectedErrorSubstring: "could go into the 'S64', 'S32' members of 'Num'");
    }
}
