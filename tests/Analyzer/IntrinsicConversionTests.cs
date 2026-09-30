namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Regression locks for the <c>LLVM::</c> conversion intrinsics: a type-argument pair the underlying LLVM cast
/// cannot take (<c>int_truncate[U32, U64]</c> became <c>trunc i32 ... to i64</c>, which opt rejected after the
/// build had reported success) is RF-S514 at the call.
/// </summary>
public class IntrinsicConversionTests
{
    [Fact]
    public void ValidConversions_Analyze()
    {
        AssertAnalyzes(source: """
                               routine start()
                                   var a = LLVM::int_truncate[U64, U32](9_u64)
                                   var b = LLVM::zero_extend[U32, U64](7_u32)
                                   var c = LLVM::sign_extend[S8, S64](-1_s8)
                                   var d = LLVM::reinterpret_bits[B64, U64](1.5_b64)
                                   var e = LLVM::float_extend[B32, B64](1.5_b32)
                                   return
                               """);
    }

    [Fact]
    public void TruncateToWiderType_IsRejected()
    {
        AssertHasError(source: """
                               routine start()
                                   var a = LLVM::int_truncate[U32, U64](7_u32)
                                   return
                               """,
            expectedErrorSubstring: "is not narrower than");
    }

    [Fact]
    public void ExtendToSameWidth_IsRejected()
    {
        AssertHasError(source: """
                               routine start()
                                   var a = LLVM::zero_extend[U32, U32](7_u32)
                                   return
                               """,
            expectedErrorSubstring: "is not wider than");
    }

    [Fact]
    public void ReinterpretBetweenSizes_IsRejected()
    {
        AssertHasError(source: """
                               routine start()
                                   var a = LLVM::reinterpret_bits[U32, U64](7_u32)
                                   return
                               """,
            expectedErrorSubstring: "the same size");
    }

    [Fact]
    public void FloatConversionOnInteger_IsRejected()
    {
        AssertHasError(source: """
                               routine start()
                                   var a = LLVM::float_extend[U32, B64](7_u32)
                                   return
                               """,
            expectedErrorSubstring: "takes a floating-point source");
    }
}
