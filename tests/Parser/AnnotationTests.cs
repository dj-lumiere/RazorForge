using SyntaxTree;

namespace RazorForge.Tests.Parser;

using static TestHelpers;

/// <summary>
/// Contains tests for attribute.
/// </summary>
public class AttributeTests
{
    #region Simple Annotation Tests

    /// <summary>
    /// Verifies that the parser accepts readonly attribute.
    /// </summary>
    [Fact]
    public void Parse_ReadonlyAttribute()
    {
        string source = """
                        @readonly
                        routine Point.distance() -> B32
                            return 0.0_b32
                        """;

        Program program = AssertParses(source: source);
        RoutineDeclaration routine = GetDeclaration<RoutineDeclaration>(program: program);
        Assert.NotNull(@object: routine.Annotations);
        Assert.Contains(expected: "readonly", collection: routine.Annotations);
    }
    /// <summary>
    /// Verifies that the parser accepts crash only attribute.
    /// </summary>
    [Fact]
    public void Parse_CrashOnlyAttribute()
    {
        string source = """
                        @crash_only
                        routine internal_divide!(a: S32, b: S32) -> S32
                            if b == 0
                                throw DivisionByZeroError()
                            return a // b
                        """;

        Program program = AssertParses(source: source);
        RoutineDeclaration routine = GetDeclaration<RoutineDeclaration>(program: program);
        Assert.NotNull(@object: routine.Annotations);
        Assert.Contains(expected: "crash_only", collection: routine.Annotations);
    }
    /// <summary>
    /// Verifies that the parser accepts prelude attribute.
    /// </summary>
    [Fact]
    public void Parse_PreludeAttribute()
    {
        string source = """
                        @prelude
                        routine show(msg: Text)
                            pass
                            return
                        """;

        Program program = AssertParses(source: source);
        RoutineDeclaration routine = GetDeclaration<RoutineDeclaration>(program: program);
        Assert.NotNull(@object: routine.Annotations);
        Assert.Contains(expected: "prelude", collection: routine.Annotations);
    }
    /// <summary>
    /// Verifies that the parser accepts static attribute.
    /// </summary>
    [Fact]
    public void Parse_StaticAttribute()
    {
        string source = """
                        @static
                        routine Math.pi() -> B64
                            return 3.14159265359_b64
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts inline attribute.
    /// </summary>
    [Fact]
    public void Parse_InlineAttribute()
    {
        string source = """
                        @inline
                        routine add(a: S32, b: S32) -> S32
                            return a + b
                        """;

        AssertParses(source: source);
    }

    #endregion

    #region Parameterized Annotation Tests

    /// <summary>
    /// Verifies that the parser accepts deprecated attribute with message.
    /// </summary>
    [Fact]
    public void Parse_DeprecatedAttributeWithMessage()
    {
        string source = """
                        @deprecated(message: "Use new_function instead")
                        routine old_function()
                            pass
                            return
                        """;

        AssertParses(source: source);
    }

    #endregion

    #region Compound Annotation Tests

    /// <summary>
    /// Verifies that the parser accepts compound attributes.
    /// </summary>
    [Fact]
    public void Parse_CompoundAttributes()
    {
        string source = """
                        @[readonly, crash_only]
                        routine validate!(value: S32) -> S32
                            if value < 0
                                throw ValidationError()
                            return value
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts compound attributes multiple.
    /// </summary>
    [Fact]
    public void Parse_CompoundAttributesMultiple()
    {
        string source = """
                        @[inline, readonly, prelude]
                        routine identity(x: S32) -> S32
                            return x
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts compound protocol annotations.
    /// </summary>
    [Fact]
    public void Parse_CompoundProtocolAnnotations()
    {
        string source = """
                        protocol Comparable
                            @readonly
                            routine Me.$lt(you: Me) -> Bool

                            @readonly
                            routine Me.$gt(you: Me) -> Bool
                        """;

        AssertParses(source: source);
    }

    #endregion

    #region Type Annotation Tests

    /// <summary>
    /// Verifies that the parser accepts an annotation on a protocol's routine.
    /// </summary>
    [Fact]
    public void Parse_AttributeOnProtocolRoutine()
    {
        string source = """
                        protocol Displayable
                            @readonly
                            routine Me.display() -> Text
                        """;

        AssertParses(source: source);
    }

    /// <summary>
    /// Verifies that an annotation on a protocol itself is rejected: it has no effect there (RF-G213).
    /// </summary>
    [Fact]
    public void Parse_AttributeOnProtocol_HasNoEffect()
    {
        string source = """
                        @prelude
                        protocol Displayable
                            routine Me.display() -> Text
                        """;

        AssertParseError(source: source);
    }

    #endregion

    #region Member Variable Annotation Tests

    /// <summary>
    /// Verifies that an annotation on a record's member variable is rejected: it has no effect there (RF-G213).
    /// </summary>
    [Fact]
    public void Parse_AttributeOnField_HasNoEffect()
    {
        string source = """
                        record Config
                            @optional
                            name: Text

                            @default(42)
                            value: S32
                        """;

        AssertParseError(source: source);
    }
    /// <summary>
    /// Verifies that an annotation on an entity's member variable is rejected: it has no effect there (RF-G213).
    /// </summary>
    [Fact]
    public void Parse_AttributeOnEntityMemberVariable_HasNoEffect()
    {
        string source = """
                        entity User
                            @readonly
                            id: U64

                            @initonly
                            email: Text
                        """;

        AssertParseError(source: source);
    }

    #endregion

    #region Multiple Annotation Lines Tests

    /// <summary>
    /// Verifies that the parser accepts multiple attribute lines.
    /// </summary>
    [Fact]
    public void Parse_MultipleAttributeLines()
    {
        string source = """
                        @readonly
                        @inline
                        @deprecated(message: "Use fast_compute_v2 instead")
                        routine fast_compute(x: S32) -> S32
                            return x * 2
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts attributes on type and memberRoutines.
    /// </summary>
    [Fact]
    public void Parse_AttributesOnTypeAndMemberRoutines()
    {
        string source = """
                        @deprecated(message: "Use NewCalculator")
                        record Calculator
                            value: S32

                        @readonly
                        routine Calculator.get() -> S32
                            return me.value

                        routine Calculator.set(v: S32)
                            me.value = v
                            return
                        """;

        AssertParses(source: source);
    }

    #endregion

    #region Visibility with Annotation Tests

    /// <summary>
    /// Verifies that the parser accepts visibility and attribute.
    /// </summary>
    [Fact]
    public void Parse_VisibilityAndAttribute()
    {
        // open is the default visibility (not a keyword), so just use @readonly + routine
        string source = """
                        @readonly
                        routine Point.distance() -> B32
                            return 0.0_b32
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts private with attribute.
    /// </summary>
    [Fact]
    public void Parse_PrivateWithAttribute()
    {
        string source = """
                        @inline
                        secret routine helper(x: S32) -> S32
                            return x * 2
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts internal with attribute.
    /// </summary>
    [Fact]
    public void Parse_InternalWithAttribute()
    {
        string source = """
                        @deprecated(message: "Use PublicData")
                        secret record InternalData
                            value: S32
                        """;

        AssertParses(source: source);
    }

    #endregion

    #region Protocol Member Routine Annotations

    /// <summary>
    /// Verifies that the parser accepts protocol memberRoutine attributes.
    /// </summary>
    [Fact]
    public void Parse_ProtocolMemberRoutineAttributes()
    {
        string source = """
                        protocol Container
                            @readonly
                            routine Me.count() -> uaddr

                            @readonly
                            routine Me.is_empty() -> bool

                            routine Me.clear()
                        """;

        AssertParses(source: source);
    }

    #endregion

    #region Test Annotation Tests

    /// <summary>
    /// Verifies that the parser accepts test attribute.
    /// </summary>
    [Fact]
    public void Parse_TestAttribute()
    {
        string source = """
                        @test
                        routine test_addition()
                            verify(1 + 1 == 2)
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts bench attribute.
    /// </summary>
    [Fact]
    public void Parse_BenchAttribute()
    {
        string source = """
                        @bench
                        routine bench_sort()
                            var items = generate_random_list(1000)
                            sort(items)
                            return
                        """;

        AssertParses(source: source);
    }

    #endregion
}
