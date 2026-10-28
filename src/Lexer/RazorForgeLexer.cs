using Builder.Diagnostics;
using System.Globalization;
using TypeModel.Enums;

using Builder.Tokenizer;

namespace RazorForge.Lexer;

/// <summary>
/// The RazorForge lexer. It uses significant indentation and newlines for block structure, and the numeric,
/// bytesize and duration literal suffixes. The builder core reaches it through
/// <see cref="Builder.Tokenizer.Lexers"/>, where <see cref="RazorForgeFrontend"/> registers it.
/// </summary>
public partial class RazorForgeLexer
{
    #region Fields

    private readonly string _source;
    private readonly string _fileName;
    private readonly Language _language;
    private int _position;
    private int _line = 1;
    private int _column = 1;
    private int _tokenStart;
    private int _tokenStartColumn;
    private int _tokenStartLine;
    private readonly List<Token> _tokens = [];

    /// <summary>Every comment, in source order (the formatter reads them; the token stream drops `#` ones).</summary>
    public List<CommentTrivia> Comments { get; } = [];
    private readonly Stack<int> _indentStack = new(collection: [0]);
    private bool _hasTokenOnLine;

    /// <summary>The last line ended with a comma outside brackets, so a deeper next line continues it.</summary>
    private bool _continuesAfterComma;
    private int _bracketDepth;
    private bool _hasDefinitions;

    #endregion

    #region Keywords

    /// <summary>
    /// Dictionary mapping keywords to their corresponding token types.
    /// Guarded keywords are always present; RF-only keywords are added conditionally.
    /// </summary>
    private readonly Dictionary<string, TokenType> _keywords;

    /// <summary>Every word this lexer reserves as a keyword (what the Language Server offers as completions). The retired
    /// <c>for</c> stays reserved but is not offered.</summary>
    public static IReadOnlyList<string> KeywordSpellings { get; } =
        new RazorForgeLexer(source: "", fileName: "<keywords>")._keywords
            .Where(predicate: k => k.Value != TokenType.Each).Select(selector: k => k.Key).Order(comparer: StringComparer.Ordinal).ToList();

    #endregion

    #region Suffix Mappings

    /// <summary>
    /// The width-less imaginary suffix (<c>4i</c>, also <c>4_i</c>). Width and radix come from the surrounding
    /// context, and with no context the value is a C128.
    /// </summary>
    private const string ImaginarySuffix = "i";

    /// <summary>
    /// The suffixes that once named a number's type (<c>5_u8</c>, <c>10n</c>, <c>2.5dn</c>), each with the type it
    /// named. A number takes its type from the place it goes, or from the declaration it starts, so each of these
    /// is an error that shows the declaration to write instead.
    /// </summary>
    private static readonly Dictionary<string, string> TypeNamingSuffixes = new()
    {
        [key: "s8"] = "S8",
        [key: "s16"] = "S16",
        [key: "s32"] = "S32",
        [key: "s64"] = "S64",
        [key: "s128"] = "S128",
        [key: "s256"] = "S256",
        [key: "u8"] = "U8",
        [key: "u16"] = "U16",
        [key: "u32"] = "U32",
        [key: "u64"] = "U64",
        [key: "u128"] = "U128",
        [key: "u256"] = "U256",
        [key: "addr"] = "Addr",
        [key: "b16"] = "B16",
        [key: "b32"] = "B32",
        [key: "b64"] = "B64",
        [key: "b128"] = "B128",
        [key: "d32"] = "D32",
        [key: "d64"] = "D64",
        [key: "d128"] = "D128",
        [key: "n"] = "Int",
        [key: "dn"] = "Decimal"
    };

