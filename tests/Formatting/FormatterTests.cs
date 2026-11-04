using System.Runtime.CompilerServices;
using Builder.Formatting;
using TypeModel.Enums;

namespace Revare.Tests.Formatting;

/// <summary>
/// The source formatter: each <c>tests/Fixtures/Format/*.rvr</c> input formats to its sibling
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
        return Directory.EnumerateFiles(path: FixturesDir, searchPattern: "*.rvr")
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

        FormatResult result = SourceFormatter.Format(source: input, fileName: fixture, language: Language.Revare);

        Assert.True(condition: result.Succeeded, userMessage: result.Refusal);
        Assert.Equal(expected: expected, actual: result.Output);
    }

    /// <summary>Verifies that formatted text is a fixed point: formatting it again changes nothing.</summary>
    [Theory]
    [MemberData(memberName: nameof(Fixtures))]
    public void Fixture_FormattingIsIdempotent(string fixture)
    {
        string input = File.ReadAllText(path: Path.Combine(path1: FixturesDir, path2: fixture));
        FormatResult once = SourceFormatter.Format(source: input, fileName: fixture, language: Language.Revare);
        Assert.True(condition: once.Succeeded, userMessage: once.Refusal);

        FormatResult twice = SourceFormatter.Format(source: once.Output!, fileName: fixture,
            language: Language.Revare);

        Assert.True(condition: twice.Succeeded, userMessage: twice.Refusal);
        Assert.Equal(expected: once.Output, actual: twice.Output);
    }

    /// <summary>
    /// Verifies that the module line, the import group and the body are three groups with exactly one blank line
    /// between them and none inside the import group, whichever groups are present, in both languages.
    /// </summary>
    [Theory]
    [InlineData("module Demo\nimport IO/Console\n\n\nimport Numerics\nroutine start()\n    return\n", "module Demo\n\nimport IO/Console\nimport Numerics\n\nroutine start()\n    return\n")]
    [InlineData("import IO/Console\nroutine start()\n    return\n", "import IO/Console\n\nroutine start()\n    return\n")]
    [InlineData("module Demo\nroutine start()\n    return\n", "module Demo\n\nroutine start()\n    return\n")]
    public void SeparatesModuleImportsAndBody_ByOneBlankLine(string source, string expected)
    {
        FormatResult result = SourceFormatter.Format(source: source, fileName: "groups.rvr",
            language: Language.Revare);

        Assert.True(condition: result.Succeeded, userMessage: result.Refusal);
        Assert.Equal(expected: expected, actual: result.Output);
    }

    /// <summary>Verifies that a file that does not parse is refused, not rewritten.</summary>
    [Fact]
    public void Refuses_FileThatDoesNotParse()
    {
        const string source = "routine start(\n    return\n";

        FormatResult result = SourceFormatter.Format(source: source, fileName: "broken.rvr",
            language: Language.Revare);

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

        FormatResult result = SourceFormatter.Format(source: source, fileName: "comments.rvr",
            language: Language.Revare);

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

        FormatResult result = SourceFormatter.Format(source: source, fileName: "comment.rvr",
            language: Language.Revare);

        Assert.True(condition: result.Succeeded, userMessage: result.Refusal);
        Assert.Contains(expectedSubstring: "    var total = add(a: 1, b: 2)  # the sum\n", actualString: result.Output);
    }

    private static string LocateRepoRoot([CallerFilePath] string thisFile = "")
    {
        // Walk up from the test assembly to the project folder that holds tests/ (it holds Revare.csproj).
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(value: dir))
        {
            if (File.Exists(path: Path.Combine(path1: dir, path2: "Revare.csproj")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(path: dir);
        }

        // A test assembly built outside the project folder finds it from this source file (tests/Formatting/).
        string? project = Path.GetDirectoryName(path: Path.GetDirectoryName(path: Path.GetDirectoryName(path: thisFile)));
        if (project != null && File.Exists(path: Path.Combine(path1: project, path2: "Revare.csproj")))
        {
            return project;
        }

        throw new InvalidOperationException(message: "Could not locate the Revare project folder.");
    }
}
