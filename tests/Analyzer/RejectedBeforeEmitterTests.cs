using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Forms that once passed analysis and then stopped the LLVM emitter or the linker: each is a build error now.
/// </summary>
public class RejectedBeforeEmitterTests
{
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
}
