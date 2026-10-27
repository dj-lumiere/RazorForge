using Builder.Declaration;
using Builder.Diagnostics;
using Builder.Verification.Results;
using TypeModel.Symbols;
using TypeModel.Types;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A <c>record</c> is a value, so it holds neither an entity nor a type parameter that could be one (RF-S409). An
/// entity is the declaration that may: <c>Slots[T, COUNT]</c> is the fixed run of places for any <c>T</c>, kept in one
/// place, and <c>bundle</c> is a build error (RF-G228).
/// </summary>
public class RecordSlotsTests
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
    public void BundleKeyword_IsABuildError()
    {
        (_, Builder.Parser.Parser parser) = ParseWithErrors(source: """
                                                                bundle Pair[T]
                                                                    a: T
                                                                """);
        Assert.Single(collection: parser.GetStructuredErrors()
                                        .Where(predicate: e => e.Code == GrammarDiagnosticCode.BundleKeyword));
    }

    [Fact]
    public void Slots_OfEntities_IsKeptInOnePlace()
    {
        AnalysisResult result = AnalyzeSa(source: Entity + """
                                                           routine start()
                                                               var s: Slots[Res, 2] = [Res(n: 1), Res(n: 2)]
                                                               var t = s
                                                               return
                                                           """);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.BareEntityAssignment);
    }

    [Fact]
    public void Slots_OfValues_IsKeptInOnePlaceToo()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine start()
                                                      var s: Slots[S64, 2] = [1, 2]
                                                      var t = s
                                                      return
                                                  """);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.BareEntityAssignment);
    }

    [Fact]
    public void OldArrayName_NamesItems()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  routine start()
                                                      var a: Array[S64, 2] = [1, 2]
                                                      return
                                                  """);
        Assert.Contains(collection: result.Errors, filter: e => e.Message.Contains(value: "'Items'"));
    }

    [Fact]
    public void Items_OfEntities_IsAnError()
    {
        AnalysisResult result = AnalyzeSa(source: Entity + """
                                                           routine start()
                                                               var a: Items[Res, 2] = [Res(n: 1), Res(n: 2)]
                                                               return
                                                           """);
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ValueTypeConstraintViolation);
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
                                                  needs RecordType T
                                                      var y = x
                                                      return y
                                                  """);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.BareEntityAssignment);
    }

    [Fact]
    public void RecordTypeConstraint_RejectsSlots()
    {
        AnalysisResult result = AnalyzeSa(source: """
                                                  record Cell[T]
                                                  needs RecordType T
                                                      v: T

                                                  routine start()
                                                      var ok = Cell[Items[S64, 1]](v: [1])
                                                      var bad = Cell[Slots[S64, 1]](v: [1])
                                                      return
                                                  """);
        Assert.Single(collection: result.Errors,
            predicate: e => e.Code == SemanticDiagnosticCode.ValueTypeConstraintViolation);
    }

    [Fact]
    public void Slots_DuplicatesOnlyWhenTheElementIsCopyable()
    {
        AnalysisResult result = AnalyzeSa(source: Entity + """
                                                           entity Cop obeys Copyable
                                                               n: S64

                                                           routine start()
                                                               var c: Slots[Cop, 1] = [Cop(n: 1)]
                                                               var c2 = c.duplicate()
                                                               var r: Slots[Res, 1] = [Res(n: 1)]
                                                               var r2 = r.duplicate()
                                                               return
                                                           """);
        Assert.Single(collection: result.Errors,
            predicate: e => e.Code == SemanticDiagnosticCode.ProtocolConstraintViolation);
    }

    /// <summary>Every RazorForge stdlib record keeps to the rule: what may hold an entity is an entity.</summary>
    [Fact]
    public void StdlibRecords_HoldNoEntity()
    {
        AnalysisResult result = AnalyzeSa(source: "routine start()\n    return\n");
        var offenders = new List<string>();
        foreach (RecordTypeSymbol record in result.Registry.GetAllTypes().OfType<RecordTypeSymbol>())
        {
            if (record is VariantTypeSymbol || record.IsInPlaceEntity || record.GenericDefinition != null ||
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
