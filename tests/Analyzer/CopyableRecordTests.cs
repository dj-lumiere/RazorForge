namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// A record that `=` copies has its copy as its <c>duplicate</c>, so it may say <c>obeys Copyable</c> whatever its
/// member variables are: a <c>Guarded</c> handle, which is not Copyable itself, is shared by the copy as `Int` shares
/// its limb list. Only a value duplicated member by member (one holding an entity) needs Copyable members.
/// </summary>
public class CopyableRecordTests
{
    /// <summary>A record holding a <c>Guarded</c> handle obeys Copyable without writing <c>duplicate</c> (no RF-S702).</summary>
    [Fact]
    public void RecordWithGuardedMember_ObeysCopyable_WithoutWrittenDuplicate()
    {
        AssertAnalyzes(source: """
                               record Limbs
                               obeys Copyable
                                   owner: Guarded[List[U64], ReadOnly]
                                   meta: U64

                               routine start()
                                   var a = Limbs(owner: Guarded[List[U64], ReadOnly](from: [1, 2]), meta: 3)
                                   var b = a.duplicate()
                                   var m = b.meta
                                   return
                               """);
    }

    /// <summary>A record of plain values obeys Copyable as before.</summary>
    [Fact]
    public void RecordOfValues_ObeysCopyable()
    {
        AssertAnalyzes(source: """
                               record Point
                               obeys Copyable
                                   x: S64
                                   name: Text

                               routine start()
                                   var p = Point(x: 1, name: "a")
                                   var n = p.duplicate().name
                                   return
                               """);
    }
}
