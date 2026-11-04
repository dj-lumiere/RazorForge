using Builder.Serialization;
using Builder.Verification;
using TypeModel.Enums;

namespace Revare.Tests.Serialization;

/// <summary>
/// Exercises the on-disk stdlib snapshot cache (<c>src/Serialization/StdlibSnapshotCache.cs</c>) — the
/// warm-start path the compile daemon relies on. A <c>dotnet build</c> emits the modular <c>.pbrf</c>
/// artifacts, so <see cref="StdlibSnapshotCache.LoadOrCapture"/> here takes the reassemble-from-cache
/// route (falling back to a fresh capture if the cache is stale/absent), then the process-lifetime memo
/// on the second call.
/// </summary>
public sealed class StdlibSnapshotCacheTests
{
    [Fact]
    public void ComputeStdlibHash_IsStableAndNonEmpty()
    {
        string? h1 = StdlibSnapshotCache.ComputeStdlibHash(language: Language.Revare);
        string? h2 = StdlibSnapshotCache.ComputeStdlibHash(language: Language.Revare);

        Assert.False(condition: string.IsNullOrWhiteSpace(value: h1));
        Assert.Equal(expected: h1, actual: h2); // deterministic over identical stdlib sources
    }

    [Fact]
    public void LoadOrCapture_ReturnsUsableState_AndMemoizes()
    {
        SemanticVerifier.CompiledStdlibState first =
            StdlibSnapshotCache.LoadOrCapture(language: Language.Revare);
        Assert.NotNull(@object: first);

        // Second call hits the process-lifetime memo and returns the SAME instance.
        SemanticVerifier.CompiledStdlibState second =
            StdlibSnapshotCache.LoadOrCapture(language: Language.Revare);
        Assert.Same(expected: first, actual: second);
    }
}
