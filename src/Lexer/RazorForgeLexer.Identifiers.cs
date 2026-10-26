using Builder.Diagnostics;
using Builder.Tokenizer;

namespace RazorForge.Lexer;

/// <summary>
/// Partial class containing identifier, keyword, and comment scanning memberRoutines for the RazorForge lexer.
/// </summary>
/// <remarks>
/// <para>
/// Identifiers follow the same rules in both RazorForge and Suflae:
/// </para>
/// <list type="bullet">
///   <item><description>Must start with a letter, underscore, or dollar sign ($)</description></item>
///   <item><description>Can contain letters, digits, and underscores</description></item>
///   <item><description>Optional single ? suffix for failable types (e.g., Int?)</description></item>
/// </list>
/// <para>
/// The parser determines from context whether an identifier refers to a type or a value.
/// </para>
/// <para>
/// Note: Only a single ? is consumed as part of an identifier. The ?? operator
/// (none coalescing) is handled separately in the main scanner.
/// </para>
/// <para>
/// This file also handles script mode detection by tracking definition keywords.
/// </para>
/// </remarks>
public partial class RazorForgeLexer
{
    #region Identifier Scanning

    /// <summary>
    /// Scans an identifier or keyword from the current position.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This memberRoutine sets <see cref="_hasTokenOnLine"/> to true, which affects
    /// newline significance detection.
    /// </para>
    /// <para>
    /// Definition keywords (routine, entity, record, choice, variant, protocol) are
    /// tracked to determine whether the file is in script mode.
    /// </para>
    /// <para>
    /// After the base identifier, an optional single ? suffix is consumed for
    /// failable types (e.g., Int?). Double ?? is NOT consumed here as
    /// it is the none coalescing operator.
    /// </para>
    /// <para>
    /// Token type determination:
    /// <list type="number">
    ///   <item><description>If the text matches a keyword, that keyword's token type is used.</description></item>
    ///   <item><description>Otherwise, it's an Identifier (parser determines if it's a type from context).</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <summary>
    /// Scans a name written between backticks: any text up to the closing backtick on the same line, a keyword
    /// included, so <c>`check`</c> names what <c>check</c> would if it weren't a keyword, and <c>`total count`</c> is
    /// one name. Spaces go between words only: one at a time, none at either end, and no tab.
    /// </summary>
    private void ScanBacktickName()
    {
        _hasTokenOnLine = true;
        int startLine = _line;
        int startColumn = _column - 1;
        int start = _position;
        while (!IsAtEnd() && Peek() != '`' && Peek() != '\n')
        {
            Advance();
        }

        string name = _source.Substring(startIndex: start, length: _position - start);
        string? problem = IsAtEnd() || Peek() == '\n'
            ? "is never closed on its line: close it with a backtick"
            : name.Length == 0
                ? "is empty: write the name between the backticks"
                : name.Contains(value: '\t') || name.StartsWith(value: ' ') || name.EndsWith(value: ' ') ||
                  name.Contains(value: "  ")
                    ? "has a tab or spaces at its ends or two in a row: put one space between words"
                    : null;
        if (problem != null)
        {
            throw new GrammarException(code: GrammarDiagnosticCode.InvalidBacktickName,
                message: $"The name `{name}` {problem}.",
                fileName: _fileName,
                line: startLine,
                column: startColumn,
                language: _language);
        }

        Advance(); // the closing backtick
        AddToken(type: TokenType.Identifier, text: name);
    }

