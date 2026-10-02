using System.Reflection;
using Builder.Frontends;
using Builder.Tokenizer;
using RazorForge.Lexer;
using TypeModel.Enums;

namespace RazorForge;

/// <summary>
/// RazorForge's rules: single ownership checked at build time, memory-unsafe surface behind
/// <c>dangerous</c>/<c>danger</c>, no module-level mutable state, and fixed-width numeric defaults.
/// </summary>
public sealed class RazorForgeRules : LanguageRules
{
    /// <summary>The one instance the front end registers.</summary>
    public static readonly RazorForgeRules Instance = new();

    private RazorForgeRules()
    {
    }

    public override Language Language => Language.RazorForge;
    public override string Name => "RazorForge";
    public override string ToolName => "razorforge";
    public override string Version => VersionOf(assembly: typeof(RazorForgeRules).Assembly);
    public override string FileExtension => ".rf";
    public override string ShortName => "RF";

    public override List<Token> Tokenize(string source, string fileName)
    {
        return new RazorForgeLexer(source: source, fileName: fileName).Tokenize();
    }

    /// <inheritdoc/>
    public override LanguageServerProfile LanguageServer { get; } = new RazorForgeLanguageServer();

    /// <inheritdoc/>
    public override (List<Token> Tokens, List<CommentTrivia> Comments) TokenizeWithComments(string source,
        string fileName)
    {
        var lexer = new RazorForgeLexer(source: source, fileName: fileName);
        return (lexer.Tokenize(), lexer.Comments);
    }

    public override bool AllowsUnsafeCode => true;
    public override bool HasPassStatement => true;
    public override bool HasThreadedRoutines => true;
    public override bool HasModuleGlobals => false;
    public override bool HasTargetDirectives => true;
    public override bool DefaultsToArbitraryPrecision => false;
    public override bool HasDataSizeQuery => true;

    public override bool EntitiesAreShared => false;
    public override bool ChecksOwnership => true;
    public override bool ChecksAccessTokens => true;
    public override bool ChecksReadonly => true;
    public override bool ChecksShapeAtRunTime => false;

    public override bool RequiresLateinitForDeferredInit => true;
    public override bool RequiresInferableLambdaParameters => true;
    public override bool RequiresEnterableForUsing => true;
    public override bool RewritesDisplayWrapperArguments => true;

    public override string ScriptVariableAdvice(string name)
    {
        return "Pass it in as an argument. RazorForge has no module-level mutable state.";
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
