using Builder.Diagnostics;
using SyntaxTree;

namespace Builder.Verification.Results;

/// <summary>
/// Represents a semantic error during analysis.
/// </summary>
/// <param name="Code">The diagnostic code for this error.</param>
/// <param name="Message">The error message.</param>
/// <param name="Location">The source location of the error.</param>
public sealed record SemanticError(
    SemanticDiagnosticCode Code,
    string Message,
    SourceLocation Location)
{
    /// <summary>
    /// The code as the user sees it, spelled in the language of the file the error points at
    /// (RF-S### for a .rf file, SF-S### for a .sf file).
    /// </summary>
    public string CodeString => Code.ToCodeString(language: DiagnosticLanguage.OfFile(fileName: Location.FileName));

    /// <summary>
    /// Gets the formatted error message including diagnostic code and location.
    /// Format: error[RF-S###]: filename:line:column: message (SF-S### for a Suflae file)
    /// </summary>
    public string FormattedMessage =>
        $"error[{CodeString}]: {Location.FileName}:{Location.Line}:{Location.Column}: {Message}";
}
