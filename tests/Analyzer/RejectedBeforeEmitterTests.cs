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
}
