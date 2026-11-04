using TypeModel.Enums;

namespace Revare;

/// <summary>The <c>revare</c> command line.</summary>
internal static class RevareCli
{
    /// <summary>Registers the Revare front end and runs the command line.</summary>
    public static int Main(string[] args)
    {
        RevareFrontend.Register();
        return Builder.Execution.Program.Run(args: args, cliLanguage: Language.Revare);
    }
}