    private void ScanIdentifier()
    {
        _hasTokenOnLine = true;

        // Buildtime splice open — the two-character sequence dollar-brace is a SEPARATE structural token
        // distinct from a bare dollar (wired marker) and a bare open-brace. The main scan loop already
        // consumed the dollar into _position, so the next character is the open-brace. Emit the splice-open
        // token and consume the brace; the balanced closing brace is an ordinary RightBrace matched by the
        // parser. This guard runs before the dollar-wired branch below so a splice sequence is never
        // mis-tokenized as a dollar marker followed by a bare identifier.
        if (_source[index: _tokenStart] == '$' && Peek() == '{')
        {
            Advance(); // consume '{'
            AddToken(type: TokenType.SpliceOpen, text: "${");
            return;
        }

        // Wired member-routine marker: a leading '$' emits its own Dollar token and re-anchors so
        // the bare identifier that follows scans on its own. Returns true when a lone '$' with no
        // identifier body was consumed (nothing more to emit).
        if (TryEmitWiredDollarMarker())
        {
            return;
        }

        // Consume identifier characters
        while (IsIdentifierPart(c: Peek()))
        {
            Advance();
        }

        string text = _source.Substring(startIndex: _tokenStart, length: _position - _tokenStart);

        // Check if text + "!" matches a keyword (for danger)
        if (Peek() == '!' && _keywords.TryGetValue(key: text + "!", value: out TokenType bangType))
        {
            Advance();
            AddToken(type: bangType, text: text + "!");
            _hasTokenOnLine = true;
            return;
        }

        // `inf` and `nan` are numbers: like a bare number they take their type from where they go. A width written
        // onto them (`inf_b64`) names the type, which a number no longer does.
        if (text is "inf" or "nan")
        {
            AddToken(type: TokenType.UndecidedDecimal, text: text);
            return;
        }

        if (text.Length > 4 && text[..4] is "inf_" or "nan_" &&
            TypeNamingSuffixes.TryGetValue(key: text[4..], value: out string? specialType))
        {
            throw TypeNamingSuffixError(typeName: specialType, suffixStart: _tokenStart + 4,
                suffixColumn: _tokenStartColumn + 4);
        }

        // Check if it's a keyword
        if (_keywords.TryGetValue(key: text, value: out TokenType type))
        {
            EmitKeywordToken(type: type, text: text);
            return;
        }

        // Skip empty identifiers (defensive)
        if (string.IsNullOrEmpty(value: text))
        {
            return;
        }

        // Always emit Identifier - parser determines type vs value from context
        AddToken(type: TokenType.Identifier, text: text);
    }

    /// <summary>
    /// Handles a leading '$' wired member-routine marker: emits the '$' as its own Dollar token and
    /// re-anchors the token start so the following bare identifier scans on its own. (The main scan
    /// loop already consumed the '$' into <see cref="_position"/>, so the identifier body is scanned
    /// by the caller's loop.)
    /// </summary>
    /// <returns>
    /// <c>true</c> if a lone '$' with no identifier body was consumed (caller should return);
    /// <c>false</c> otherwise (including when there was no '$' at all).
    /// </returns>
    private bool TryEmitWiredDollarMarker()
    {
        if (_source[index: _tokenStart] != '$')
        {
            return false;
        }

        _tokens.Add(item: new Token(Type: TokenType.Dollar,
            FileName: _fileName,
            Text: "$",
            Line: _tokenStartLine,
            Column: _tokenStartColumn,
            Position: _tokenStart));
        _tokenStart += 1;
        _tokenStartColumn += 1;
        // A lone '$' with no identifier body — nothing more to emit.
        return !IsIdentifierPart(c: Peek()) && _position == _tokenStart;
    }

    /// <summary>
    /// Emits a keyword token and tracks definition keywords for script mode detection.
    /// </summary>
    private void EmitKeywordToken(TokenType type, string text)
    {
        AddToken(type: type, text: text);

        // Track definition keywords for script mode detection
        if (type is TokenType.Routine or TokenType.Entity or TokenType.Record or TokenType.Bundle or TokenType.Choice
            or TokenType.Variant or TokenType.Flags or TokenType.Protocol)
        {
            _hasDefinitions = true;
        }
    }

    #endregion

    #region Comment Scanning

    /// <summary>
    /// Scans a comment from the current position.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both RazorForge and Suflae use the same comment syntax:
    /// </para>
    /// <list type="bullet">
    ///   <item><description># - Regular comment (ignored)</description></item>
    ///   <item><description>### - Documentation comment (tokenized)</description></item>
    /// </list>
    /// <para>
    /// All comments extend to the end of the line. The newline character
    /// is not consumed.
    /// </para>
    /// </remarks>
    private void ScanComment()
    {
        // The first `#` is consumed: the comment starts one character back, and never spans a line.
        int commentStart = _position - 1;
        int commentColumn = _column - 1;
        int commentLine = _line;
        bool isDoc = Peek() == '#' && Peek(offset: 1) == '#';
        int end = _position;
        while (end < _source.Length && _source[index: end] != '\n')
        {
            end++;
        }

        Comments.Add(item: new CommentTrivia(Line: commentLine, Column: commentColumn,
            Text: _source[commentStart..end], IsDoc: isDoc, Position: commentStart));

        // Check for doc comment (###)
        if (isDoc)
        {
            Advance(); // consume second #
            Advance(); // consume third #

            int start = _position;

            // Consume until end of line
            while (Peek() != '\n' && !IsAtEnd())
            {
                Advance();
            }

            string text = _source.Substring(startIndex: start, length: _position - start);
            AddToken(type: TokenType.DocComment, text: text);
        }
        else
        {
            // Regular comment - consume until end of line (no token emitted)
            while (Peek() != '\n' && !IsAtEnd())
            {
                Advance();
            }
        }
    }

    #endregion
}
