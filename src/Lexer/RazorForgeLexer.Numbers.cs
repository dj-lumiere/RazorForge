using Builder.Diagnostics;

using Builder.Tokenizer;

namespace RazorForge.Lexer;

/// <summary>
/// Partial class containing numeric literal scanning memberRoutines for the RazorForge lexer.
/// </summary>
/// <remarks>
/// A number carries no type of its own: it is an undecided integer or decimal token, typed later by the place it
/// goes or by the declaration it starts. Only unit suffixes (durations, sizes) and the imaginary <c>i</c> remain.
/// </remarks>
public partial class RazorForgeLexer
{
    #region Decimal Numbers

    /// <summary>
    /// Scans a decimal numeric literal, handling integers, floats, and unit-suffixed numbers.
    /// </summary>
    private void ScanNumber()
    {
        // Consume digits and underscores
        while (char.IsDigit(c: Peek()) || Peek() == '_')
        {
            Advance();
        }

        bool isFloat = ScanDecimalFractionalPart();
        if (ScanScientificNotation())
        {
            isFloat = true;
        }

        // Skip underscore before suffix after scientific notation (e.g., 3.4e10_i)
        if (Peek() == '_' && char.IsLetter(c: Peek(offset: 1)))
        {
            Advance();
        }

        // Check for a suffix
        if (char.IsLetter(c: Peek()))
        {
            EmitDecimalSuffixedToken(isFloat: isFloat);
        }
        else
        {
            AddToken(type: isFloat
                ? TokenType.UndecidedDecimal
                : TokenType.UndecidedInteger);
        }
    }

    /// <summary>
    /// Scans the optional fractional part of a decimal literal (e.g. the <c>.25</c> in <c>3.25</c>).
    /// </summary>
    /// <returns><c>true</c> if a fractional part was consumed.</returns>
    private bool ScanDecimalFractionalPart()
    {
        if (Peek() != '.' || !char.IsDigit(c: Peek(offset: 1)))
        {
            return false;
        }

        Advance(); // consume '.'
        while (char.IsDigit(c: Peek()) || Peek() == '_')
        {
            Advance();
        }

        return true;
    }

    /// <summary>
    /// Scans the optional scientific-notation exponent (<c>e</c>/<c>E</c> followed by an optional
    /// sign and exponent digits).
    /// </summary>
    /// <returns><c>true</c> if a scientific-notation suffix was consumed.</returns>
    private bool ScanScientificNotation()
    {
        if (Peek() != 'e' && Peek() != 'E')
        {
            return false;
        }

        Advance(); // consume 'e' or 'E'
        if (Peek() == '+' || Peek() == '-')
        {
            Advance();
        }

        while (char.IsDigit(c: Peek()))
        {
            Advance();
        }

        return true;
    }

    /// <summary>
    /// Consumes a decimal-number suffix and emits the matching token, dispatching over the imaginary, memory-size
    /// and duration suffixes. Throws on a suffix that names a type and on an unknown one. Assumes the caller has
    /// confirmed a letter begins the suffix.
    /// </summary>
    private void EmitDecimalSuffixedToken(bool isFloat)
    {
        int suffixStart = _position;
        int suffixColumn = _column;
        while (char.IsLetterOrDigit(c: Peek()))
        {
            Advance();
        }

        string suffix =
            _source.Substring(startIndex: suffixStart, length: _position - suffixStart);

        if (TypeNamingSuffixes.TryGetValue(key: suffix, value: out string? typeName))
        {
            throw TypeNamingSuffixError(typeName: typeName, suffixStart: suffixStart, suffixColumn: suffixColumn);
        }

        if (suffix == ImaginarySuffix)
        {
            AddToken(type: TokenType.ImaginaryLiteral);
        }
        else if (_byteSizeSuffixToTokenType.TryGetValue(key: suffix,
                     value: out TokenType memoryType))
        {
            AddToken(type: memoryType);
        }
        else if (_durationSuffixToTokenType.TryGetValue(key: suffix,
                     value: out TokenType durationToken))
        {
            AddToken(type: durationToken);
        }
        else
        {
            throw new GrammarException(code: ClassifySuffixError(suffix: suffix, isFloat: isFloat),
                message: $"Unknown suffix '{suffix}'",
                fileName: _fileName,
                line: _line,
                column: _column,
                language: _language);
        }
    }

    /// <summary>
    /// The error for a suffix that names the number's type (<c>5_u8</c>). The caret sits on the suffix, with its
    /// <c>_</c> separator when there is one, and the message shows the declaration that states the type instead.
    /// </summary>
    private GrammarException TypeNamingSuffixError(string typeName, int suffixStart, int suffixColumn)
    {
        bool separated = suffixStart > _tokenStart && _source[index: suffixStart - 1] == '_';
        int digitsEnd = separated
            ? suffixStart - 1
            : suffixStart;
        string written = _source.Substring(startIndex: _tokenStart, length: _position - _tokenStart);
        string digits = _source.Substring(startIndex: _tokenStart, length: digitsEnd - _tokenStart);
        return new GrammarException(code: GrammarDiagnosticCode.TypeNamingSuffix,
            message: $"`{written}` names its type with a suffix, and a number no longer carries its type. " +
                     $"Write the type where the value is declared (`var x: {typeName} = {digits}`), or let the " +
                     "place the number goes decide it (an argument, an assignment, the other side of an " +
                     "operator). `migrate --step suffix` rewrites old code.",
            fileName: _fileName,
            line: _line,
            column: separated
                ? suffixColumn - 1
                : suffixColumn,
            language: _language);
    }

