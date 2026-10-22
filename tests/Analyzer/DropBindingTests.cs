using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// <c>drop x</c> tears a local binding the routine owns down where it stands and ends the name, as <c>steal x</c>
/// does. A Work is an entity with one owner, and a started one nothing collects is warning RF-W009.
/// </summary>
public class DropBindingTests
{
    private const string Prelude = """
                                   entity Node
                                       value: S64

                                   routine work() -> S64
                                       return 1

                                   """;

    [Fact]
    public void Analyze_DropThenUse_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine start()
                                                                       var n = Node(value: 1)
                                                                       drop n
                                                                       var v = n.value
                                                                       return
                                                                   """,
            expectedErrorSubstring: "was ended by 'drop n'");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.UseAfterSteal);
    }

    [Fact]
    public void Analyze_DropOwnedLocalAndParameter_Passes()
    {
        AssertAnalyzesSa(source: Prelude + """
                                           routine finish(n: Node)
                                               drop n
                                               return

                                           routine start()
                                               var n = Node(value: 1)
                                               var m = Node(value: 2)
                                               drop m
                                               finish(n: steal n)
                                               return
                                           """);
    }

    [Fact]
    public void Analyze_DropOnOneBranch_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine start()
                                                                       var n = Node(value: 1)
                                                                       if n.value > 0
                                                                           drop n
                                                                       return
                                                                   """,
            expectedErrorSubstring: "ended with 'drop' on some paths");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.StealOnSomePaths);
    }

    [Fact]
    public void Analyze_DropToken_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine start()
                                                                       var n = Node(value: 1)
                                                                       var v = n.view()
                                                                       drop v
                                                                       return
                                                                   """,
            expectedErrorSubstring: "is a Viewing token");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.InvalidDropTarget);
    }

    [Fact]
    public void Analyze_TokenUsedAfterItsSourceIsDropped_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine start()
                                                                       var n = Node(value: 1)
                                                                       var v = n.view()
                                                                       drop n
                                                                       var x = v.value
                                                                       return
                                                                   """,
            expectedErrorSubstring: "was ended by 'drop n'");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.TokenUsedAfterSourceChanged);
    }

    [Fact]
    public void Analyze_WorkCopy_IsRejected()
    {
        AnalysisResult result = AssertHasErrorSa(source: Prelude + """
                                                                   routine start()
                                                                       var job = work.to[Work]()
                                                                       var other = job
                                                                       drop other.retrieve()
                                                                       return
                                                                   """,
            expectedErrorSubstring: "with no manager");
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.BareEntityAssignment);
    }

    [Fact]
    public void Analyze_StartedWorkNothingCollects_Warns()
    {
        AnalysisResult result = AssertAnalyzesSa(source: Prelude + """
                                                                   routine start()
                                                                       var job = work.to[Work]()
                                                                       job.start_now()
                                                                       return
                                                                   """);
        Assert.Contains(collection: result.Warnings, filter: w => w.Code == SemanticWarningCode.WorkNeverCollected);
    }

    [Fact]
    public void Analyze_StartedWorkRetrievedOrDropped_DoesNotWarn()
    {
        AnalysisResult result = AssertAnalyzesSa(source: Prelude + """
                                                                   routine start()
                                                                       var kept = work.to[Work]()
                                                                       kept.start_now()
                                                                       when check kept.retrieve()
                                                                           is S64 as v => nothing
                                                                           else => nothing
                                                                       var stopped = work.to[Work]()
                                                                       stopped.start_now()
                                                                       drop stopped
                                                                       return
                                                                   """);
        Assert.DoesNotContain(collection: result.Warnings,
            filter: w => w.Code == SemanticWarningCode.WorkNeverCollected);
    }
}
