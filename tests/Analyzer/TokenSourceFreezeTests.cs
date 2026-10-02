using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Tests for token sources. A token remembers the object it was taken from. Reassigning or stealing that
/// object ends the token, and using the token afterwards is RF-S643 at the use. Inside one call, handing over
/// a token while the same call moves its source out is RF-S639 at the call. Other bindings, and the source
/// itself once the token is no longer used, stay free.
/// </summary>
public class TokenSourceFreezeTests
{
    private const string Prelude = """
                                   import IO/Console

                                   entity Box
                                       n: S64

                                   routine Box.bump()
                                       me.n = me.n + 1
                                       return

                                   routine take(m: Modifying[Box], b: Box)
                                       m.bump()
                                       return

                                   """;

    [Fact]
    public void Analyze_ReassignSourceInsideUsing_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var a = Box(n: 1)
                                      using a.modify() as m
                                          a = Box(n: 50)
                                          m.bump()
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "You are using the token 'm'");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenUsedAfterSourceChanged);
    }

    [Fact]
    public void Analyze_StealSourceInsideUsing_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var a = Box(n: 1)
                                      using a.view() as v
                                          var b = steal a
                                          show(f"{v.n}")
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "You are using the token 'v'");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenUsedAfterSourceChanged);
    }

    [Fact]
    public void Analyze_StealSourceInSameCallAsInlineToken_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var a = Box(n: 1)
                                      take(m: a.modify(), b: steal a)
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenSourceReplaced);
    }

    [Fact]
    public void Analyze_ReassignSourceAfterUsing_Ok()
    {
        string source = Prelude + """
                                  routine start()
                                      var a = Box(n: 1)
                                      using a.modify() as m
                                          m.bump()
                                      a = Box(n: 50)
                                      show(f"{a.n}")
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenSourceReplaced);
    }

    [Fact]
    public void Analyze_ReassignOtherBindingInsideUsing_Ok()
    {
        string source = Prelude + """
                                  routine start()
                                      var a = Box(n: 1)
                                      var b = Box(n: 2)
                                      using a.modify() as m
                                          b = Box(n: 9)
                                          m.bump()
                                      show(f"{a.n} {b.n}")
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenSourceReplaced);
    }

    [Fact]
    public void Analyze_ReshapeContainerWhileWritingItsElement_Errors()
    {
        // `boxes[0].n = ...` writes the element in place through a token into `boxes`; removing from
        // `boxes` in the same statement could free that element.
        string source = Prelude + """
                                  routine start()
                                      var boxes = List[Box]()
                                      boxes.add_last(value: Box(n: 1))
                                      boxes.add_last(value: Box(n: 2))
                                      boxes[0].n = boxes.remove_last().n
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "You are trying to call 'boxes.remove_last()'");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenSourceReplaced);
    }

    [Fact]
    public void Analyze_ReadContainerWhileCallingOnItsElement_Ok()
    {
        // A @readonly call on the container cannot move the element, so it may share the statement.
        string source = Prelude + """
                                  routine start()
                                      var grid = List[List[U64]]()
                                      grid.add_last(value: List[U64]())
                                      grid[0].add_last(value: grid.count())
                                      show(f"{grid[0][0]}")
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenSourceReplaced);
    }

    [Fact]
    public void Analyze_InlineTokenCallWithoutSteal_Ok()
    {
        string source = Prelude + """
                                  routine start()
                                      var a = Box(n: 1)
                                      a.modify().bump()
                                      a = Box(n: 3)
                                      show(f"{a.n}")
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenSourceReplaced);
    }
}
