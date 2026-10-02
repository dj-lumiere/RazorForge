using System.Runtime.CompilerServices;
using Builder.Formatting;
using TypeModel.Enums;

namespace RazorForge.Tests.Formatting;

/// <summary>
/// The source formatter: each <c>tests/Fixtures/Format/*.rf</c> input formats to its sibling
/// <c>.expected.txt</c>, formatting that output again changes nothing, and a file the formatter cannot reproduce
/// is refused with a reason instead of being rewritten.
/// </summary>
public sealed class FormatterTests
{
    private static readonly string FixturesDir = Path.Combine(path1: LocateRepoRoot(),
        path2: "tests",
        path3: "Fixtures",
        path4: "Format");

    /// <summary>The fixture inputs, by file name.</summary>
    public static IEnumerable<object[]> Fixtures()
    {
        return Directory.EnumerateFiles(path: FixturesDir, searchPattern: "*.rf")
                        .Select(selector: path => new object[] { Path.GetFileName(path: path) })
                        .OrderBy(keySelector: row => (string)row[0], comparer: StringComparer.Ordinal);
    }

    /// <summary>Verifies that a fixture formats to its expected text.</summary>
    [Theory]
    [MemberData(memberName: nameof(Fixtures))]
    public void Fixture_FormatsToExpected(string fixture)
    {
        string input = File.ReadAllText(path: Path.Combine(path1: FixturesDir, path2: fixture));
        string expected = File.ReadAllText(path: Path.Combine(path1: FixturesDir,
                                path2: Path.ChangeExtension(path: fixture, extension: ".expected.txt")))
                              .Replace(oldValue: "\r\n", newValue: "\n");

        FormatResult result = SourceFormatter.Format(source: input, fileName: fixture, language: Language.RazorForge);

        Assert.True(condition: result.Succeeded, userMessage: result.Refusal);
        Assert.Equal(expected: expected, actual: result.Output);
    }

    /// <summary>Verifies that formatted text is a fixed point: formatting it again changes nothing.</summary>
    [Theory]
    [MemberData(memberName: nameof(Fixtures))]
    public void Fixture_FormattingIsIdempotent(string fixture)
    {
        string input = File.ReadAllText(path: Path.Combine(path1: FixturesDir, path2: fixture));
        FormatResult once = SourceFormatter.Format(source: input, fileName: fixture, language: Language.RazorForge);
        Assert.True(condition: once.Succeeded, userMessage: once.Refusal);

        FormatResult twice = SourceFormatter.Format(source: once.Output!, fileName: fixture,
            language: Language.RazorForge);

        Assert.True(condition: twice.Succeeded, userMessage: twice.Refusal);
        Assert.Equal(expected: once.Output, actual: twice.Output);
    }

    /// <summary>Verifies that a file that does not parse is refused, not rewritten.</summary>
    [Fact]
    public void Refuses_FileThatDoesNotParse()
    {
        const string source = "routine start(\n    return\n";

        FormatResult result = SourceFormatter.Format(source: source, fileName: "broken.rf",
            language: Language.RazorForge);

        Assert.False(condition: result.Succeeded);
        Assert.Contains(expectedSubstring: "does not parse", actualString: result.Refusal);
    }

    /// <summary>
    /// Verifies that two comments inside one statement the formatter prints on one line are refused: keeping
    /// both would put two comments on one line.
    /// </summary>
    [Fact]
    public void Refuses_TwoCommentsInsideOneStatement()
    {
        const string source = """
                              routine start()
                                  var total = add(  # first
                                      a: 1,  # second
                                      b: 2)
                                  return
                              """;

        FormatResult result = SourceFormatter.Format(source: source, fileName: "comments.rf",
            language: Language.RazorForge);

        Assert.False(condition: result.Succeeded);
        Assert.Contains(expectedSubstring: "several comments", actualString: result.Refusal);
    }

    /// <summary>Verifies that every comment of a file survives formatting, on the line it belongs to.</summary>
    [Fact]
    public void KeepsComment_InsideBrokenStatement()
    {
        const string source = """
                              routine start()
                                  var total = add(
                                      a: 1,
                                      b: 2)  # the sum
                                  return
                              """;

        FormatResult result = SourceFormatter.Format(source: source, fileName: "comment.rf",
            language: Language.RazorForge);

        Assert.True(condition: result.Succeeded, userMessage: result.Refusal);
        Assert.Contains(expectedSubstring: "    var total = add(a: 1, b: 2)  # the sum\n", actualString: result.Output);
    }

    private static string LocateRepoRoot([CallerFilePath] string thisFile = "")
    {
        // Walk up from the test assembly to the project folder that holds tests/ (it holds RazorForge.csproj).
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(value: dir))
        {
            if (File.Exists(path: Path.Combine(path1: dir, path2: "RazorForge.csproj")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(path: dir);
        }

        // A test assembly built outside the project folder finds it from this source file (tests/Formatting/).
        string? project = Path.GetDirectoryName(path: Path.GetDirectoryName(path: Path.GetDirectoryName(path: thisFile)));
        if (project != null && File.Exists(path: Path.Combine(path1: project, path2: "RazorForge.csproj")))
        {
            return project;
        }

        throw new InvalidOperationException(message: "Could not locate the RazorForge project folder.");
    }
}
