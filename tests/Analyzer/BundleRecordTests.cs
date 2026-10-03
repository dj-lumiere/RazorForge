using Builder.Declaration;
using Builder.Diagnostics;
using Builder.Verification.Results;
using TypeModel.Symbols;
using TypeModel.Types;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A <c>record</c> is a value, so it holds neither an entity nor a type parameter that could be one (RF-S409); a
/// <c>bundle</c> is the generic aggregate whose instances holding an entity are single-owner.
/// </summary>
public class BundleRecordTests
{
    private const string Entity = """
                                  entity Res
                                      n: S64

                                  """;

    [Fact]
    public void Record_HoldingAnEntity_IsAnError()
    {
        AnalysisResult result = AnalyzeSa(source: Entity + """
                                                           record Holder
                                                               r: Res
                                                           """);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecordHoldsEntity);
    }

    [Fact]
    public void Record_HoldingAnUnconstrainedParameter_IsAnError()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  record Pair[T]
                                                      a: T
                                                      b: T
                                                  """);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecordHoldsEntity);
    }

    [Fact]
    public void Record_HoldingAParameterKeptToValues_IsFine()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  record Pair[T]
                                                  needs RecordType T
                                                      a: T
                                                      b: T
                                                  """);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecordHoldsEntity);
    }

    [Fact]
    public void Bundle_HoldingAnyParameter_IsFine()
    {
        AnalysisResult result = AnalyzeSa(source: Entity + """
                                                           bundle Pair[T]
                                                               a: T
                                                               b: T

                                                           routine start()
                                                               var p = Pair[Res](a: Res(n: 1), b: Res(n: 2))
                                                               var q = Pair[S64](a: 1, b: 2)
                                                               var r = q
                                                               return
                                                           """);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecordHoldsEntity);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.BareEntityAssignment);
    }

    [Fact]
    public void BundleInstance_HoldingAnEntity_IsSingleOwner()
    {
        AnalysisResult result = AnalyzeSa(source: Entity + """
                                                           bundle Pair[T]
                                                               a: T
                                                               b: T

                                                           routine start()
                                                               var p = Pair[Res](a: Res(n: 1), b: Res(n: 2))
                                                               var q = p
                                                               return
                                                           """);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.BareEntityAssignment);
    }

    /// <summary>Every RazorForge stdlib record keeps to the rule: the ones that may hold an entity are bundles.</summary>
    [Fact]
    public void StdlibRecords_HoldNoEntity()
    {
        AnalysisResult result = AnalyzeSa(source: "routine start()\n    return\n");
        var offenders = new List<string>();
        foreach (RecordTypeSymbol record in result.Registry.GetAllTypes().OfType<RecordTypeSymbol>())
        {
            if (record is VariantTypeSymbol || record.IsBundle || record.GenericDefinition != null ||
                record.CarrierKind != global::TypeModel.Enums.CarrierKind.None || record.Realm != global::TypeModel.Realms.Shared)
            {
                continue;
            }

            foreach (MemberVariableInfo member in record.MemberVariables)
            {
                string? parameter = TypeRegistry.EntityParameterIn(type: member.Type,
                    constraints: record.GenericConstraints);
                if (parameter != null || member.Type is not GenericParameterTypeSymbol &&
                    result.Registry.IsEntityKind(type: member.Type))
                {
                    offenders.Add(item: $"{record.FullName}.{member.Name}: {parameter ?? member.Type.Name}");
                }
            }
        }

        Assert.Empty(collection: offenders.Distinct());
    }
}
