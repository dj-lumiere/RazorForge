using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Contains tests for text bytes interop.
/// </summary>
public class TextBytesInteropTests
{
    /// <summary>
    /// Verifies that the test validates literal assigns to character.
    /// </summary>
    [Fact]
    public void CharacterLiteral_AssignsToCharacter()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine test()
                                                      var ch: Char = 'A'
                                                      return
                                                  """);

        Assert.Empty(collection: result.Errors);
    }

    /// <summary>
    /// Verifies that the test validates indexing assigns to character.
    /// </summary>
    [Fact]
    public void TextIndexing_AssignsToCharacter()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine test()
                                                      var ch: Char = "Hello"[0]
                                                      return
                                                  """);

        Assert.Empty(collection: result.Errors);
    }

    /// <summary>
    /// Verifies that the test validates uses is alphabetic not is letter.
    /// </summary>
    [Fact]
    public void CharacterUsesIsAlphabetic_NotIsLetter()
    {
        AnalysisResult valid = AnalyzeSa(source: """
                                                 routine test(ch: Char)
                                                     var ok: Bool = ch.is_alphabetic()
                                                     return
                                                 """);
        Assert.Empty(collection: valid.Errors);

        AnalysisResult invalid = AnalyzeSa(source: """
                                                   routine test(ch: Char)
                                                       var bad: Bool = ch.is_letter()
                                                       return
                                                   """);
        // `is_letter()` calls a routine that does not exist on Char (the API is
        // `is_alphabetic`), so it is a memberRoutineNotFound — `.name()` is a routine call, distinct
        // from `.name` member access, and an unresolved routine call is RF-S458.
        Assert.Contains(collection: invalid.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.MemberRoutineNotFound);
    }

    /// <summary>
    /// Verifies that the test validates encode and bytes decode utf8 analyze.
    /// </summary>
    [Fact]
    public void TextEncodeAndBytesDecodeUtf8_Analyze()
    {
        // decode_as_utf8 is failable (strict validation) → `decode_as_utf8!()` from a failable
        // routine; the non-failable counterpart is `decode_as_utf8_lossy()`. RF-S458 otherwise.
        AnalysisResult result = AnalyzeSa(source: """
                                                  failable routine test()
                                                      var text: Text = "Hello, 계"
                                                      var bytes: Bytes = text.encode_as_utf8()
                                                      var roundtrip: Text = bytes.decode_as_utf8()
                                                      return
                                                  """);

        Assert.Empty(collection: result.Errors);
    }

    /// <summary>
    /// Verifies that the test validates interpret as utf8 produces characters.
    /// </summary>
    [Fact]
    public void BytesInterpretAsUtf8_ProducesCharacters()
    {
        // interpret_as_utf8 is failable (strict UTF-8 validation), so it must be called as
        // `interpret_as_utf8!()` from a failable routine — the non-failable `interpret_as_utf8()`
        // form does not exist (the lossy counterpart is `interpret_as_utf8_lossy()`). RF-S458.
        AnalysisResult result = AnalyzeSa(source: """
                                                  failable routine test()
                                                      var bytes: Bytes = "Hi".encode_as_utf8()
                                                      for ch in bytes.interpret_as_utf8()
                                                          var cp: U32 = ch.codepoint()
                                                      return
                                                  """);

        Assert.Empty(collection: result.Errors);
    }

    /// <summary>
    /// Verifies that the test validates lossy utf8 apis analyze.
    /// </summary>
    [Fact]
    public void BytesLossyUtf8Apis_Analyze()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine test()
                                                      var bytes: Bytes = b"\x80ABC"
                                                      var text: Text = bytes.decode_as_utf8_lossy()
                                                      for ch in bytes.interpret_as_utf8_lossy()
                                                          var cp: U32 = ch.codepoint()
                                                      return
                                                  """);

        Assert.Empty(collection: result.Errors);
    }

    /// <summary>
    /// Verifies that the test validates strict utf8 apis analyze inside failable routine.
    /// </summary>
    [Fact]
    public void BytesStrictUtf8Apis_AnalyzeInsideFailableRoutine()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  failable routine test()
                                                      var bytes: Bytes = b"ABC"
                                                      var text: Text = bytes.decode_as_utf8()
                                                      var view = bytes.interpret_as_utf8()
                                                      for ch in view
                                                          var cp: U32 = ch.codepoint()
                                                      throw AbsentValueError()
                                                  """);

        Assert.Empty(collection: result.Errors);
    }
}
