using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Forms that once passed analysis and then stopped the LLVM emitter or the linker: each is a build error now.
/// </summary>
public class RejectedBeforeEmitterTests
{
    [Fact]
    public void Analyze_CStrParameterOfCRoutine_IsRejected()
    {
        AssertHasErrorSa(source: """
                         routine C::strlen(s: CStr) -> U64

                         routine start()
                             drop C::strlen("x".to[CStr]())
                             return
                         """,
            expectedErrorSubstring: "but C reads a pointer there. Declare it as 'CStrView'");
    }

    [Fact]
    public void Analyze_CWStrReturnOfCRoutine_IsRejected()
    {
        AssertHasErrorSa(source: """
                         routine C::wide_name() -> CWStr

                         routine start()
                             var w = C::wide_name()
                             return
                         """,
            expectedErrorSubstring: "Declare it as returning 'CWStrView'");
    }

    [Fact]
    public void Analyze_ConstructionLeavingOutAMember_IsRejected()
    {
        AssertHasErrorSa(source: """
                         record Size
                             width: S64
                             height: S64

                         routine start()
                             var s = Size(width: 3)
                             return
                         """,
            expectedErrorSubstring: "this leaves out 'height', which has no default");
    }

    [Fact]
    public void Analyze_PositionalConstructionWithTooFewValues_IsRejected()
    {
        AssertHasErrorSa(source: """
                         record Size
                             width: S64
                             height: S64

                         routine start()
                             var s = Size(3)
                             return
                         """,
            expectedErrorSubstring: "you give 1 value for its 2 member variables");
    }

    [Fact]
    public void Analyze_IntBuiltFromALiteral_PointsToTheTypedDeclaration()
    {
        AssertHasErrorSa(source: """
                         routine start()
                             var a = Int(1)
                             return
                         """,
            expectedErrorSubstring: "An 'Int' is not built from an 'S64'");
    }

    [Fact]
    public void Analyze_IntBuiltFromAVariable_PointsToTheConversion()
    {
        AssertHasErrorSa(source: """
                         routine start()
                             var x: S64 = 4
                             var a = Int(x)
                             return
                         """,
            expectedErrorSubstring: "Convert the value with '.to[Int]()'");
    }

    [Fact]
    public void Analyze_ListOfRoutinesTakingNothing_Builds()
    {
        AssertAnalyzes(source: """
                       routine start()
                           var fs = List[Callable[(), S64]]()
                           fs.add_last(() => 7)
                           var f = fs[0]
                           drop f()
                           return
                       """);
    }

    [Fact]
    public void Analyze_SliceOfEntityList_IsRejected()
    {
        AssertHasErrorSa(source: """
                         entity Player
                             name: Text

                         routine start()
                             var team = [Player(name: "a"), Player(name: "b")]
                             var first = team[0 til 1]
                             return
                         """,
            expectedErrorSubstring: "is an entity, which is not copied");
    }

    [Fact]
    public void Analyze_ProtocolAsElementType_IsRejected()
    {
        AssertHasErrorSa(source: """
                         protocol Named
                             routine Me.name() -> Text

                         routine start()
                             var xs = List[Named]()
                             return
                         """,
            expectedErrorSubstring: "'Named' is a protocol, not a type of values");
    }

    [Fact]
    public void Analyze_MemberWriteThroughReadOnlyElement_IsRejected()
    {
        AssertHasErrorSa(source: """
                         entity Box
                             n: S64

                         routine bump(boxes: Viewing[List[Box]])
                             boxes[0].n += 1
                             return

                         routine start()
                             var boxes = [Box(n: 1)]
                             bump(boxes: boxes.view())
                             return
                         """,
            expectedErrorSubstring: "reached through a read-only token");
    }

    [Fact]
    public void Analyze_VariantBuiltFromNonMember_IsRejected()
    {
        AssertHasErrorSa(source: """
                         variant Shape
                             S64
                             Text

                         routine start()
                             var flag = true
                             var s = Shape(flag)
                             return
                         """,
            expectedErrorSubstring: "'Shape' is not built from a 'Bool'");
    }

    [Fact]
    public void Analyze_DictionaryFromKeyValueCall_IsRejected()
    {
        AssertHasErrorSa(source: """
                         routine start()
                             var d = Dict(1: 10, 2: 20)
                             return
                         """,
            expectedErrorSubstring: "is not built from key-value pairs in a call");
    }

    [Fact]
    public void Analyze_BitArrayFromValueCall_IsRejected()
    {
        AssertHasErrorSa(source: """
                         import Collections

                         routine start()
                             var b = BitArray[4](true, false, true, false)
                             return
                         """,
            expectedErrorSubstring: "is not built from its values in a call");
    }

    [Fact]
    public void Analyze_ReadBackOfTypeWithSecretMember_IsRejected()
    {
        AssertHasErrorSa(source: """
                         record Hidden
                             secret n: S64

                         record Box
                             h: Hidden

                         routine start()
                             var sv = SerialValue(from: "x")
                             var b = try sv.to[Box]()
                             return
                         """,
            expectedErrorSubstring: "'Hidden' ('Box' holds it) cannot be read back from a serialized value",
            fileName: "read_back.rf");
    }

    [Fact]
    public void Parse_TupleArmWrittenWithIs_GivesOneErrorWithTheSpelling()
    {
        (_, Builder.Parser.Parser parser) = ParseWithErrors(source: """
            routine start()
                var p = (1, 2)
                when p
                    is (1, 2) => return
                    else => return
            """);
        List<string> errors = parser.GetErrors();
        Assert.Single(collection: errors);
        Assert.Contains(expectedSubstring: "A tuple arm is written without 'is'", actualString: errors[index: 0]);
    }

    [Fact]
    public void Analyze_MatchedVariableAssignedInItsArm_IsRejected()
    {
        AssertHasErrorSa(source: """
                         entity Player
                             name: Text

                         variant Seat
                             Player
                             Text

                         routine start()
                             var seat: Seat = Player(name: "Ada")
                             when seat
                                 is Player as p =>
                                     seat = "empty"
                                     show(p.name)
                                 is Text as t => show(t)
                             return
                         """,
            expectedErrorSubstring: "inside the arm where 'p' reads what 'seat' holds");
    }

    [Fact]
    public void Analyze_StealLoopOverSet_NamesTheLoop()
    {
        AssertHasErrorSa(source: """
                         routine start()
                             var s = Set[S64]()
                             drop s.add(1)
                             each n in steal s
                                 show(n)
                             return
                         """,
            expectedErrorSubstring: "Walking with 'steal' takes the elements out of a 'List'");
    }
}
