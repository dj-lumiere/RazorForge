namespace RazorForge.Tests.Parser;

using static TestHelpers;

/// <summary>A spelling 0.5 retired is a build error that names what is written now.</summary>
public class RetiredSpellingTests
{
    [Theory]
    [InlineData("discard f()", "'discard' is now 'drop'")]
    [InlineData("var r = grab f()", "'grab' is now 'check'")]
    [InlineData("var l = lookup f()", "'lookup' is gone")]
    [InlineData("var b = 1 notin xs", "'xs lack x'")]
    [InlineData("unless true", "'if not (condition)'")]
    [InlineData("absent", "'absent' is gone")]
    public void Parse_RetiredWord_NamesTheFix(string statement, string fix)
    {
        string source = $"""
                         routine f() -> S64
                             return 1

                         routine start()
                             var xs = List[S64]()
                             {statement}
                             return
                         """;
        (SyntaxTree.Program _, Builder.Parser.Parser parser) = ParseWithErrors(source: source, fileName: "retired");
        Assert.Contains(collection: parser.GetErrors(), filter: e => e.Contains(value: fix));
    }

    [Fact]
    public void Parse_RetiredWordAsAName_IsAName()
    {
        AssertParses(source: """
                             routine start()
                                 var discard = 1
                                 var but = discard + 1
                                 return
                             """);
    }

    [Theory]
    [InlineData("var c: Character = 0", "It is called 'Char' now")]
    [InlineData("var e = d.entries()", "It is called 'items' now")]
    [InlineData("var q = xs.assign()", "'assign' is gone")]
    public void Analyze_RenamedName_NamesTheNewName(string statement, string fix)
    {
        AssertHasErrorSa(source: $"""
                                  routine start()
                                      var xs = List[S64]()
                                      var d = Dict[S64, S64]()
                                      {statement}
                                      return
                                  """,
            expectedErrorSubstring: fix);
    }
}