    #endregion

    #region Prefixed Numbers

    /// <summary>
    /// Scans a prefixed numeric literal (hexadecimal, binary, or hex float).
    /// Hex floats use C99 format: 0x1.ABCDp5 (hex mantissa + binary exponent).
    /// </summary>
    private void ScanPrefixedNumber(bool isHex)
    {
        bool isHexFloat = false;

        // Consume valid digits and underscores
        if (isHex)
        {
            isHexFloat = ScanHexDigitsAndFloatParts();
        }
        else
        {
            while (Peek() == '0' || Peek() == '1' || Peek() == '_')
            {
                Advance();
            }
        }

        // Skip underscore before suffix (e.g., 0x1_i)
        if (Peek() == '_' && char.IsLetter(c: Peek(offset: 1)))
        {
            Advance();
        }

        // Check for a suffix
        if (char.IsLetter(c: Peek()))
        {
            EmitPrefixedSuffixedToken(isHex: isHex);
        }
        else
        {
            AddToken(type: isHexFloat
                ? TokenType.UndecidedDecimal
                : TokenType.UndecidedInteger);
        }
    }

    /// <summary>
    /// Consumes hex digits/underscores plus any hex-float fractional part (0x1.ABCD) and binary
    /// exponent (p5), treating an underscore that precedes a known type suffix as the suffix
    /// separator rather than a digit separator.
    /// </summary>
    /// <returns><c>true</c> if a hex-float fractional part or exponent was seen.</returns>
    private bool ScanHexDigitsAndFloatParts()
    {
        ScanHexIntegerDigits();
        bool isHexFloat = ScanHexFractionalPart();
        if (ScanHexBinaryExponent())
        {
            isHexFloat = true;
        }

        return isHexFloat;
    }

    /// <summary>
    /// Consumes hex digits and underscores, stopping early when an underscore introduces a type
    /// suffix (e.g. <c>_addr</c>) rather than a digit separator (e.g. <c>_ABCD</c>).
    /// </summary>
    private void ScanHexIntegerDigits()
    {
        while (IsHexDigit(c: Peek()) || Peek() == '_')
        {
            // When encountering underscore in hex mode, check if what follows
            // is a type suffix (e.g., _addr) rather than a digit separator (e.g., _ABCD)
            if (Peek() == '_' && UnderscoreIntroducesSuffix())
            {
                Advance(); // consume the underscore
                break; // suffix follows
            }

            Advance();
        }
    }

    /// <summary>
    /// Scans the optional hex-float fractional part (e.g. the <c>.ABCD</c> in <c>0x1.ABCDp5</c>). A hex
    /// float always carries its <c>p</c> exponent, so the <c>.</c> is a fraction point only when hex
    /// digits and then a <c>p</c> exponent follow it; otherwise it is left alone, which keeps a member
    /// call on a hex integer (<c>0xFF.abs()</c>) from being read as a fraction.
    /// </summary>
    /// <returns><c>true</c> if a fractional part was consumed.</returns>
    private bool ScanHexFractionalPart()
    {
        if (Peek() != '.' || !IsHexDigit(c: Peek(offset: 1)))
        {
            return false;
        }

        int lookAhead = 1;
        while (IsHexDigit(c: Peek(offset: lookAhead)) || Peek(offset: lookAhead) == '_')
        {
            lookAhead++;
        }

        if (!IsBinaryExponentAt(offset: lookAhead))
        {
            return false;
        }

        Advance(); // consume '.'
        while (IsHexDigit(c: Peek()) || Peek() == '_')
        {
            Advance();
        }

        return true;
    }

    /// <summary>
    /// Whether a hex-float binary exponent (<c>p</c>/<c>P</c>, an optional sign, then at least one
    /// decimal digit) starts <paramref name="offset"/> characters ahead.
    /// </summary>
    private bool IsBinaryExponentAt(int offset)
    {
        if (Peek(offset: offset) != 'p' && Peek(offset: offset) != 'P')
        {
            return false;
        }

        int digitAt = Peek(offset: offset + 1) is '+' or '-'
            ? offset + 2
            : offset + 1;
        return char.IsDigit(c: Peek(offset: digitAt));
    }

