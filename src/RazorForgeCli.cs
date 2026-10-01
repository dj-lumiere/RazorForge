using TypeModel.Enums;

namespace RazorForge;

/// <summary>The <c>razorforge</c> command line.</summary>
internal static class RazorForgeCli
{
    /// <summary>Registers the RazorForge front end and runs the command line.</summary>
    public static int Main(string[] args)
    {
        RazorForgeFrontend.Register();
        return Builder.Execution.Program.Run(args: args, cliLanguage: Language.RazorForge);
    }
}
