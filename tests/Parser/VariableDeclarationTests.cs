using Builder.Diagnostics;
using SyntaxTree;

namespace RazorForge.Tests.Parser;

using static TestHelpers;

/// <summary>
/// Contains tests for variable declaration.
/// </summary>
public class VariableDeclarationTests
{
    #region Variable Declarations

    /// <summary>
    /// Verifies that the parser accepts var with type and initializer.
    /// </summary>
    [Fact]
    public void Parse_VarWithTypeAndInitializer()
    {
        string source = """
                        routine test()
                            var x: S32 = 42
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts var with inferred type.
    /// </summary>
    [Fact]
    public void Parse_VarWithInferredType()
    {
        string source = """
                        routine test()
                            var x = 42
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts var with complex type.
    /// </summary>
    [Fact]
    public void Parse_VarWithComplexType()
    {
        string source = """
                        routine test()
                            var items: List[S32] = [1, 2, 3]
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts var with generic type.
    /// </summary>
    [Fact]
    public void Parse_VarWithGenericType()
    {
        string source = """
                        routine test()
                            var map: Dict[Text, S32] = Dict()
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts var with maybe type.
    /// </summary>
    [Fact]
    public void Parse_VarWithMaybeType()
    {
        string source = """
                        routine test()
                            var value: S32? = none
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts var with function call.
    /// </summary>
    [Fact]
    public void Parse_VarWithFunctionCall()
    {
        string source = """
                        routine test()
                            var result = compute(10, 20)
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts var with expression.
    /// </summary>
    [Fact]
    public void Parse_VarWithExpression()
    {
        string source = """
                        routine test()
                            var sum = a + b * c
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts var with mutation.
    /// </summary>
    [Fact]
    public void Parse_VarWithMutation()
    {
        string source = """
                        routine test()
                            var count = 0
                            count = 1
                            count += 1
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts var without initializer.
    /// </summary>
    [Fact]
    public void Parse_VarWithoutInitializer()
    {
        string source = """
                        routine test()
                            var x: S32
                            x = 42
                            return
                        """;

        AssertParses(source: source);
    }

    #endregion

    #region Multiple Declarations

    /// <summary>
    /// Verifies that the parser accepts multiple var declarations.
    /// </summary>
    [Fact]
    public void Parse_MultipleVarDeclarations()
    {
        string source = """
                        routine test()
                            var a = 1
                            var b = 2
                            var c = 3
                            return
                        """;

        AssertParses(source: source);
    }

    #endregion

    #region Destructuring Declarations

    /// <summary>
    /// Verifies that the parser accepts var destructuring tuple.
    /// </summary>
    [Fact]
    public void Parse_VarDestructuringTuple()
    {
        string source = """
                        routine test()
                            var (x, y) = get_point()
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts var destructuring record.
    /// </summary>
    [Fact]
    public void Parse_VarDestructuringRecord()
    {
        string source = """
                        routine test()
                            var (name, age) = Person(name: "Alice", age: 30)
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts var nested destructuring.
    /// </summary>
    [Fact]
    public void Parse_VarNestedDestructuring()
    {
        string source = """
                        routine test()
                            var ((x, y), radius) = Circle(center: Point(5, 6), radius: 7)
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts var destructuring with alias.
    /// </summary>
    [Fact]
    public void Parse_VarDestructuringWithAlias()
    {
        string source = """
                        routine test()
                            var (center: c, radius: r) = circle
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts var destructuring with wildcard.
    /// </summary>
    [Fact]
    public void Parse_VarDestructuringWithWildcard()
    {
        string source = """
                        routine test()
                            var (_, y) = get_point()
                            return
                        """;

        AssertParses(source: source);
    }

    #endregion

    #region Type Annotations

    /// <summary>
    /// Verifies that the parser accepts primitive types.
    /// </summary>
    [Fact]
    public void Parse_PrimitiveTypes()
    {
        string source = """
                        routine test()
                            var a: S8 = 1
                            var b: S16 = 2
                            var c: S32 = 3
                            var d: S64 = 4
                            var e: U8 = 5
                            var f: U16 = 6
                            var g: U32 = 7
                            var h: U64 = 8
                            var i: B32 = 9.0
                            var j: B64 = 10.0
                            var k: bool = true
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts text type.
    /// </summary>
    [Fact]
    public void Parse_TextType()
    {
        string source = """
                        routine test()
                            var name: Text = "Alice"
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts const generic type.
    /// </summary>
    [Fact]
    public void Parse_ConstGenericType()
    {
        string source = """
                        routine test()
                            var data: List[U8] = bytes
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts nested generic type.
    /// </summary>
    [Fact]
    public void Parse_NestedGenericType()
    {
        string source = """
                        routine test()
                            var nested: Dict[Text, List[S32]] = Dict()
                            return
                        """;

        AssertParses(source: source);
    }

    #endregion

    #region Literal Initializers

    /// <summary>
    /// Verifies that the parser accepts integer literals.
    /// </summary>
    [Fact]
    public void Parse_IntegerLiterals()
    {
        string source = """
                        routine test()
                            var dec = 42
                            var hex = 0xFF
                            var bin = 0b1010
                            var oct = 0o77
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts float literals.
    /// </summary>
    [Fact]
    public void Parse_FloatLiterals()
    {
        string source = """
                        routine test()
                            var a = 3.14
                            var b = 1.0e10
                            var c = 2.5e-3
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts numbers whose type the declaration states.
    /// </summary>
    [Fact]
    public void Parse_DeclaredNumberTypes()
    {
        string source = """
                        routine test()
                            var a: S32 = 42
                            var b: U64 = 100
                            var c: B32 = 3.14
                            var d = 2.718
                            return
                        """;

        AssertParses(source: source);
    }

    /// <summary>
    /// A number no longer names its type with a suffix: each spelling (with or without the separator, hex,
    /// binary, octal, float, <c>n</c>, <c>dn</c>) is RF-G010, with the caret on the suffix and the declaration to
    /// write instead in the message. Unit suffixes and the imaginary <c>i</c> stay.
    /// </summary>
    [Theory]
    [InlineData("5_u8", "U8", 14)]
    [InlineData("7s32", "S32", 14)]
    [InlineData("0xFF_u16", "U16", 17)]
    [InlineData("0x1_b64", "B64", 16)]
    [InlineData("0b1010_u8", "U8", 19)]
    [InlineData("0o17_u32", "U32", 17)]
    [InlineData("1.5_b32", "B32", 16)]
    [InlineData("10n", "Int", 15)]
    [InlineData("2.5dn", "Decimal", 16)]
    [InlineData("0_addr", "Addr", 14)]
    public void Tokenize_TypeNamingSuffix_IsAnError(string number, string typeName, int column)
    {
        string source = $"routine test()\n    var x = {number}\n    return\n";

        GrammarException exception = Assert.Throws<GrammarException>(testCode: () => Tokenize(source: source));
        Assert.Equal(expected: GrammarDiagnosticCode.TypeNamingSuffix, actual: exception.Code);
        Assert.Equal(expected: 2, actual: exception.Line);
        Assert.Equal(expected: column, actual: exception.Column);
        Assert.Contains(expectedSubstring: $"var x: {typeName} =", actualString: exception.RawMessage);
        Assert.Contains(expectedSubstring: "migrate --step suffix", actualString: exception.RawMessage);
    }

    /// <summary>
    /// Unit suffixes (durations, sizes) and the imaginary <c>i</c> still read: they name a unit or a kind of
    /// value, not a type.
    /// </summary>
    [Fact]
    public void Tokenize_UnitAndImaginarySuffixes_StillRead()
    {
        string source = """
                        routine test()
                            var wait = 50ms
                            var page = 4kib
                            var z = 3 + 4i
                            var w = 2_i
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts string literals.
    /// </summary>
    [Fact]
    public void Parse_StringLiterals()
    {
        string source = """
                        routine test()
                            var simple = "hello"
                            var escaped = "line1\nline2"
                            var formatted = f"value = {x}"
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts boolean literals.
    /// </summary>
    [Fact]
    public void Parse_BooleanLiterals()
    {
        string source = """
                        routine test()
                            var t = true
                            var f = false
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts none literal.
    /// </summary>
    [Fact]
    public void Parse_NoneLiteral()
    {
        string source = """
                        routine test()
                            var x: S32? = none
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts list literal.
    /// </summary>
    [Fact]
    public void Parse_ListLiteral()
    {
        string source = """
                        routine test()
                            var items = [1, 2, 3, 4, 5]
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts empty list literal.
    /// </summary>
    [Fact]
    public void Parse_EmptyListLiteral()
    {
        string source = """
                        routine test()
                            var empty: List[S32] = []
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts long text escape literal.
    /// </summary>
    [Fact]
    public void Parse_LongTextEscapeLiteral()
    {
        string source = """
                        routine test()
                            var a = "asdfasdfgasdfasdfasdfasdfsjhdygfckiujsdhfokiulsdjfjhoiwsedhfweolfwefgolijwserdgvoiwe\
                          asdfljhwe4foitrujwergopijwergfolijuwerifopwejgfoiweujiogfwehrjfgoiwehjfoij"
                            return
                        """;

        AssertParses(source: source);
    }

    #endregion

    #region Field Declarations in Types

    /// <summary>
    /// Verifies that the parser accepts record member variables.
    /// </summary>
    [Fact]
    public void Parse_RecordMemberVariables()
    {
        string source = """
                        record Point
                            x: B32
                            y: B32
                        """;

        Program program = AssertParses(source: source);
        RecordDeclaration record = GetDeclaration<RecordDeclaration>(program: program);
        Assert.Equal(expected: 2, actual: record.Members.Count);
    }
    /// <summary>
    /// Verifies that the parser accepts entity var member variables rejected.
    /// </summary>
    [Fact]
    public void Parse_EntityVarMemberVariables_Rejected()
    {
        // var keywords are no longer allowed in entity bodies
        // MemberVariables use 'name: Type' syntax without var keywords
        string source = """
                        entity Counter
                            var count: S32
                        """;

        AssertParseError(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts entity multiple var member variables rejected.
    /// </summary>
    [Fact]
    public void Parse_EntityMultipleVarMemberVariables_Rejected()
    {
        // var keywords are no longer allowed in entity bodies
        // MemberVariables use 'name: Type' syntax without var keywords
        string source = """
                        entity User
                            var id: U64
                            var name: Text
                        """;

        AssertParseError(source: source);
    }

    #endregion

    #region Complex Initializers

    /// <summary>
    /// Verifies that the parser accepts constructor call.
    /// </summary>
    [Fact]
    public void Parse_ConstructorCall()
    {
        string source = """
                        routine test()
                            var point = Point(x: 10.0, y: 20.0)
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts memberRoutine call chain.
    /// </summary>
    [Fact]
    public void Parse_memberRoutineCallChain()
    {
        string source = """
                        routine test()
                            var result = data.where().select().to_list()
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts conditional initializer.
    /// </summary>
    [Fact]
    public void Parse_ConditionalInitializer()
    {
        string source = """
                        routine test()
                            var value = if condition then 1 else 0
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts when expression initializer.
    /// </summary>
    [Fact]
    public void Parse_WhenExpressionInitializer()
    {
        string source = """
                        routine test()
                            var description = when status
                                is ACTIVE => "running"
                                else => "stopped"
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that the parser accepts a subjectless (condition-based) when expression
    /// initializer with bare-identifier conditions. Regression test: `flag => 1`
    /// was previously consumed as a lambda expression instead of condition + arm arrow.
    /// </summary>
    [Fact]
    public void Parse_SubjectlessWhenExpressionInitializer()
    {
        string source = """
                        routine test()
                            var flag = true
                            var x = when
                                flag => 1
                                else => 2
                            return
                        """;

        Program program = AssertParses(source: source);
        RoutineDeclaration routine = GetDeclaration<RoutineDeclaration>(program: program);
        var body = routine.Body as BlockStatement;
        Assert.NotNull(@object: body);
        VariableDeclaration xDecl = body.Statements
                                        .OfType<DeclarationStatement>()
                                        .Select(selector: s => s.Declaration)
                                        .OfType<VariableDeclaration>()
                                        .First(predicate: v => v.Name == "x");
        WhenExpression whenExpr = Assert.IsType<WhenExpression>(@object: xDecl.Initializer);
        Assert.Null(@object: whenExpr.Expression);
        Assert.Equal(expected: 2, actual: whenExpr.Clauses.Count);
    }
    /// <summary>
    /// Verifies that subjectless when-expression conditions accept boolean operators,
    /// comparisons, and parenthesized expressions.
    /// </summary>
    [Fact]
    public void Parse_SubjectlessWhenInitializerWithBooleanOperators()
    {
        string source = """
                        routine test(n: S64)
                            var flag = true
                            var label = when
                                flag and n > 3 => "big"
                                (n > 4) => "medium"
                                else => "small"
                            return
                        """;

        AssertParses(source: source);
    }
    /// <summary>
    /// Verifies that a lambda inside a call argument list within a subjectless
    /// when-expression condition still parses as a lambda (the no-lambda condition
    /// context is suspended inside argument lists).
    /// </summary>
    [Fact]
    public void Parse_SubjectlessWhenInitializerLambdaArgument()
    {
        string source = """
                        routine test(items: List[S64])
                            var label = when
                                items.any(pred: x => x > 0) => "has positives"
                                else => "none"
                            return
                        """;

        AssertParses(source: source);
    }

    #endregion
}
