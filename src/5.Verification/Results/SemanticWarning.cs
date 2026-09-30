using Builder.Diagnostics;
using SyntaxTree;

namespace Builder.Verification.Results;

/// <summary>
/// Represents a semantic warning during analysis.
/// </summary>
/// <param name="Code">The diagnostic code for this warning.</param>
/// <param name="Message">The warning message.</param>
/// <param name="Location">The source location of the warning.</param>
public sealed record SemanticWarning(
    SemanticWarningCode Code,
    string Message,
    SourceLocation Location)
{
    /// <summary>
    /// The code as the user sees it, spelled in the language of the file the warning points at
    /// (RF-W### for a .rf file, SF-W### for a .sf file).
    /// </summary>
    public string CodeString => Code.ToCodeString(language: DiagnosticLanguage.OfFile(fileName: Location.FileName));

    /// <summary>
    /// Gets the formatted warning message including diagnostic code and location.
    /// Format: warning[RF-W###]: filename:line:column: message (SF-W### for a Suflae file)
    /// </summary>
    public string FormattedMessage =>
        $"warning[{CodeString}]: {Location.FileName}:{Location.Line}:{Location.Column}: {Message}";
}
