using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Messages and small behaviors fixed together before 0.5: one error per cause, the error at the line the
/// user wrote, and the words naming what the user wrote.
/// </summary>
public sealed class BListMessageTests
{
    /// <summary>A routine with three parameters called positionally reports RF-S510 once, not once per argument.</summary>
    [Fact]
    public void Analyze_PositionalCallOfThreeParameters_ReportsNamedArgumentOnce()
    {
        string source = """
                        routine add3(a: S64, b: S64, c: S64) -> S64
                            return a + b + c

                        routine start()
                            var total = add3(1, 2, 3)
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Single(collection: result.Errors.Where(predicate: e =>
            e.Code == SemanticDiagnosticCode.NamedArgumentRequired));
    }

    /// <summary>Assigning to a preset points at the line that assigns, not at the preset's own line.</summary>
    [Fact]
    public void Analyze_AssignToPreset_PointsAtTheUse()
    {
        string source = """
                        preset LIMIT: S64 = 10

                        routine start()
                            LIMIT = 5
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        SemanticError error = Assert.Single(collection: result.Errors.Where(predicate: e =>
            e.Code == SemanticDiagnosticCode.InvalidAssignmentTarget));
        Assert.Equal(expected: 4, actual: error.Location.Line);
    }

    /// <summary>A change through a Viewing token says the token is read-only and names the token.</summary>
    [Fact]
    public void Analyze_ChangeThroughViewingToken_NamesTheReadOnlyToken()
    {
        string source = """
                        routine start()
                            var xs: List[S64] = [1, 2]
                            viewing xs as v
                                v.add_last(3)
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        SemanticError error = Assert.Single(collection: result.Errors);
        Assert.Contains(expectedSubstring: "'v' is a Viewing token, which is read-only", actualString: error.Message);
    }

    /// <summary>The reshaping ban names the call the user wrote (<c>bag.put()</c>), not the path inside it.</summary>
    [Fact]
    public void Analyze_ReshapeThroughEntityRoutine_NamesTheWrittenCall()
    {
        string source = """
                        entity Bag
                            items: List[S64]

                        routine Bag.put(x: S64)
                            me.items.add_last(x)
                            return

                        routine start()
                            var bag = Bag(items: [1, 2])
                            for k in bag.items
                                bag.put(5)
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        SemanticError error = Assert.Single(collection: result.Errors.Where(predicate: e =>
            e.Code == SemanticDiagnosticCode.ReshapingDuringIteration));
        Assert.Contains(expectedSubstring: "call 'bag.put()'", actualString: error.Message);
        Assert.DoesNotContain(expectedSubstring: "bag.items.put()", actualString: error.Message);
    }

    /// <summary>A keyword written where a name goes is named in the error.</summary>
    [Fact]
    public void Parse_KeywordAsVariableName_NamesTheKeyword()
    {
        string source = """
                        routine start()
                            var everywhere: S64 = 1
                            return
                        """;

        (_, Builder.Parser.Parser parser) = ParseWithErrors(source: source);
        Assert.Contains(collection: parser.GetErrors(),
            filter: e => e.Contains(value: "'everywhere' is a keyword"));
    }

    /// <summary><c>inject</c> is a keyword only after <c>steal place as name</c>, a plain name elsewhere.</summary>
    [Fact]
    public void Parse_InjectAsVariableName_Parses()
    {
        string source = """
                        routine start()
                            var inject: S64 = 2
                            return
                        """;

        AssertParses(source: source);
    }

    /// <summary>An annotation before a local variable says where annotations go.</summary>
    [Fact]
    public void Parse_AnnotationOnLocal_SaysAnnotationsGoOnDeclarations()
    {
        string source = """
                        routine start()
                            @initonly
                            var x: S64 = 1
                            return
                        """;

        (_, Builder.Parser.Parser parser) = ParseWithErrors(source: source);
        Assert.Contains(collection: parser.GetErrors(),
            filter: e => e.Contains(value: "An annotation (`@name`) goes before a routine"));
    }
}
