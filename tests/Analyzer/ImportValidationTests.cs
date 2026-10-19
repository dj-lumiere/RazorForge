using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Contains tests for import validation.
/// </summary>
public class ImportValidationTests
{
    #region #106: Import position enforcement

    /// <summary>
    /// Verifies semantic analysis behavior for import after declaration and reports the expected error.
    /// </summary>
    [Fact]
    public void Analyze_ImportAfterDeclaration_ReportsError()
    {
        string source = """
                        record Point
                            x: S32
                            y: S32
                        import System
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ImportPositionViolation);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for import before declaration without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_ImportBeforeDeclaration_NoError()
    {
        string source = """
                        import System
                        record Point
                            x: S32
                            y: S32
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ImportPositionViolation);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for multiple imports before declarations without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_MultipleImportsBeforeDeclarations_NoError()
    {
        string source = """
                        import System
                        import System.Text
                        import System.Bool
                        record Point
                            x: S32
                            y: S32
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ImportPositionViolation);
    }

    #endregion

    #region #105: Import name collision

    /// <summary>
    /// Verifies semantic analysis behavior for duplicate imported symbol and reports the expected error.
    /// </summary>
    [Fact]
    public void Analyze_DuplicateImportedSymbol_ReportsError()
    {
        string source = """
                        import System.[Text]
                        import System.[Text]
                        record Dummy
                            value: S32
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ImportNameCollision);
    }
    /// <summary>
    /// Verifies semantic analysis behavior for disjoint specific imports without unexpected diagnostics.
    /// </summary>
    [Fact]
    public void Analyze_DisjointSpecificImports_NoError()
    {
        string source = """
                        import System.[Text]
                        import System.[Bool]
                        record Dummy
                            value: S32
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ImportNameCollision);
    }

    #endregion
}
