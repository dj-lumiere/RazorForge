using TypeModel.Enums;

namespace Builder.Diagnostics;

/// <summary>
/// Picks the language a diagnostic code is spelled in. A code carries the language of the file the
/// diagnostic points at, so a Suflae file reports SF-S436 while an imported RazorForge module in the
/// same build still reports RF codes for its own lines.
/// </summary>
public static class DiagnosticLanguage
{
    /// <summary>The code prefix for a language: <c>RF</c> or <c>SF</c>.</summary>
    public static string Prefix(Language language)
    {
        return language == Language.Suflae
            ? "SF"
            : "RF";
    }

    /// <summary>
    /// The language of a source file, by extension: a <c>.sf</c> file is Suflae, anything else
    /// (including a missing file name) is RazorForge.
    /// </summary>
    public static Language OfFile(string? fileName)
    {
        return fileName != null &&
               fileName.EndsWith(value: ".sf", comparisonType: StringComparison.OrdinalIgnoreCase)
            ? Language.Suflae
            : Language.RazorForge;
    }
}
