using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Tests for the analysis of <c>viewing</c> / <c>modifying</c> blocks and of a Guarded's <c>view()</c> /
/// <c>modify()</c> passed to a call: opening a Guarded again inside a block of the same routine (RF-S630, for reading
/// and for writing), naming a target twice (RF-S650), a target the block can't open that way (RF-S651),
/// <c>try</c> on a target that can't be tried (RF-S611), the access call anywhere but passed to a call (RF-S652), and
/// the token of a block handed to an agent (RF-S632).
/// </summary>
public class AccessBlockAnalysisTests
{
    private const string Prelude = """
                                   import IO/Console
                                   import BuilderQuery

                                   entity Counter
                                       value: S64

                                   """;

    [Theory]
    [InlineData("modifying", "modifying")]
    [InlineData("modifying", "viewing")]
    [InlineData("viewing", "modifying")]
    [InlineData("viewing", "viewing")]
    public void Analyze_NestedOpen_SameHandle_Errors(string outer, string inner)
    {
        string source = Prelude + $$"""
                                    routine start()
                                        var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                        {{outer}} s as a
                                            {{inner}} s as b
                                                show("nested")
                                        return
                                    """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: $"inside a '{outer}' block");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.GuardedOpenedAgain);
    }

    [Fact]
    public void Analyze_NestedOpen_DifferentHandles_Ok()
    {
        string source = Prelude + """
                                  routine start()
                                      var s1 = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      var s2 = Guarded[Counter, MultiRead](from: Counter(value: 2))
                                      modifying s1 as c1
                                          modifying s2 as c2
                                              show("two handles")
                                      return
                                  """;

        Assert.Empty(collection: AnalyzeSa(source: source).Errors);
    }

    [Fact]
    public void Analyze_SequentialOpen_SameHandle_Ok()
    {
        string source = Prelude + """
                                  routine Counter.bump(inc: S64)
                                      me.value = me.value + inc
                                      return

                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      modifying s as c1
                                          c1.bump(inc: 1)
                                      modifying s as c2
                                          c2.bump(inc: 1)
                                      return
                                  """;

        Assert.Empty(collection: AnalyzeSa(source: source).Errors);
    }

    [Fact]
    public void Analyze_NestedOpen_AliasedHandle_Errors()
    {
        // `s2 = s` is a second handle to the same entity: opening it inside a block that has `s` open is opening the
        // same Guarded again.
        string source = Prelude + """
                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      var s2 = s
                                      modifying s as c
                                          viewing s2 as v
                                              show("aliased reader")
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: "under another name");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.GuardedOpenedAgain);
    }

    [Fact]
    public void Analyze_AccessArgumentInsideBlock_SameHandle_Errors()
    {
        string source = Prelude + """
                                  routine peek(c: Viewing[Counter]) -> S64
                                      return c.value

                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      modifying s as c
                                          show(peek(c: s.view()))
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: "already has 's' open");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.GuardedOpenedAgain);
    }

    [Fact]
    public void Analyze_TargetNamedTwice_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      modifying s, viewing s as again
                                          pass
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: "You name 's' twice");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.AccessTargetNamedTwice);
    }

    [Theory]
    [InlineData("Exclusive", "viewing", "has only 'modifying'")]
    [InlineData("ReadOnly", "modifying", "which nothing may change")]
    public void Analyze_VerbThePolicyLacks_Errors(string policy, string verb, string message)
    {
        string source = Prelude + $$"""
                                    routine start()
                                        var s = Guarded[Counter, {{policy}}](from: Counter(value: 1))
                                        {{verb}} s as c
                                            pass
                                        return
                                    """;

        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: message);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.AccessTargetNotOpenable);
    }

    [Fact]
    public void Analyze_TargetWithoutName_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var xs = List[Guarded[Counter, Exclusive]]()
                                      modifying xs[0]
                                          pass
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: "Give it one with 'as'");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.AccessTargetNotOpenable);
    }

    [Fact]
    public void Analyze_TryOnToken_Errors()
    {
        string source = Prelude + """
                                  routine bump(c: Modifying[Counter])
                                      try modifying c
                                          c.value += 1
                                      else
                                          pass
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: "opening it never waits");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.TryAccessOnUntryable);
    }

    [Fact]
    public void Analyze_AccessCallKeptInVariable_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      var held = s.modify()
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: "isn't passed to a call");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.GuardAccessOutsideArgument);
    }

    [Fact]
    public void Analyze_AccessArgument_Ok()
    {
        string source = Prelude + """
                                  routine bump(c: Modifying[Counter])
                                      c.value += 1
                                      return

                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      bump(c: s.modify())
                                      var v = s.view().value
                                      return
                                  """;

        Assert.Empty(collection: AnalyzeSa(source: source).Errors);
    }

    [Fact]
    public void Analyze_AccessArgumentInCondition_Errors()
    {
        string source = Prelude + """
                                  routine big(c: Viewing[Counter]) -> Bool
                                      return c.value > 10

                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      if big(c: s.view())
                                          show("big")
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: "statement of its own");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.GuardAccessOutsideArgument);
    }

    [Fact]
    public void Analyze_LockBackedWriteTokenTwiceInOneCall_Errors()
    {
        // The token a `modifying` block binds is a plain Modifying: it is the one writer by where it came from.
        string source = Prelude + """
                                  routine two(a: Modifying[Counter], b: Modifying[Counter])
                                      a.value += 1
                                      b.value += 1
                                      return

                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      modifying s as c
                                          two(a: c, b: c)
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "the write token a 'modifying' block opened");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ExclusiveTokenDuplicate);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.SameContainerTwiceInCall);
    }

    [Fact]
    public void Analyze_BlockTokenToAgent_Errors()
    {
        string source = Prelude + """
                                  routine bump(c: Modifying[Counter]) -> S64
                                      c.value += 1
                                      return c.value

                                  routine start()
                                      var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                                      modifying s as c
                                          var job = bump.to[Agent](c: c)
                                          job.start_here()
                                          show(job.retrieve())
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "the access ends with the block");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.ThreadArgNotShareable);
    }

    [Fact]
    public void Analyze_ViewOnExclusive_SaysToModify()
    {
        string source = Prelude + """
                                  routine peek(c: Viewing[Counter]) -> S64
                                      return c.value

                                  routine start()
                                      var s = Guarded[Counter, Exclusive](from: Counter(value: 1))
                                      show(peek(c: s.view()))
                                      return
                                  """;

        AssertHasErrorSa(source: source, expectedErrorSubstring: "read it inside 'modifying s as m'");
    }

    [Fact]
    public void Analyze_UsingOnGuardedAccess_SaysToOpenWithBlock()
    {
        string source = Prelude + """
                                  routine start()
                                      var c = Counter(value: 1)
                                      using c.view() as v
                                          show(f"{v.value}")
                                      return
                                  """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "open it with 'viewing x as v'");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.UsingTargetMissingEnterExit);
    }

    [Fact]
    public void Analyze_TwoTokensOpenedTogether_MayChangeShapeWhileTheOtherIsWalked()
    {
        // Two tokens on the same kind of list may be the same list, unless one block opened both.
        string source = """
                        routine merge(into: Modifying[List[S64]], from: Viewing[List[S64]])
                            modifying into, viewing from
                                each v in from
                                    into.add_last(value: v)
                            return
                        """;

        Assert.Empty(collection: AnalyzeSa(source: source).Errors);
    }
}