    /// <summary>
    /// Scans the optional hex-float binary exponent (<c>p</c>/<c>P</c> with optional sign and
    /// decimal exponent digits, e.g. the <c>p5</c> in <c>0x1.0p5</c>).
    /// </summary>
    /// <returns><c>true</c> if a binary exponent was consumed.</returns>
    private bool ScanHexBinaryExponent()
    {
        if (!IsBinaryExponentAt(offset: 0))
        {
            return false;
        }

        Advance(); // consume 'p'/'P'
        if (Peek() == '+' || Peek() == '-')
        {
            Advance();
        }

        while (char.IsDigit(c: Peek()))
        {
            Advance();
        }

        return true;
    }

    /// <summary>
    /// Peeks whether the underscore at the current position introduces a known type suffix
    /// (e.g., <c>_i</c>, or a type-naming one such as <c>_b64</c>, which is an error) rather than serving as a hex digit separator (e.g., <c>_ABCD</c>).
    /// </summary>
    private bool UnderscoreIntroducesSuffix()
    {
        int lookAhead = 1;
        while (char.IsLetterOrDigit(c: Peek(offset: lookAhead)))
        {
            lookAhead++;
        }

        if (lookAhead > 1)
        {
            string candidate = _source.Substring(startIndex: _position + 1, length: lookAhead - 1);
            if (candidate == ImaginarySuffix || TypeNamingSuffixes.ContainsKey(key: candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Consumes a prefixed-number (hex/binary) suffix and emits the matching token. Throws on a suffix that names a
    /// type and on an unknown one. Assumes the caller has confirmed a letter begins the suffix.
    /// </summary>
    private void EmitPrefixedSuffixedToken(bool isHex)
    {
        int suffixStart = _position;
        int suffixColumn = _column;
        while (char.IsLetterOrDigit(c: Peek()))
        {
            Advance();
        }

        string suffix =
            _source.Substring(startIndex: suffixStart, length: _position - suffixStart);

        if (TypeNamingSuffixes.TryGetValue(key: suffix, value: out string? typeName))
        {
            throw TypeNamingSuffixError(typeName: typeName, suffixStart: suffixStart, suffixColumn: suffixColumn);
        }

        if (suffix == ImaginarySuffix)
        {
            AddToken(type: TokenType.ImaginaryLiteral);
        }
        else
        {
            string baseType = isHex
                ? "hex"
                : "binary";
            throw new GrammarException(code: GrammarDiagnosticCode.InvalidNumericLiteral,
                message: $"Unknown {baseType} suffix '{suffix}'",
                fileName: _fileName,
                line: _line,
                column: _column,
                language: _language);
        }
    }

    /// <summary>
    /// Scans an octal numeric literal (0o prefix).
    /// </summary>
    private void ScanOctalNumber()
    {
        // Consume valid octal digits and underscores
        while (Peek() >= '0' && Peek() <= '7' || Peek() == '_')
        {
            Advance();
        }

        // Check for a suffix
        if (char.IsLetter(c: Peek()))
        {
            int suffixStart = _position;
            int suffixColumn = _column;
            while (char.IsLetterOrDigit(c: Peek()))
            {
                Advance();
            }

            string suffix =
                _source.Substring(startIndex: suffixStart, length: _position - suffixStart);

            if (TypeNamingSuffixes.TryGetValue(key: suffix, value: out string? typeName))
            {
                throw TypeNamingSuffixError(typeName: typeName, suffixStart: suffixStart, suffixColumn: suffixColumn);
            }

            if (suffix == ImaginarySuffix)
            {
                AddToken(type: TokenType.ImaginaryLiteral);
            }
            else
            {
                throw new GrammarException(code: GrammarDiagnosticCode.InvalidNumericLiteral,
                    message: $"Unknown octal suffix '{suffix}'",
                    fileName: _fileName,
                    line: _line,
                    column: _column,
                    language: _language);
            }
        }
        else
        {
            AddToken(type: TokenType.UndecidedInteger);
        }
    }

    #endregion

    #region Suffix Error Classification

    /// <summary>
    /// Classifies an unknown numeric suffix into the most specific diagnostic code.
    /// </summary>
    private static GrammarDiagnosticCode ClassifySuffixError(string suffix, bool isFloat)
    {
        char first = char.ToLowerInvariant(c: suffix[index: 0]);

        // Memory-unit-like suffixes (b, k, m, g)
        if (first is 'b' or 'k' or 'm' or 'g')
        {
            return GrammarDiagnosticCode.InvalidMemoryLiteral;
        }

        // Duration-like suffixes (w, d, h, s, or contains ms/us/ns)
        if (first is 'w' or 'd' or 'h' or 's')
        {
            return GrammarDiagnosticCode.InvalidDurationLiteral;
        }

        string lower = suffix.ToLowerInvariant();
        if (lower.Contains(value: "ms") || lower.Contains(value: "us") ||
            lower.Contains(value: "ns"))
        {
            return GrammarDiagnosticCode.InvalidDurationLiteral;
        }

        // Float with decimal point
        if (isFloat)
        {
            return GrammarDiagnosticCode.InvalidFloatLiteral;
        }

        return GrammarDiagnosticCode.InvalidNumericLiteral;
    }

    #endregion
}
