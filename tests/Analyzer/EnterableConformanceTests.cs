using Builder.Diagnostics;
using Builder.Verification.Results;

namespace RazorForge.Tests.Analyzer;

using static TestHelpers;

/// <summary>
/// Tests for the <c>Enterable</c> protocol and the language features underpinning it:
/// <list type="bullet">
///   <item>Failability covariance — a NON-failable implementation satisfies a FAILABLE (<c>!</c>)
///         protocol requirement (never-failing is a stronger contract); the reverse is an error.</item>
///   <item>The <c>?</c> in-flight mark is meaningless on value-type records, so a record memberRoutine that
///         returns the bare type satisfies a protocol requirement written as <c>?Me</c>.</item>
///   <item>A user type may obey <c>Enterable</c> with a pass-through <c>$enter</c> and drive a
///         <c>using</c> scope.</item>
/// </list>
/// </summary>
public class EnterableConformanceTests
{
    #region Failability covariance (general)

    [Fact]
    public void Analyze_NonFailableImpl_SatisfiesFailableRequirement_Ok()
    {
        string source = """
                        protocol Openable
                            failable routine Me.go()

                        entity Door obeys Openable
                            secret n: S32

                        routine Door.go()
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    /// <summary>Failability is not part of a protocol's contract: a failable implementation satisfies a requirement
    /// written without `!` (its failure crashes loudly unless the caller recovers).</summary>
    [Fact]
    public void Analyze_FailableImpl_ForNonFailableRequirement_Passes()
    {
        string source = """
                        protocol Plain
                            routine Me.go()

                        entity Door obeys Plain
                            secret n: S32

                        failable routine Door.go()
                            throw AbsentValueError()
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.DoesNotContain(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.ProtocolMemberRoutineSignatureMismatch);
    }

    #endregion

    #region Enterable conformance

    [Fact]
    public void Analyze_RecordObeysEnterable_BareReturnPassThrough_Ok()
    {
        // A record `$enter` returns the bare type (no `?`); it satisfies Enterable's `$enter!() -> ?Me`
        // because the `?` in-flight mark is normalized away for value-type records, and a non-failable
        // `$enter` covariantly satisfies the failable `$enter!` requirement.
        string source = """
                        record MyGuard obeys Enterable
                            secret n: S32

                        routine MyGuard.enter() -> MyGuard
                            return me

                        routine MyGuard.exit()
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void Analyze_EntityObeysEnterable_UsedInUsingScope_Ok()
    {
        string source = """
                        import IO/Console

                        entity Session obeys Enterable
                            secret n: S32

                        routine Session.enter() -> Session
                            return me

                        routine Session.exit()
                            return

                        routine start()
                            using Session(n: 1) as s
                                show("in scope")
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    #endregion

    #region A Guarded's access: passed to a call, or opened with a block

    [Fact]
    public void Analyze_AccessPassedToCall_Ok()
    {
        string source = """
                        import IO/Console
                        import BuilderQuery

                        entity Counter
                            value: S64

                        routine peek(c: Viewing[Counter])
                            show(f"{c.value}")
                            return

                        routine start()
                            var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                            peek(c: s.view())
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void Analyze_InspectOpenedWithBlock_Ok()
    {
        string source = """
                        import IO/Console
                        import BuilderQuery

                        entity Counter
                            value: S64

                        routine start()
                            var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                            viewing s as v
                                show(f"{v.value}")
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    [Fact]
    public void Analyze_InspectBoundToVar_Errors()
    {
        string source = """
                        import IO/Console
                        import BuilderQuery

                        entity Counter
                            value: S64

                        routine start()
                            var s = Guarded[Counter, MultiRead](from: Counter(value: 1))
                            var v = s.view()
                            return
                        """;

        AnalysisResult result = AssertHasErrorSa(source: source, expectedErrorSubstring: "isn't passed to a call");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.GuardAccessOutsideArgument);
    }

    #endregion

    #region Resources are using-CAPABLE, not using-required (only a Guarded's locking view/modify is required)

    /// <summary>A `using` target must OBEY Enterable, not merely define $enter/$exit by name.</summary>
    [Fact]
    public void Analyze_UsingNonEnterableType_Errors()
    {
        string source = """
                        record Bare
                            n: S32

                        routine Bare.enter() -> Bare
                            return me

                        routine Bare.exit()
                            return

                        routine test()
                            var b = Bare(n: 1)
                            using b as x
                                var a: S32 = x.n
                            return
                        """;

        AnalysisResult result = AssertHasErrorSa(source: source,
            expectedErrorSubstring: "obeys 'Enterable'");
        Assert.Contains(collection: result.Errors,
            filter: e => e.Code == SemanticDiagnosticCode.UsingTargetMissingEnterExit);
    }

    /// <summary>FileHandle obeys Enterable but is using-CAPABLE, not using-required.</summary>
    [Fact]
    public void Analyze_FileHandleVarBound_NotUsingRequired_Ok()
    {
        // FileHandle obeys Enterable but is NOT using-required (only the MT lock tokens are). A bare
        // `var f = FileManager.open_write(...)` is legal — RAII `$destroy` closes it at scope exit.
        string source = """
                        import IO/File

                        failable routine start()
                            var f = FileManager.open_write(at: File(at: "x.txt"))
                            f.flush()
                            return
                        """;

        AnalysisResult result = AnalyzeSa(source: source);
        Assert.Empty(collection: result.Errors);
    }

    #endregion
}
