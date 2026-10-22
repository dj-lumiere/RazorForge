using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Tests for a routine returning a token taken from one of several token parameters. Different returns may use
/// different parameters, and the caller's token has every argument handed to one of them as a source: a change
/// that ends any of them ends the token (RF-S643 at a later use). Returning a token taken from a local is still
/// RF-S600.
/// </summary>
public class TokenReturnSourcesTests
{
    private const string Prelude = """
                                   import IO/Console

                                   entity Box
                                       n: S64

                                   routine shorter(x: Viewing[List[S64]], y: Viewing[List[S64]]) -> Viewing[List[S64]]
                                       if x.count() <= y.count()
                                           return x
                                       return y

                                   routine shortest(p: Viewing[List[S64]], q: Viewing[List[S64]]) -> Viewing[List[S64]]
                                       return shorter(x: p, y: q)

                                   routine first_box(x: Viewing[List[Box]], y: Viewing[List[Box]]) -> Viewing[Box]
                                       return if x.count() > 0 then x.view_at(index: 0) else y.view_at(index: 0)

                                   """;

    [Fact]
    public void Analyze_ReturnFromEitherParameter_Builds()
    {
        string source = Prelude + """
                                  routine start()
                                      var a = [1, 2, 3]
                                      var b = [4, 5]
                                      var v = shorter(x: a, y: b)
                                      show(f"{v.count()}")
                                      var w = shortest(p: a, q: b)
                                      show(f"{w.count()}")
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code is SemanticDiagnosticCode.TokenReturnNotAllowed
                or SemanticDiagnosticCode.TokenUsedAfterSourceChanged);
    }

    [Fact]
    public void Analyze_UseAfterOneSourceStolen_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var a = [1, 2, 3]
                                      var b = [4, 5]
                                      var v = shorter(x: a, y: b)
                                      var c = steal a
                                      show(f"{v.count()} {c.count()}")
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "You are using the token 'v'");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenUsedAfterSourceChanged &&
                         e.Message.Contains(value: "'a'"));
    }

    [Fact]
    public void Analyze_UseAfterOtherSourceEnds_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var a = [1, 2, 3]
                                      var keep = [9]
                                      var v = keep.view()
                                      if a.count() > 0
                                          var b = [4, 5]
                                          v = shorter(x: a, y: b)
                                      show(f"{v.count()}")
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "You are using the token 'v'");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenUsedAfterSourceChanged &&
                         e.Message.Contains(value: "'b'"));
    }

    [Fact]
    public void Analyze_UseAfterOneSourceReshaped_Errors()
    {
        // An element token from either list ends when either list can add or remove elements.
        string source = Prelude + """
                                  routine start()
                                      var xs = List[Box]()
                                      xs.add_last(value: Box(n: 1))
                                      var ys = List[Box]()
                                      ys.add_last(value: Box(n: 2))
                                      var first = first_box(x: xs, y: ys)
                                      ys.add_last(value: Box(n: 3))
                                      show(f"{first.n}")
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "You are using the token 'first'");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenUsedAfterSourceChanged &&
                         e.Message.Contains(value: "'ys[...]'"));
    }

    [Fact]
    public void Analyze_ForwardingRoutineInheritsBothSources_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var a = [1, 2, 3]
                                      var b = [4, 5]
                                      var w = shortest(p: a, q: b)
                                      var c = steal b
                                      show(f"{w.count()} {c.count()}")
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "You are using the token 'w'");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenUsedAfterSourceChanged &&
                         e.Message.Contains(value: "'b'"));
    }

    [Fact]
    public void Analyze_ReturnLocalToken_StillErrors()
    {
        string source = Prelude + """
                                  routine pick(x: Viewing[List[S64]]) -> Viewing[List[S64]]
                                      var mine = [1]
                                      if x.count() > 0
                                          return x
                                      return mine.view()

                                  routine start()
                                      var a = [1, 2, 3]
                                      show(f"{pick(x: a).count()}")
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "from 'me' or from a token parameter");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenReturnNotAllowed &&
                         e.Message.Contains(value: "'mine'"));
    }

    [Fact]
    public void Analyze_ReturnOwnedEntityParameterToken_StillErrors()
    {
        string source = Prelude + """
                                  routine pick(x: Viewing[List[S64]], y: List[S64]) -> Viewing[List[S64]]
                                      if x.count() > 0
                                          return x
                                      return y.view()

                                  routine start()
                                      var a = [1, 2, 3]
                                      show(f"{pick(x: a, y: [4]).count()}")
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "an entity parameter handed over to this routine");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenReturnNotAllowed);
    }
}
