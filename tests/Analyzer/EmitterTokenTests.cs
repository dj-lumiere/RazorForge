using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// An emitter kept in a local and made from a local container (<c>xs.emitter()</c>, <c>xs.where(...)</c>) is a token
/// on the container's elements: a change of the container's shape ends it (RF-S643 at a later use), and it is returned
/// only when made from <c>me</c> or a token parameter (RF-S644 otherwise). One built from <c>steal</c> holds the
/// elements itself.
/// </summary>
public class EmitterTokenTests
{
    private const string Prelude = """
                                   import IO/Console
                                   import IterTools

                                   """;

    [Fact]
    public void Emitter_KeptInLocal_IsAllowed()
    {
        string source = Prelude + """
                                  routine start()
                                      var xs: List[S64] = [1, 2, 3]
                                      var it = xs.emitter()
                                      show(f"{it.emit()}")
                                      for x in it
                                          show(f"{x}")
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void Emitter_UsedAfterReshape_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var xs: List[S64] = [1, 2, 3]
                                      var it = xs.emitter()
                                      xs.add_last(value: 4)
                                      show(f"{it.emit()}")
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenUsedAfterSourceChanged);
    }

    [Fact]
    public void Adapter_UsedAfterReshape_Errors()
    {
        string source = Prelude + """
                                  routine start()
                                      var xs: List[S64] = [1, 2, 3]
                                      var big = xs.where(predicate: v => v > 1)
                                      xs.add_last(value: 4)
                                      for b in big
                                          show(f"{b}")
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenUsedAfterSourceChanged);
    }

    [Fact]
    public void Emitter_FromSteal_OutlivesTheName()
    {
        string source = Prelude + """
                                  routine start()
                                      var xs: List[S64] = [1, 2, 3]
                                      var taken = (steal xs).emitter()
                                      xs = [9]
                                      for t in taken
                                          show(f"{t}")
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void Emitter_ReturnedFromTokenParameter_IsAllowed()
    {
        string source = Prelude + """
                                  routine walk(xs: Viewing[List[S64]]) -> ListEmitter[S64]
                                      return xs.emitter()

                                  routine big(xs: Viewing[List[S64]]) -> WhereEmittable[S64, List[S64]]
                                      return xs.where(predicate: v => v > 1)

                                  routine start()
                                      var xs: List[S64] = [1, 2, 3]
                                      var it = walk(xs: xs)
                                      show(f"{it.emit()}")
                                      for b in big(xs: xs)
                                          show(f"{b}")
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void Emitter_ReturnedFromLocal_Errors()
    {
        string source = Prelude + """
                                  routine walk() -> ListEmitter[S64]
                                      var xs: List[S64] = [1, 2, 3]
                                      return xs.emitter()
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.SourceIterableKept);
    }

    [Fact]
    public void Emitter_Returned_EndsWithTheCallersContainer()
    {
        string source = Prelude + """
                                  routine walk(xs: Viewing[List[S64]]) -> ListEmitter[S64]
                                      return xs.emitter()

                                  routine start()
                                      var xs: List[S64] = [1, 2, 3]
                                      var it = walk(xs: xs)
                                      xs.add_last(4)
                                      drop it.emit()
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenUsedAfterSourceChanged);
    }

    [Fact]
    public void TakingEmitter_Returned_IsUsedByTheCaller()
    {
        string source = Prelude + """
                                  routine taken() -> ListTakingEmitter[Text]
                                      var names: List[Text] = ["a", "b"]
                                      return (steal names).emitter()

                                  routine start()
                                      var t = taken()
                                      for n in t
                                          show(n)
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void RetainedEmitter_Returned_IsAllowed()
    {
        // `r.emitter()` holds one more reference to the list, and its element type comes from the list's
        // conformance (`List[S64]` obeys `Emittable[S64]`), so nothing in the call names it.
        string source = Prelude + """
                                  routine walk() -> RetainedEmitter[S64, ListEmitter[S64], List[S64]]
                                      var r = Retained[List[S64]](from: [1, 2, 3])
                                      return r.emitter()

                                  routine start()
                                      var it = walk()
                                      for x in it
                                          show(f"{x}")
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void EmitterOfRetainedsEntity_Returned_Errors()
    {
        // Made from the entity itself (`r.view()`), the emitter is a token on it, as one made from a local.
        string source = Prelude + """
                                  routine walk() -> ListEmitter[S64]
                                      var r = Retained[List[S64]](from: [1, 2, 3])
                                      return r.view().emitter()
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.SourceIterableKept);
    }
}
