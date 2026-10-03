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

    [Fact]
    public void Bundle_WithoutTypeParameters_IsAnError()
    {
        AnalysisResult result = AnalyzeSa(source: Entity + """
                                                           bundle Doc
                                                               r: Res
                                                           """);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.BundleWithoutParameters);
    }

    [Fact]
    public void Bundle_ContainingItself_IsAnError()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  bundle Tree[T]
                                                      value: T
                                                      child: Maybe[Tree[T]]
                                                  """);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.RecursiveBundle);
    }

    [Fact]
    public void GenericBody_CopyingAnUnconstrainedParameter_IsAnError()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  bundle Box[T]
                                                      value: T

                                                  routine Box[T].get() -> T
                                                      return me.value

                                                  routine keep[T](x: T) -> T
                                                      var y = x
                                                      return steal y
                                                  """);
        Assert.Equal(expected: 2,
            actual: result.Errors.Count(predicate: e => e.Code == SemanticDiagnosticCode.BareEntityAssignment));
    }

    [Fact]
    public void GenericBody_ParameterKeptToValues_MayBeCopied()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  bundle Box[T]
                                                      value: T

                                                  routine Box[T].get() -> T
                                                  needs RecordType T
                                                      return me.value

                                                  routine keep[T](x: T) -> T
                                                  needs T obeys Assignable
                                                      var y = x
                                                      return y
                                                  """);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.BareEntityAssignment);
    }

    [Fact]
    public void RecordTypeConstraint_RejectsABundleHoldingAnEntity()
    {
        AnalysisResult result = AnalyzeSa(source: Entity + """
                                                           bundle Pair[T]
                                                               a: T

                                                           record Cell[T]
                                                           needs RecordType T
                                                               v: T

                                                           routine start()
                                                               var ok = Cell[Pair[S64]](v: Pair[S64](a: 1))
                                                               var bad = Cell[Pair[Res]](v: Pair[Res](a: Res(n: 1)))
                                                               return
                                                           """);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ValueTypeConstraintViolation);
    }

    [Fact]
    public void BundleHoldingAnEntity_DuplicatesOnlyWhenEveryMemberIsCopyable()
    {
        AnalysisResult result = AnalyzeSa(source: Entity + """
                                                           entity Cop obeys Copyable
                                                               n: S64

                                                           bundle Pair[T]
                                                               a: T

                                                           routine start()
                                                               var c = Pair[Cop](a: Cop(n: 1))
                                                               var c2 = c.duplicate()
                                                               var r = Pair[Res](a: Res(n: 1))
                                                               var r2 = r.duplicate()
                                                               return
                                                           """);
        Assert.Single(collection: result.Errors,
            predicate: e => e.Code == SemanticDiagnosticCode.ProtocolConstraintViolation);
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
