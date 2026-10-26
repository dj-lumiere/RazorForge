#pragma warning disable CS1591
using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// The errors a previous-release spelling meets name what is written now: a renamed or removed type, a recovery
/// written as a <c>try_</c> / <c>check_</c> / <c>lookup_</c> name, a free routine that moved, and the warning for a
/// member routine named <c>create</c> that was the constructor spelling.
/// </summary>
public class ReleaseHintTests
{
    /// <summary>The messages of the errors <paramref name="source"/> gives, one string.</summary>
    private static string Errors(string source, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        AnalysisResult result = AnalyzeSa(source: source, fileName: name);
        return string.Join(separator: "\n", values: result.Errors.Select(selector: e => e.Message));
    }

    [Theory]
    [InlineData("Result[S64]", "It is called 'Check' now")]
    [InlineData("Lookup[S64]", "'Lookup[T]' is gone")]
    [InlineData("Q32", "It is called 'Q128' now")]
    [InlineData("Indexable[S64]", "It is called 'ViewingIndexable' now")]
    [InlineData("MutableIndexable[S64]", "It is called 'ModifyingIndexable' now")]
    [InlineData("Accessing[S64]", "'Accessing' is gone")]
    [InlineData("RetainController[S64]", "It is called 'RetainManager' now")]
    [InlineData("GuardController[S64]", "It is called 'GuardManager' now")]
    [InlineData("FrozenController", "'FrozenController' is gone")]
    [InlineData("PQEntry[S64, S64]", "'PQEntry' is gone")]
    [InlineData("Agent[S64]", "a 'Work' now")]
    [InlineData("AgentStartError", "It is called 'WorkStartError' now")]
    [InlineData("Atomic[S64]", "It is called 'Unbroken' now (migrate --step type-names rewrites it)")]
    [InlineData("CircularList[S64]", "It is called 'RotatableList' now (migrate --step type-names rewrites it)")]
    [InlineData("MemoryAllocationError", "It is called 'OutOfMemoryError' now (migrate --step type-names rewrites it)")]
    [InlineData("Integer", "It is called 'Int' now (migrate --step type-names rewrites it)")]
    public void RetiredType_NamesWhatIsWrittenNow(string type, string hint)
    {
        string errors = Errors(source: $"""
                                        routine take(x: {type}) -> S64
                                            return 0

                                        routine start()
                                            return
                                        """);
        Assert.Contains(expectedSubstring: hint, actualString: errors);
    }

    [Theory]
    [InlineData("try_halve(n: 4)", "write 'try halve(...)'")]
    [InlineData("check_halve(n: 4)", "write 'check halve(...)'")]
    [InlineData("lookup_halve(n: 4)", "write 'check halve(...)'")]
    [InlineData("gather(of: 1)", "'jobs.gather()'")]
    [InlineData("hollow[S64]()", "'T.hollow()'")]
    [InlineData("blank[S64]()", "'blank[T]()' is gone")]
    public void RetiredRoutine_NamesWhatIsWrittenNow(string call, string hint)
    {
        string errors = Errors(source: $"""
                                        failable routine halve(n: S64) -> S64
                                            return n // 2

                                        routine start()
                                            var r = {call}
                                            return
                                        """);
        Assert.Contains(expectedSubstring: hint, actualString: errors);
    }

    [Fact]
    public void UnknownPrefixedName_WithoutTheRoutine_GetsNoRecoveryHint()
    {
        string errors = Errors(source: """
                                       routine start()
                                           var r = try_nothing_here(n: 4)
                                           return
                                       """);
        Assert.DoesNotContain(expectedSubstring: "Recovery is a keyword", actualString: errors);
    }

    [Fact]
    public void MemberRecoveryPrefix_NamesTheKeyword()
    {
        string errors = Errors(source: """
                                       routine S64.halve() -> S64
                                           return me // 2

                                       routine start()
                                           var x: S64 = 4
                                           var r = x.try_halve()
                                           return
                                       """);
        Assert.Contains(expectedSubstring: "write 'try x.halve(...)'", actualString: errors);
    }

    [Fact]
    public void MomentNow_NamesTheClock()
    {
        string errors = Errors(source: """
                                       routine start()
                                           var m = Moment.now()
                                           return
                                       """);
        Assert.Contains(expectedSubstring: "'Clock.now()'", actualString: errors);
    }

    [Fact]
    public void CreateReturningItsOwnType_IsAWarning()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  record Pt
                                                      x: S64

                                                  routine Pt.create(x: S64) -> Pt
                                                      return Pt(x: x)

                                                  routine start()
                                                      var p = Pt(x: 1)
                                                      return
                                                  """);
        Assert.Contains(collection: result.Warnings,
            filter: w => w.Code == SemanticWarningCode.CreateIsNotConstructor &&
                         w.Message.Contains(value: "declare it 'routine Pt(...)'"));
    }
}
