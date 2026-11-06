using TypeModel.Enums;
using Builder.Tokenizer;

namespace Revare.Lexer;

/// <summary>The words Revare reserves, each with its token type. The shared <see cref="SourceLexer"/> takes this table.</summary>
public static class RevareKeywords
{
    /// <summary>The keyword table for ordinary source files.</summary>
    public static IReadOnlyDictionary<string, TokenType> Table { get; } = Build();

    /// <summary>Every word this language reserves as a keyword (what the Language Server offers as completions). The retired
    /// <c>for</c> stays reserved but is not offered.</summary>
    public static IReadOnlyList<string> KeywordSpellings { get; } =
        Table.Where(predicate: k => k.Value != TokenType.Each).Select(selector: k => k.Key)
             .Order(comparer: StringComparer.Ordinal).ToList();


    private static Dictionary<string, TokenType> Build()
    {
        Dictionary<string, TokenType> table = new()
        {
            // Type declarations
            [key: "routine"] = TokenType.Routine,
            [key: "entity"] = TokenType.Entity,
            [key: "record"] = TokenType.Record,
            [key: "bundle"] = TokenType.Bundle,
            [key: "choice"] = TokenType.Choice,
            [key: "flags"] = TokenType.Flags,
            [key: "crashable"] = TokenType.Crashable,
            [key: "variant"] = TokenType.Variant,
            [key: "protocol"] = TokenType.Protocol,

            // Variable declarations
            [key: "var"] = TokenType.Var,
            [key: "preset"] = TokenType.Preset,
            [key: "global"] = TokenType.Global,
            [key: "lateinit"] = TokenType.LateInit,

            // Access modifiers
            [key: "secret"] = TokenType.Secret,
            [key: "posted"] = TokenType.Posted,
            [key: "common"] = TokenType.Common,

            // Self references
            [key: "me"] = TokenType.Me,
            [key: "Me"] = TokenType.MyType,

            // Protocol implementation (renamed keywords, old TokenType names)
            [key: "obeys"] = TokenType.Obeys,

            // Control flow
            [key: "if"] = TokenType.If,
            [key: "elseif"] = TokenType.Elseif,
            [key: "else"] = TokenType.Else,
            [key: "then"] = TokenType.Then,
            [key: "with"] = TokenType.With,
            [key: "when"] = TokenType.When,
            [key: "is"] = TokenType.Is,
            [key: "loop"] = TokenType.Loop,
            [key: "for"] = TokenType.For,
            // Reserved: a loop over a range or a collection is written `for` (RV-G229).
            [key: "each"] = TokenType.Each,
            [key: "break"] = TokenType.Break,
            [key: "continue"] = TokenType.Continue,
            [key: "return"] = TokenType.Return,
            [key: "throw"] = TokenType.Throw,
            [key: "todo"] = TokenType.Todo,
            // Recovery prefix keywords: wrap a failable call into a carrier (Optional/Check).
            // Bare words only — `try_foo`/`check_foo` tokenize as single identifiers, untouched.
            [key: "try"] = TokenType.Try,
            [key: "check"] = TokenType.Check,
            // Module system
            [key: "import"] = TokenType.Import,
            [key: "module"] = TokenType.Module,

            // Special keywords
            [key: "using"] = TokenType.Using,
            // Access blocks: `viewing g as v`, `modifying a, b`, `try modifying g as m` ... `else`.
            [key: "viewing"] = TokenType.Viewing,
            [key: "modifying"] = TokenType.Modifying,
            [key: "as"] = TokenType.As,
            [key: "define"] = TokenType.Define,
            [key: "nothing"] = TokenType.Nothing,
            [key: "given"] = TokenType.Given,
            [key: "in"] = TokenType.In,
            [key: "isnot"] = TokenType.IsNot,
            // Container-first containment (collections AND flags). `notin` is retired — use `lack`.
            [key: "to"] = TokenType.To,
            [key: "til"] = TokenType.Til,
            [key: "by"] = TokenType.By,
            [key: "drop"] = TokenType.Drop,

            // Logical operators
            [key: "and"] = TokenType.And,
            [key: "or"] = TokenType.Or,
            [key: "not"] = TokenType.Not,

            // Literals
            [key: "true"] = TokenType.True,
            [key: "false"] = TokenType.False,
            [key: "None"] = TokenType.None,
            [key: "none"] = TokenType.NoneValue,

            // Generic constraints
            [key: "needs"] = TokenType.Needs,

            // Associated types
            [key: "relates"] = TokenType.Relates,

            // `needs P everywhere` — the structural-conferral gate of the protocol model (a `needs`-clause
            // modifier, NOT a reflection construct). It lives with `needs`/`obeys`/`protocol`, which MI
            // shares, so it is a SHARED keyword. The derive TEMPLATES it gates (e.g. `routine T.eq` with an
            // `expand` body) are RV stdlib that MI reuses wholesale; authoring such a template needs
            // `expand` (RV-only), but the gate itself and the built-in structural protocols apply to MI
            // types just as well.
            [key: "everywhere"] = TokenType.Everywhere,
            // `until cond` loops while the condition is false, `becomes v` gives a block arm of a value `when` its value.
            [key: "until"] = TokenType.Until,
            [key: "becomes"] = TokenType.Becomes
        };


        // Revare-only keywords
        table[key: "danger"] = TokenType.Danger;
        table[key: "dangerous"] = TokenType.Dangerous;
        table[key: "failable"] = TokenType.Failable;
        table[key: "infallible"] = TokenType.Infallible;
        table[key: "steal"] = TokenType.Steal;
        // Buildtime reflection is RV's model (monomorph unroll). Mielia's reflection is the runtime
        // ObjectHacker (later); MI inherits the RV-mode-compiled stdlib derives via wholesale System
        // reuse, so it never needs `expand` itself. `expand` is the ONLY reflection keyword; the sources
        // (`openmemvarof`/`allmemvarof`/`caseof`/`branchof`) and accessors (`nameof`/`typeof`/…)
        // tokenize as plain identifiers and are classified in SA (see ExpandSources / the
        // BuilderExpansion import gate), not here.
        table[key: "expand"] = TokenType.Expand;

        return table;
    }
}