    /// <summary>
    /// Maps ByteSize suffixes to their corresponding token types.
    /// </summary>
    private readonly Dictionary<string, TokenType> _byteSizeSuffixToTokenType = new()
    {
        [key: "b"] = TokenType.ByteLiteral,
        [key: "kb"] = TokenType.KilobyteLiteral,
        [key: "kib"] = TokenType.KibibyteLiteral,
        [key: "mb"] = TokenType.MegabyteLiteral,
        [key: "mib"] = TokenType.MebibyteLiteral,
        [key: "gb"] = TokenType.GigabyteLiteral,
        [key: "gib"] = TokenType.GibibyteLiteral
    };

    /// <summary>
    /// Maps duration suffixes to their corresponding token types.
    /// </summary>
    private readonly Dictionary<string, TokenType> _durationSuffixToTokenType = new()
    {
        [key: "w"] = TokenType.WeekLiteral,
        [key: "d"] = TokenType.DayLiteral,
        [key: "h"] = TokenType.HourLiteral,
        [key: "m"] = TokenType.MinuteLiteral,
        [key: "s"] = TokenType.SecondLiteral,
        [key: "ms"] = TokenType.MillisecondLiteral,
        [key: "us"] = TokenType.MicrosecondLiteral,
        [key: "ns"] = TokenType.NanosecondLiteral
    };

    /// <summary>
    /// Maps text literal prefixes to their corresponding token types.
    /// </summary>
    private readonly Dictionary<string, TokenType> _textPrefixToTokenType = new()
    {
        [key: "r"] = TokenType.RawText,
        [key: "f"] = TokenType.FormattedText,
        [key: "rf"] = TokenType.RawFormattedText,
        [key: "b"] = TokenType.BytesLiteral,
        [key: "br"] = TokenType.BytesRawLiteral
    };

    /// <summary>
    /// List of all valid text prefixes for prefix matching.
    /// </summary>
    private readonly List<string> _textPrefixes =
    [
        "r", "f", "rf", "b", "br"
    ];

    #endregion

    #region Constructor and Properties

    /// <summary>
    /// Initializes a new instance of the <see cref="RazorForgeLexer"/> class.
    /// </summary>
    /// <param name="source">The source code to tokenize.</param>
    /// <param name="fileName">The source code's file name being tokenized.</param>
    public RazorForgeLexer(string source, string fileName)
    {
        const Language language = Language.RazorForge;
        _fileName = fileName;
        _source = NormalizeAndValidateSource(source: source ??
                                                     throw new ArgumentNullException(
                                                         paramName: nameof(source)),
            fileName: fileName,
            language: language);
        _language = language;

        // Guarded keywords (both RF and SF)
        _keywords = new Dictionary<string, TokenType>
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
            // Reserved: a loop over a range or a collection is written `for` (RF-G229).
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
            // modifier, NOT a reflection construct). It lives with `needs`/`obeys`/`protocol`, which SF
            // shares, so it is a SHARED keyword. The derive TEMPLATES it gates (e.g. `routine T.eq` with an
            // `expand` body) are RF stdlib that SF reuses wholesale; authoring such a template needs
            // `expand` (RF-only), but the gate itself and the built-in structural protocols apply to SF
            // types just as well.
            [key: "everywhere"] = TokenType.Everywhere,
            // `until cond` loops while the condition is false, `becomes v` gives a block arm of a value `when` its value.
            [key: "until"] = TokenType.Until,
            [key: "becomes"] = TokenType.Becomes,
            // Reserved for a keyword a later release uses: it can't name anything now.
            [key: "inject"] = TokenType.ReservedWord
        };

