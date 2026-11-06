using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Contains tests for type prohibition.
/// </summary>
public class TypeProhibitionTests
{
    #region None as Type Argument (rejected)

    /// <summary>
    /// `None?` desugars to Optional[None], which COLLAPSES to Bool (present|absent, no payload) — no error.
    /// </summary>
    [Fact]
    public void Analyze_NoneNullable_CollapsesToBool()
    {
        // None? desugars to Optional[None] ≡ Bool (carrier None-collapse)
        string source = """
                        routine foo(x: None?)
                            nothing
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.NoneAsTypeArgument);
    }
    /// <summary>
    /// `Optional[None]` COLLAPSES to Bool (present|absent with no payload = a 2-state) — no error.
    /// </summary>
    [Fact]
    public void Analyze_ExplicitMaybeNone_CollapsesToBool()
    {
        string source = """
                        routine bar() -> Optional[None]
                            throw AbsentValueError()
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.NoneAsTypeArgument);
    }

    #endregion

    #region Normal nullable types (allowed)

    /// <summary>
    /// Verifies semantic analysis behavior for normal nullable without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_NormalNullable_NoErrors()
    {
        string source = """
                        routine foo(x: S32?)
                            nothing
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.NoneAsTypeArgument);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for blank direct type without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_NoneDirectType_NoErrors()
    {
        // None as a direct type (not wrapped in a generic) is fine
        string source = """
                        routine foo() -> None
                            nothing
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.NoneAsTypeArgument);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for result blank without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_ResultNone_NoErrors()
    {
        // Result<None> is allowed for failable void routines
        string source = """
                        routine foo(x: Check[None])
                            nothing
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.NoneAsTypeArgument);
    }
    #endregion

    #region Nested Optional Prohibition

    /// <summary>
    /// Verifies semantic analysis behavior for nested maybe and reports the expected error.
    /// </summary>
    [Fact]
    public void Analyze_NestedMaybe_ReportsError()
    {
        string source = """
                        routine test(x: Optional[Optional[S32]])
                            nothing
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.NestedMaybeProhibited);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for single maybe without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_SingleMaybe_NoError()
    {
        string source = """
                        routine test(x: S32?)
                            nothing
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.NestedMaybeProhibited);
    }

    /// <summary>
    /// <c>Optional[T]</c> and <c>T?</c> build over a value and over an entity alike, and the name <c>Optional</c> is gone:
    /// writing it is an unknown type that names <c>Optional[T]</c> and <c>T?</c>.
    /// </summary>
    [Fact]
    public void Analyze_OptionalOverAnyKind_NoError_MaybeNamesOptional()
    {
        string source = """
                        entity Cell
                            n: S64

                        routine test(a: Optional[S64], b: Optional[Cell], c: Cell?, d: S64?)
                            nothing
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);

        AnalysisResult old = AnalyzeSa(source: """
                                               routine test()
                                                   var a: Maybe[S64] = 3
                                                   return
                                               """);
        Assert.Contains(collection: old.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.UnknownType && e.Message.Contains(value: "'Optional[T]'") &&
                         e.Message.Contains(value: "'T?'"));
    }

    #endregion

    #region Additional nullable and collection prohibitions

    /// <summary>
    /// Verifies that Text? (nullable Text) is allowed and produces no error.
    /// </summary>
    [Fact]
    public void Analyze_TextNullable_NoError()
    {
        string source = """
                        routine find(text: Text?) -> Text
                            if text is None
                                throw AbsentValueError()
                            return text
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.NoneAsTypeArgument);
    }

    /// <summary>
    /// Verifies that Optional[S32?] (explicit nested Optional) is rejected.
    /// </summary>
    [Fact]
    public void Analyze_ExplicitNestedMaybe_ReportsError()
    {
        string source = """
                        routine test(x: Optional[S32?])
                            nothing
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.NestedMaybeProhibited);
    }

    #endregion

    #region Byte Literals

    /// <summary>
    /// A character with no ASCII form is more than one byte, so a Byte refuses it.
    /// </summary>
    [Theory]
    [InlineData("var x: Byte = 'é'")]
    [InlineData("var x = Byte('é')")]
    public void Analyze_ByteLiteralNonAscii_ReportsError(string declaration)
    {
        string source = $"routine test()\n    {declaration}\n    return\n";

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.NonAsciiByte);
    }

    /// <summary>
    /// A plain character is a Byte where a Byte goes, and so is a Byte('x') call or a hex escape.
    /// </summary>
    [Theory]
    [InlineData("var x: Byte = 'a'")]
    [InlineData("var x: Byte = '\\xE9'")]
    [InlineData("var x = Byte('a')")]
    [InlineData("var x = Byte('\\xFF')")]
    public void Analyze_ByteLiteralAscii_NoErrors(string declaration)
    {
        string source = $"routine test()\n    {declaration}\n    return\n";

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    /// <summary>
    /// Text becomes bytes where a Bytes goes: hex escapes and non-ASCII characters are fine there.
    /// </summary>
    [Fact]
    public void Analyze_TextWhereBytesGo_NoErrors()
    {
        string source = "routine test()\n    var x: Bytes = \"a\\xFF한\"\n    return\n";

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    /// <summary>
    /// Text and a Char have no hex escape: the error points to \u.
    /// </summary>
    [Theory]
    [InlineData("var x = \"a\\xFF\"")]
    [InlineData("var x: Text = \"\\x41\"")]
    [InlineData("var x = '\\xE9'")]
    [InlineData("var x: Char = '\\x41'")]
    public void Analyze_HexEscapeInText_ReportsError(string declaration)
    {
        string source = $"routine test()\n    {declaration}\n    return\n";

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors, filter: e => e.Code == SemanticDiagnosticCode.HexEscapeInText);
    }

    #endregion
}
