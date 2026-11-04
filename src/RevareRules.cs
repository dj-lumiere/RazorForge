using System.Reflection;
using Builder.Frontends;
using Builder.Tokenizer;
using Builder.Verification;
using Revare.Checks;
using Revare.Lexer;
using TypeModel.Enums;

namespace Revare;

/// <summary>
/// Revare's rules: single ownership checked at build time, memory-unsafe surface behind
/// <c>dangerous</c>/<c>danger</c>, module-level <c>global</c>s only of types threads may share, and fixed-width numeric
/// defaults.
/// </summary>
public sealed class RevareRules : LanguageRules
{
    /// <summary>The one instance the front end registers.</summary>
    public static readonly RevareRules Instance = new();

    private RevareRules()
    {
    }

    public override Language Language => Language.Revare;
    public override string Name => "Revare";
    public override string ToolName => "revare";
    public override string Version => VersionOf(assembly: typeof(RevareRules).Assembly);
    public override string FileExtension => Builder.Frontends.SourceExtensions.Revare;
    public override IReadOnlyList<string> PreviousFileExtensions => Builder.Frontends.SourceExtensions.PreviousRevare;
    public override string ShortName => TypeModel.Realms.Shared;

    public override List<Token> Tokenize(string source, string fileName)
    {
        return new RevareLexer(source: source, fileName: fileName).Tokenize();
    }

    /// <inheritdoc/>
    public override LanguageServerProfile LanguageServer { get; } = new RevareLanguageServer();

    /// <inheritdoc/>
    public override (List<Token> Tokens, List<CommentTrivia> Comments) TokenizeWithComments(string source,
        string fileName)
    {
        var lexer = new RevareLexer(source: source, fileName: fileName);
        return (lexer.Tokenize(), lexer.Comments);
    }

    public override bool AllowsUnsafeCode => true;
    public override bool HasNothingStatement => true;
    public override bool HasModuleGlobals => true;
    public override bool HasTargetDirectives => true;
    public override bool DefaultsToArbitraryPrecision => false;
    public override bool HasDataSizeQuery => true;

    public override bool EntitiesAreShared => false;
    public override bool ChecksOwnership => true;
    public override bool ChecksAccessTokens => true;
    public override bool ChecksReadonly => true;
    public override bool MarksDroppedResults => true;
    public override bool ChecksShapeAtRunTime => false;
    public override bool ZeroIsEmptyPlace => false;

    public override bool RequiresLateinitForDeferredInit => true;
    public override bool RequiresEnterableForUsing => true;
    public override bool RewritesDisplayWrapperArguments => true;

    public override string ScriptVariableAdvice(string name)
    {
        return $"Pass it in as an argument, or declare it as 'global {name}: <Type> = ...'. A global can change, and a " +
               "routine that runs on another thread uses an Unbroken or a Guarded one.";
    }

    public override bool SynthesizeBeforeAnalysis(List<(SyntaxTree.Program Program, string FilePath)> files,
        Action<Builder.Diagnostics.SemanticDiagnosticCode, string, SyntaxTree.SourceLocation> report)
    {
        return Passes.GlobalInitPass.Run(orderedFiles: files, report: report);
    }

    /// <summary>Access tokens are Revare's: their lifetimes are checked here (see
    /// <see cref="TokenLifetimeChecker"/>).</summary>
    internal override IUserBodyCheck CreateUserBodyCheck(DiagnosticReporter report)
    {
        return new TokenLifetimeChecker(report: report);
    }

    /// <summary>The informational version of <paramref name="assembly"/> (its csproj <c>&lt;Version&gt;</c>)
    /// without the <c>+commit</c> suffix.</summary>
    internal static string VersionOf(Assembly assembly)
    {
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
                         assembly.GetName().Version?.ToString() ?? "0.0.0";
        int plus = version.IndexOf(value: '+');
        return plus > 0
            ? version[..plus]
            : version;
    }
}
