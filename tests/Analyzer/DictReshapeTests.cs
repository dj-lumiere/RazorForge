using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// <c>d[k] = v</c> may add the key, so a map's <c>setitem</c> is <c>@reshaping</c>: writing a key of the map an
/// <c>each</c> loop is going through is RF-S625, directly, through a field, through a routine that writes a key of
/// its parameter, and as a compound write. A list element write and a write after the loop stay allowed.
/// </summary>
public class DictReshapeTests
{
    [Theory]
    [InlineData("""
                routine test()
                    var stock = Dict[Text, S64]()
                    each entry in stock
                        stock["kiwi"] = 1
                    return
                """)]
    [InlineData("""
                routine test()
                    var stock = Dict[Text, S64]()
                    each entry in stock
                        stock["kiwi"] += 1
                    return
                """)]
    [InlineData("""
                routine restock(d: Modifying[Dict[Text, S64]])
                    d["kiwi"] = 1
                    return
                routine test()
                    var stock = Dict[Text, S64]()
                    each entry in stock
                        restock(d: stock.modify())
                    return
                """)]
    [InlineData("""
                entity Shop
                    stock: Dict[Text, S64]
                routine Shop.restock_all()
                    each entry in me.stock
                        me.stock["kiwi"] = 1
                    return
                """)]
    public void Analyze_KeyWriteWhileLoopingOverTheMap_ReportsReshapingDuringIteration(string source)
    {
        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReshapingDuringIteration);
    }

    [Fact]
    public void Analyze_ElementWriteAndWriteAfterTheLoop_NoError()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine test()
                                                      var stock = Dict[Text, S64]()
                                                      var xs = List[S64]()
                                                      each x in xs
                                                          xs[0] = 5
                                                      each entry in stock
                                                          pass
                                                      stock["after"] = 2
                                                      return
                                                  """);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ReshapingDuringIteration);
    }
}
