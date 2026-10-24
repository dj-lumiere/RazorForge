using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Choice values and flags bits are written on every member or on none (intent C79): some with and some without is
/// RF-S417, a flags bit is 0 to 63 (RF-S419), and two members on one bit is RF-S429.
/// </summary>
public class ChoiceFlagsValueTests
{
    private static void AssertHasErrorCode(string source, SemanticDiagnosticCode code, string expected)
    {
        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: expected);
        Assert.Contains(collection: result.Errors, filter: e => e.Code == code);
    }

    [Fact]
    public void Analyze_ChoiceSomeValues_Errors()
    {
        AssertHasErrorCode(source: """
                           choice Level
                               LOW
                               HIGH: 10

                           routine start()
                               return
                           """, code: SemanticDiagnosticCode.ChoiceMixedValues, expected: "('LOW' aren't)");
    }

    [Fact]
    public void Analyze_FlagsSomeBits_Errors()
    {
        AssertHasErrorCode(source: """
                           flags Marks
                               MINE
                               FLAGGED: 3

                           routine start()
                               return
                           """, code: SemanticDiagnosticCode.ChoiceMixedValues, expected: "('MINE' don't)");
    }

    [Fact]
    public void Analyze_FlagsBitPastU64_Errors()
    {
        AssertHasErrorCode(source: """
                           flags Marks
                               MINE: 0
                               FLAGGED: 64

                           routine start()
                               return
                           """, code: SemanticDiagnosticCode.ChoiceCaseValueOverflow, expected: "from 0 to 63");
    }

    [Fact]
    public void Analyze_FlagsSameBit_Errors()
    {
        AssertHasErrorCode(source: """
                           flags Marks
                               MINE: 2
                               FLAGGED: 2

                           routine start()
                               return
                           """, code: SemanticDiagnosticCode.ChoiceDuplicateValue, expected: "are both bit 2");
    }

    [Fact]
    public void Analyze_FlagsEveryBitWritten_Builds()
    {
        AssertAnalyzesSa(source: """
                         flags Marks
                             MINE: 5
                             FLAGGED: 0

                         routine start()
                             var m = Marks.MINE and Marks.FLAGGED
                             return
                         """);
    }
}
