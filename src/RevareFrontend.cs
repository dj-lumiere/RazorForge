using Builder.Frontends;

namespace Revare;

/// <summary>The Revare front end: registers Revare's rules (its lexer, surface and checks) with the
/// builder core.</summary>
public static class RevareFrontend
{
    /// <summary>Registers Revare's rules (idempotent).</summary>
    public static void Register()
    {
        Languages.Register(rules: RevareRules.Instance);
    }
}
