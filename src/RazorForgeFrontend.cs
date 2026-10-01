using Builder.Frontends;

namespace RazorForge;

/// <summary>The RazorForge front end: registers RazorForge's rules (its lexer, surface and checks) with the
/// builder core.</summary>
public static class RazorForgeFrontend
{
    /// <summary>Registers RazorForge's rules (idempotent).</summary>
    public static void Register()
    {
        Languages.Register(rules: RazorForgeRules.Instance);
    }
}
