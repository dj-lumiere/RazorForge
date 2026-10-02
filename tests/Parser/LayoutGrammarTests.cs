using Builder.Tokenizer;
using SyntaxTree;

namespace RazorForge.Tests.Parser;

using static TestHelpers;

/// <summary>
/// The grammar the canonical layout relies on: a comma before the closing bracket of a broken list, a call chain
/// broken before each <c>.</c>, <c>have</c>/<c>lack</c> arms in a <c>when</c> statement, checked expand sources,
/// and <c>\u</c> escapes above U+FFFF.
/// </summary>
public class LayoutGrammarTests
{
    /// <summary>Verifies that a comma before <c>)</c> ends an argument list instead of starting an argument.</summary>
    [Fact]
    public void Parse_TrailingCommaInArguments()
    {
        Program program = AssertParses(source: """
                                               routine start()
                                                   var total = add(
                                                       a: 1,
                                                       b: 2,
                                                   )
                                                   return
                                               """);
        var routine = GetDeclaration<RoutineDeclaration>(program: program);
        var declaration = (DeclarationStatement)((BlockStatement)routine.Body).Statements[index: 0];
        var call = (CallExpression)((VariableDeclaration)declaration.Declaration).Initializer!;
        Assert.Equal(expected: 2, actual: call.Arguments.Count);
    }

    /// <summary>Verifies that a comma before <c>)</c> ends a parameter list.</summary>
    [Fact]
    public void Parse_TrailingCommaInParameters()
    {
        Program program = AssertParses(source: """
                                               routine add(
                                                   a: S32,
                                                   b: S32,
                                               ) -> S32
                                                   return a + b
                                               """);
        Assert.Equal(expected: 2, actual: GetDeclaration<RoutineDeclaration>(program: program).Parameters.Count);
    }

    /// <summary>Verifies that list, set and dict literals take a comma before their closing bracket.</summary>
    [Fact]
    public void Parse_TrailingCommaInCollectionLiterals()
    {
        Program program = AssertParses(source: """
                                               routine start()
                                                   var xs = [1, 2,]
                                                   var s = {1, 2,}
                                                   var d = {1: 2, 3: 4,}
                                                   return
                                               """);
        var statements = ((BlockStatement)GetDeclaration<RoutineDeclaration>(program: program).Body).Statements;
        Assert.Equal(expected: 2,
            actual: ((ListLiteralExpression)Initializer(statement: statements[index: 0])).Elements.Count);
        Assert.Equal(expected: 2,
            actual: ((SetLiteralExpression)Initializer(statement: statements[index: 1])).Elements.Count);
        Assert.Equal(expected: 2,
            actual: ((DictLiteralExpression)Initializer(statement: statements[index: 2])).Pairs.Count);
    }

    /// <summary>Verifies that a line starting with <c>.</c> continues the call chain of the line before it.</summary>
    [Fact]
    public void Parse_CallChainBrokenBeforeDots()
    {
        Program program = AssertParses(source: """
                                               routine start()
                                                   var t = name
                                                       .trim()
                                                       .upper()
                                                   return
                                               """);
        var statements = ((BlockStatement)GetDeclaration<RoutineDeclaration>(program: program).Body).Statements;
        Assert.Equal(expected: 2, actual: statements.Count);
        var outer = (CallExpression)Initializer(statement: statements[index: 0]);
        Assert.Equal(expected: "upper", actual: ((MemberExpression)outer.Callee).MemberName);
    }

    /// <summary>Verifies that a leading <c>.</c> emits no layout token.</summary>
    [Fact]
    public void Tokenize_LeadingDotLineEmitsNoLayoutToken()
    {
        List<Token> tokens = Tokenize(source: "routine start()\n    var t = a\n        .b()\n    return\n");
        int dot = tokens.FindIndex(match: t => t.Type == TokenType.Dot);
        Assert.Equal(expected: TokenType.Identifier, actual: tokens[index: dot - 1].Type);
    }

