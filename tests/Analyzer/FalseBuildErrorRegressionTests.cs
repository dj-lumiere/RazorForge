using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Programs the analyzer once rejected although they are legal: a variant declared after a record that holds it, a
/// second change through a token taken from a token parameter, and <c>.to()</c> handed to a routine that also has a
/// token parameter. Also a generic type built with an argument name it has nowhere, which once passed analysis and
/// reached the LLVM emitter as an unfilled generic definition.
/// </summary>
public class FalseBuildErrorRegressionTests
{
    /// <summary>The record resolves its member variable before the variant's arms are known. The arms must reach the
    /// variant the record holds, so <c>is Slow</c> on the member can match.</summary>
    [Fact]
    public void Analyze_VariantDeclaredAfterHolder_ArmsMatch()
    {
        AssertAnalyzesSa(source: """
                                 import IO/Console

                                 record Holder
                                     inner: AnyService

                                 record Slow
                                     delay: S64

                                 variant AnyService
                                     Slow
                                     Text

                                 routine start()
                                     var b: AnyService = Slow(delay: 2)
                                     var h = Holder(inner: b)
                                     when h.inner
                                         is Slow as slow => show(f"slow {slow.delay}")
                                         is Text as t => show(t)
                                     return
                                 """);
    }

    /// <summary>A change made through a token changes what is inside its object, never the object itself, so the
    /// token stays usable for the next change.</summary>
    [Fact]
    public void Analyze_SecondCallThroughTokenFromTokenParameter_Builds()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  import IO/Console

                                                  entity Inner
                                                      values: Dict[Text, S64]

                                                  entity Outer
                                                      inner: Inner

                                                  routine Inner.put(key: Text)
                                                      me.values[key] = 1
                                                      return

                                                  routine Outer.part() -> Modifying[Inner]
                                                      return me.inner.modify()

                                                  routine fill(o: Modifying[Outer])
                                                      var part = o.part()
                                                      part.put(key: "a")
                                                      part.put(key: "b")
                                                      return

                                                  routine start()
                                                      var o = Outer(inner: Inner(values: Dict[Text, S64]()))
                                                      fill(o: o)
                                                      show(f"{o.inner.values.count()}")
                                                      return
                                                  """);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenUsedAfterSourceChanged);
    }

    /// <summary>A token taken from a token parameter still ends when what it was taken from is replaced.</summary>
    [Fact]
    public void Analyze_TokenFromTokenParameterAfterReplace_Errors()
    {
        AssertHasErrorSa(source: """
                                 import IO/Console

                                 entity Inner
                                     values: Dict[Text, S64]

                                 entity Outer
                                     inner: Inner

                                 routine Inner.put(key: Text)
                                     me.values[key] = 1
                                     return

                                 routine Outer.part() -> Modifying[Inner]
                                     return me.inner.modify()

                                 routine fill(o: Modifying[Outer])
                                     var part = o.part()
                                     o.inner = Inner(values: Dict[Text, S64]())
                                     part.put(key: "b")
                                     return

                                 routine start()
                                     var o = Outer(inner: Inner(values: Dict[Text, S64]()))
                                     fill(o: o)
                                     return
                                 """,
            expectedErrorSubstring: "You are using the token 'part'");
    }

    /// <summary>The value handed to a token parameter becomes the token at the call, which is no reason to look for
    /// another overload, so <c>n: w.to()</c> takes its type from the routine's <c>n</c>.</summary>
    [Fact]
    public void Analyze_InferredConversionBesideTokenParameter_Builds()
    {
        AssertAnalyzesSa(source: """
                                 import IO/Console

                                 routine one(n: S32, xs: Viewing[List[S32]]) -> S32
                                     return n

                                 routine start()
                                     var w = "5"
                                     var xs = List[S32]()
                                     show(one(n: w.to(), xs: xs))
                                     return
                                 """);
    }

    /// <summary>A generic type built with an argument name that is neither one of its member variables nor a
    /// creator parameter is an error where it is written.</summary>
    [Fact]
    public void Analyze_GenericTypeWithUnknownArgumentName_Errors()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  import IO/Console

                                                  entity Session
                                                      n: S64

                                                  routine start()
                                                      var w = Watched(from: Session(n: 1))
                                                      return
                                                  """);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.UnknownNamedArgument && e.Message.Contains(value: "'from'"));
    }
}
