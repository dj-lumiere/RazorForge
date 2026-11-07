using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A member routine of a generic record or entity written outside it names the type's parameters on the receiver
/// (<c>routine Box[T].get()</c>). Without them it is RV-S165, which used to show up only as a missing symbol at link
/// time. Also: <c>check d["k"]</c> builds its recovery body without the internal "no handler" warning.
/// </summary>
public class GenericReceiverTests
{
    [Fact]
    public void Analyze_GenericReceiverWithoutParameters_ReportsS165()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  record Box[T]
                                                  needs RecordType T
                                                      v: T

                                                  routine Box.get() -> T
                                                      return me.v
                                                  """);
        SemanticError error = Assert.Single(collection: result.Errors.Where(predicate: e =>
            e.Code == SemanticDiagnosticCode.GenericReceiverWithoutParameters));
        Assert.Contains(expectedSubstring: "routine Box[T].get(...)", actualString: error.Message);
    }

    [Fact]
    public void Analyze_GenericReceiverWithParameters_IsFine()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  record Fill[N]
                                                  needs U64 N
                                                      x: S64

                                                  routine Fill[N].next_count() -> U64
                                                      return N + 1
                                                  """);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.GenericReceiverWithoutParameters);
    }

    [Fact]
    public void Analyze_CheckDictSubscript_HasNoInternalWarning()
    {
        AnalysisResult result = Analyze(source: """
                                                import IO/Console

                                                routine start()
                                                    var d = Dict[Text, S64]()
                                                    d["k"] = 3
                                                    var r = check d["k"]
                                                    when r
                                                        is S64 as v => show(v)
                                                        else => show("fail")
                                                    return
                                                """);
        Assert.DoesNotContain(collection: result.Warnings,
            filter: w => w.Code == SemanticWarningCode.UnknownExpressionType);
    }
}