    /// <summary>Verifies that a <c>when</c> statement takes <c>have</c> and <c>lack</c> arms.</summary>
    [Fact]
    public void Parse_FlagsArmsInWhenStatement()
    {
        Program program = AssertParses(source: """
                                               routine check(p: Perm)
                                                   when p
                                                       have READ and WRITE => pass
                                                       lack EXEC => pass
                                                       else => pass
                                                   return
                                               """);
        var when = (WhenStatement)((BlockStatement)GetDeclaration<RoutineDeclaration>(program: program).Body)
           .Statements[index: 0];
        Assert.IsType<FlagsPattern>(@object: when.Clauses[index: 0].Pattern);
        Assert.True(condition: ((FlagsPattern)when.Clauses[index: 1].Pattern).IsNegated);
    }

    /// <summary>Verifies that a misspelled arm-expansion source is a grammar error, not read as <c>branchof</c>.</summary>
    [Fact]
    public void Parse_UnknownArmExpansionSourceIsError()
    {
        AssertParseError(source: """
                                 routine T.show() -> Text
                                     when me
                                         expand m in brnchof(T)
                                             is $typeof(m) v => return "x"
                                     return "y"
                                 """);
    }

    /// <summary>Verifies that a <c>\u</c> escape above U+FFFF keeps its whole code point.</summary>
    [Fact]
    public void Tokenize_UnicodeEscapeAboveBmp()
    {
        List<Token> tokens = Tokenize(source: "routine start()\n    var s = \"\\u01F600\"\n    return\n");
        Token text = tokens.First(predicate: t => t.Type == TokenType.TextLiteral);
        Assert.Equal(expected: char.ConvertFromUtf32(utf32: 0x1F600), actual: text.Text);
    }

    /// <summary>Verifies that annotation arguments take durations, memory sizes, negative numbers and tuples.</summary>
    [Fact]
    public void Parse_AnnotationValuesWithSuffixesAndTuples()
    {
        Program program = AssertParses(source: """
                                               @case(input: (1, -2), output: -1)
                                               @time_limit(5s)
                                               @memory_limit(1mib)
                                               routine add(a: S32, b: S32) -> S32
                                                   return a + b
                                               """);
        List<string> annotations = GetDeclaration<RoutineDeclaration>(program: program).Annotations;
        Assert.Equal(expected: ["case(input=(1, -2), output=-1)", "time_limit(5s)", "memory_limit(1mib)"],
            actual: annotations);
    }

    /// <summary>
    /// Verifies that a type header takes its clauses in any order (`relates` before `obeys`) and that a clause
    /// continues on a deeper line after a trailing comma.
    /// </summary>
    [Fact]
    public void Parse_HeaderClausesInAnyOrderWithContinuation()
    {
        Program program = AssertParses(source: """
                                               entity Pair[A, B]
                                               relates PairEmitter[A] as Iter
                                               obeys Equatable, Hashable,
                                                   Comparable
                                               needs EntityType A, U64 B
                                               needs A obeys Hashable,
                                                   Equatable
                                                   first: A
                                               """);
        var entity = GetDeclaration<EntityDeclaration>(program: program);
        Assert.Equal(expected: 3, actual: entity.Protocols.Count);
        Assert.Single(collection: entity.AssociatedTypes!);
        Assert.Equal(expected: 3, actual: entity.GenericConstraints!.Count);
        Assert.Equal(expected: 2, actual: entity.GenericConstraints[index: 2].ConstraintTypes!.Count);
        Assert.Single(collection: entity.Members);
    }

    /// <summary>Verifies that a script's top level takes a destructuring declaration.</summary>
    [Fact]
    public void Parse_ScriptDestructuring()
    {
        Program program = AssertParses(source: """
                                               var pair = (1, 2)
                                               var (a, b) = pair
                                               show(a)
                                               """);
        var start = GetDeclaration<RoutineDeclaration>(program: program);
        Assert.True(condition: start.IsScriptEntry);
        Assert.IsType<DestructuringStatement>(@object: ((BlockStatement)start.Body).Statements[index: 1]);
    }