        // RazorForge-only keywords
        _keywords[key: "danger"] = TokenType.Danger;
        _keywords[key: "dangerous"] = TokenType.Dangerous;
        _keywords[key: "failable"] = TokenType.Failable;
        _keywords[key: "steal"] = TokenType.Steal;
        // Buildtime reflection is RF's model (monomorph unroll). Suflae's reflection is the runtime
        // ObjectHacker (later); SF inherits the RF-mode-compiled stdlib derives via wholesale System
        // reuse, so it never needs `expand` itself. `expand` is the ONLY reflection keyword; the sources
        // (`openmemvarof`/`allmemvarof`/`caseof`/`branchof`) and accessors (`nameof`/`typeof`/…)
        // tokenize as plain identifiers and are classified in SA (see ExpandSources / the
        // BuilderExpansion import gate), not here.
        _keywords[key: "expand"] = TokenType.Expand;
    }

    /// <summary>
    /// Gets a value indicating whether the source code is in script mode (no definitions found).
    /// </summary>
    public bool IsScriptMode => !_hasDefinitions;

    #endregion

    #region Source Validation

    /// <summary>
    /// Normalizes source text before scanning and rejects invisible or ambiguous input.
    /// </summary>
    private static string NormalizeAndValidateSource(string source, string fileName,
        Language language)
    {
        if (source.Length > 0 && source[index: 0] == '\uFEFF')
        {
            source = source[1..];
        }

        source = source.Replace(oldValue: "\r\n", newValue: "\n")
                       .Replace(oldChar: '\r', newChar: '\n');

        for (int i = 0; i < source.Length; i += 1)
        {
            ValidateSourceCharacter(source: source,
                fileName: fileName,
                language: language,
                position: i);
        }

        return source;
    }

    /// <summary>
    /// Rejects a single invisible or ambiguous source character (null byte, tab, unsupported
    /// whitespace, or a Unicode format character) at the given position.
    /// </summary>
    private static void ValidateSourceCharacter(string source, string fileName, Language language,
        int position)
    {
        char c = source[index: position];
        if (c == '\0')
        {
            ThrowInvalidSourceCharacter(source: source,
                fileName: fileName,
                language: language,
                position: position,
                message: "Source contains a null byte");
        }

        if (c == '\t')
        {
            ThrowInvalidSourceCharacter(source: source,
                fileName: fileName,
                language: language,
                position: position,
                message: "Tabs are not allowed; indentation must use spaces");
        }

        if (char.IsWhiteSpace(c: c) && c is not ' ' and not '\n')
        {
            ThrowInvalidSourceCharacter(source: source,
                fileName: fileName,
                language: language,
                position: position,
                message: $"Unsupported whitespace character U+{(int)c:X4}");
        }

        if (CharUnicodeInfo.GetUnicodeCategory(ch: c) == UnicodeCategory.Format)
        {
            ThrowInvalidSourceCharacter(source: source,
                fileName: fileName,
                language: language,
                position: position,
                message: $"Unsupported format character U+{(int)c:X4}");
        }
    }

    private static void ThrowInvalidSourceCharacter(string source, string fileName,
        Language language, int position, string message)
    {
        int line = 1;
        int column = 1;
        for (int i = 0; i < position; i += 1)
        {
            if (source[index: i] == '\n')
            {
                line += 1;
                column = 1;
            }
            else
            {
                column += 1;
            }
        }

        throw new GrammarException(code: GrammarDiagnosticCode.InvalidCharacter,
            message: message,
            fileName: fileName,
            line: line,
            column: column,
            language: language);
    }

    #endregion

    #region Public memberRoutines

    /// <summary>
    /// Tokenizes the entire source code and returns a list of tokens.
    /// </summary>
    /// <returns>A list of tokens representing the tokenized source code, ending with EOF.</returns>
    public List<Token> Tokenize()
    {
        while (!IsAtEnd())
        {
            _tokenStart = _position;
            _tokenStartColumn = _column;
            _tokenStartLine = _line;

            ScanToken();
        }

        // Emit remaining DEDENT tokens at end of file
        while (_indentStack.Count > 1)
        {
            _indentStack.Pop();
            AddToken(type: TokenType.Dedent, text: "");
        }

        _tokens.Add(item: new Token(Type: TokenType.Eof,
            FileName: _fileName,
            Text: "",
            Line: _line,
            Column: _column,
            Position: _position));
        return _tokens;
    }

    #endregion
}
