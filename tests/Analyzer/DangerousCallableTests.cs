using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A routine value from a dangerous routine is a dangerous callable (intent C82): it goes to a C routine's or a
/// dangerous routine's <c>Callable</c> parameter or into a local variable, a dangerous routine's <c>Callable</c>
/// parameters are dangerous callables in its body, and a dangerous callable is called only inside <c>danger</c> or a
/// dangerous routine. Anything else is RF-S609.
/// </summary>
public class DangerousCallableTests
{
    private const string Prelude = """
                                   import IO/Console

                                   dangerous routine raw(n: S64) -> S64
                                       return n + 1

                                   dangerous routine C::qsort(base: Addr, count: U64, size: U64, compare: Callable[(Addr, Addr), S32])

                                   dangerous routine apply(f: Callable[(S64,), S64], n: S64) -> S64
                                       return f(n)

                                   routine plain_apply(f: Callable[(S64,), S64], n: S64) -> S64
                                       return f(n)

                                   dangerous routine by_address(a: Addr, b: Addr) -> S32
                                       return 0

                                   """;

    private static void AssertDangerError(string source, string expected)
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + source, expectedErrorSubstring: expected);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.DangerousCallOutsideDangerBlock);
    }

    // ---- where a dangerous callable may go ----------------------------------------------------------------------

    [Fact]
    public void Analyze_DangerousRoutineToCRoutineCallable_Builds()
    {
        AssertAnalyzesSa(source: Prelude + """
                                 routine start()
                                     danger
                                         C::qsort(base: Addr.hollow(), count: 0, size: 8, compare: by_address)
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_DangerousRoutineToDangerousRoutineCallable_Builds()
    {
        AssertAnalyzesSa(source: Prelude + """
                                 routine start()
                                     danger
                                         show(apply(f: raw, n: 1))
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_DangerousCallableInLocalCalledInDanger_Builds()
    {
        AssertAnalyzesSa(source: Prelude + """
                                 routine start()
                                     var f = raw
                                     danger
                                         show(f(2))
                                         show(apply(f: f, n: 3))
                                     return
                                 """);
    }

    [Fact]
    public void Analyze_DangerousRoutineCallableParameterPassedOn_Builds()
    {
        AssertAnalyzesSa(source: Prelude + """
                                 dangerous routine twice(f: Callable[(S64,), S64], n: S64) -> S64
                                     var g = f
                                     return apply(f: g, n: f(n))

                                 routine start()
                                     danger
                                         show(twice(f: raw, n: 1))
                                     return
                                 """);
    }

    // ---- where it may not go (RF-S609) ----------------------------------------------------------------------------

    [Fact]
    public void Analyze_DangerousRoutineToPlainRoutineCallable_Errors()
    {
        AssertDangerError(source: """
                          routine start()
                              danger
                                  show(plain_apply(f: raw, n: 1))
                              return
                          """, expected: "You take the dangerous routine 'raw' as a value");
    }

    [Fact]
    public void Analyze_DangerousCallableLocalToPlainRoutine_Errors()
    {
        AssertDangerError(source: """
                          routine start()
                              var f = raw
                              show(plain_apply(f: f, n: 1))
                              return
                          """, expected: "'f' holds a routine value from a dangerous routine");
    }

    [Fact]
    public void Analyze_DangerousCallableIntoMemberVariable_Errors()
    {
        AssertDangerError(source: """
                          record Holder
                              f: Callable[(S64,), S64]

                          routine start()
                              var h = Holder(f: raw)
                              return
                          """, expected: "where a plain routine value goes");
    }

    [Fact]
    public void Analyze_DangerousCallableIntoCollection_Errors()
    {
        AssertDangerError(source: """
                          routine start()
                              var f = raw
                              var fs: List[Callable[(S64,), S64]] = []
                              fs.add_last(value: f)
                              return
                          """, expected: "'f' holds a routine value from a dangerous routine");
    }

    [Fact]
    public void Analyze_DangerousCallableParameterEscapes_Errors()
    {
        AssertDangerError(source: """
                          dangerous routine keep(f: Callable[(S64,), S64]) -> S64
                              return plain_apply(f: f, n: 1)

                          routine start()
                              danger
                                  show(keep(f: raw))
                              return
                          """, expected: "a 'Callable' parameter of a dangerous routine is one");
    }

    [Fact]
    public void Analyze_DangerousCallableReturned_Errors()
    {
        AssertDangerError(source: """
                          routine give() -> Callable[(S64,), S64]
                              return raw

                          routine start()
                              return
                          """, expected: "You take the dangerous routine 'raw' as a value");
    }

    [Fact]
    public void Analyze_DangerousCallableCalledOutsideDanger_Errors()
    {
        AssertDangerError(source: """
                          routine start()
                              var f = raw
                              show(f(1))
                              return
                          """, expected: "You call 'f'");
    }

    [Fact]
    public void Analyze_DangerousCallableCalledInLambda_Errors()
    {
        AssertDangerError(source: """
                          routine start()
                              var f = raw
                              danger
                                  var g = (n: S64) => f(n)
                              return
                          """, expected: "You call 'f'");
    }
}