    /// <summary>Verifies that a parenthesized `when` arm is a tuple pattern, not a lambda or a tuple value.</summary>
    [Fact]
    public void Parse_TuplePatternArm()
    {
        Program program = AssertParses(source: """
                                               routine describe(point: (S64, S64)) -> Text
                                                   when point
                                                       (0, 0) => return "origin"
                                                       (x, y) => return "other"
                                                   return "none"
                                               """);
        var when = (WhenStatement)((BlockStatement)GetDeclaration<RoutineDeclaration>(program: program).Body)
           .Statements[index: 0];
        var origin = Assert.IsType<DestructuringPattern>(@object: when.Clauses[index: 0].Pattern);
        Assert.IsType<LiteralPattern>(@object: origin.Bindings[index: 0].NestedPattern);
        var other = Assert.IsType<DestructuringPattern>(@object: when.Clauses[index: 1].Pattern);
        Assert.Equal(expected: "y", actual: other.Bindings[index: 1].BindingName);
    }

    /// <summary>Verifies that `unless` takes an `else`.</summary>
    [Fact]
    public void Parse_UnlessElse()
    {
        Program program = AssertParses(source: """
                                               routine start()
                                                   unless ready
                                                       wait()
                                                   else
                                                       go()
                                                   return
                                               """);
        var unless = (IfStatement)((BlockStatement)GetDeclaration<RoutineDeclaration>(program: program).Body)
           .Statements[index: 0];
        Assert.NotNull(@object: unless.ElseStatement);
    }

    /// <summary>Verifies that an `if` expression with an indented block is a grammar error.</summary>
    [Fact]
    public void Parse_BlockIfExpressionIsError()
    {
        AssertParseError(source: """
                                 routine start()
                                     var status = if ready
                                         "go"
                                     return
                                 """);
    }

    /// <summary>Verifies that a bare lowercase name with a guard in a `when` arm is a grammar error.</summary>
    [Fact]
    public void Parse_UntypedBindingArmIsError()
    {
        AssertParseError(source: """
                                 routine start()
                                     var value: S32 = 2
                                     when value
                                         n and 0 <= n <= 3 => pass
                                         else => pass
                                     return
                                 """);
    }

    /// <summary>Verifies that `needs P1, P2 everywhere` gates each protocol, like one line per protocol.</summary>
    [Fact]
    public void Parse_EverywhereList()
    {
        Program program = AssertParses(source: """
                                               routine T.eq(you: T) -> Bool
                                               needs Equatable, Hashable[T] everywhere
                                                   return true
                                               """);
        List<GenericConstraintDeclaration> constraints =
            GetDeclaration<RoutineDeclaration>(program: program).GenericConstraints!;
        Assert.Equal(expected: 2, actual: constraints.Count);
        Assert.All(collection: constraints,
            action: c => Assert.Equal(expected: ConstraintKind.Everywhere, actual: c.ConstraintType));
        Assert.Equal(expected: "Hashable", actual: constraints[index: 1].ConstraintTypes![index: 0].Name);
    }

    /// <summary>Verifies that one `relates` clause takes a list, in a type header and in a protocol body.</summary>
    [Fact]
    public void Parse_RelatesList()
    {
        Program program = AssertParses(source: """
                                               entity Pair[A]
                                               relates PairEmitter[A] as Iter, A as Key
                                                   first: A

                                               protocol Keyed
                                                   relates Key obeys Hashable, Value
                                                   routine Me.size() -> U64
                                               """);
        Assert.Equal(expected: 2, actual: GetDeclaration<EntityDeclaration>(program: program).AssociatedTypes!.Count);
        List<AssociatedTypeDeclaration> slots = GetDeclaration<ProtocolDeclaration>(program: program).AssociatedTypes!;
        Assert.Equal(expected: ["Key", "Value"], actual: slots.Select(selector: a => a.Name));
    }

    private static Expression Initializer(Statement statement)
    {
        return ((VariableDeclaration)((DeclarationStatement)statement).Declaration).Initializer!;
    }
}
