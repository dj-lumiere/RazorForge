using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// An emitter kept in a local and made from a local container (<c>xs.emitter()</c>, <c>xs.where(...)</c>) is a token
/// on the container's elements: a change of the container's shape ends it (RF-S643 at a later use), and it cannot be
/// returned (RF-S644). One built from <c>steal</c> holds the elements itself.
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
                                      each x in it
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
                                      each b in big
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
                                      each t in taken
                                          show(f"{t}")
                                      return
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void Emitter_Returned_Errors()
    {
        string source = Prelude + """
                                  routine walk(xs: Viewing[List[S64]]) -> ListEmitter[S64]
                                      return xs.emitter()
                                  """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.SourceIterableKept);
    }
}
