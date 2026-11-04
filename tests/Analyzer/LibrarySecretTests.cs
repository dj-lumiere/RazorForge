using Builder.Diagnostics;
using Builder.Verification.Results;

namespace Revare.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A <c>secret</c> name declared in the standard library is seen by every library file and by no program.
/// </summary>
public class LibrarySecretTests
{
    /// <summary>A program naming the library's <c>HandlerCell</c> record gets RV-S402.</summary>
    [Fact]
    public void Analyze_ProgramNamesLibrarySecretType_Fails()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                       import System/Cpu

                                       routine start()
                                           var cell = HandlerCell(handler: start)
                                           return
                                       """);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.SecretTypeAccess);
    }

    /// <summary>The same goes for a secret record of another library module, named in a signature.</summary>
    [Fact]
    public void Analyze_ProgramNamesLibrarySecretTypeInSignature_Fails()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                       import Math3D

                                       routine probe(result: IngridMatrixResult) -> U32
                                           return 0

                                       routine start()
                                           return
                                       """);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.SecretTypeAccess);
    }
}
