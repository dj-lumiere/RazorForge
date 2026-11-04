using TypeModel.Enums;
using Builder.Verification;
using Builder.Verification.Results;

namespace Revare.Tests.Meta;

/// <summary>
/// Validates that all stdlib routine bodies pass semantic verification.
/// Mirrors the `validate-stdlib` CLI command.
/// </summary>
public sealed class StdlibValidationTests
{
    /// <summary>Verifies that all Revare stdlib bodies pass semantic analysis.</summary>
    [Fact]
    public void Revare_Stdlib_Validates()
    {
        var analyzer = new SemanticVerifier(language: Language.Revare);
        List<SemanticError> errors = analyzer.ValidateStdlibBodies();
        Assert.Empty(collection: errors);
    }

}
