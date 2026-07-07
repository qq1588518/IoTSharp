using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using SonnetDB.Sql;

namespace SonnetDB.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("SqlLexer")]
public class SqlLexerBenchmark
{
    private string _script = string.Empty;

    [Params(128)]
    public int InsertRows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var sb = new StringBuilder();
        sb.AppendLine("REM lexer benchmark script");
        sb.AppendLine("CREATE MEASUREMENT cpu (host TAG, region TAG STRING, usage FIELD FLOAT, active FIELD BOOL DEFAULT true);");
        sb.AppendLine("CREATE MEASUREMENT docs (tenant TAG, embedding FIELD VECTOR(4) WITH INDEX hnsw(m=16, ef=200));");
        sb.Append("INSERT INTO cpu (host, region, time, usage, active) VALUES ");
        for (int i = 0; i < InsertRows; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append("('srv-");
            sb.Append(i % 16);
            sb.Append("', 'us-east', ");
            sb.Append(1_700_000_000_000L + i);
            sb.Append(", ");
            sb.Append((i % 100) + 0.125d);
            sb.Append(", ");
            sb.Append(i % 2 == 0 ? "true" : "false");
            sb.Append(')');
        }
        sb.AppendLine(";");
        sb.AppendLine("SELECT time, moving_average(usage, 5) AS ma FROM cpu WHERE host = 'srv-1' AND usage >= 1.5e2 GROUP BY time(1m) ORDER BY time DESC LIMIT 100 OFFSET 10;");
        sb.AppendLine("SELECT * FROM docs WHERE embedding <=> [0.1, -0.2, 0.3, 0.4] < 0.25;");
        sb.AppendLine("SELECT \"odd \"\"column\"\"\", 'O''Hara' FROM cpu WHERE active != false;");
        _script = sb.ToString();
    }

    [Benchmark(Baseline = true, Description = "legacy branch lexer")]
    public int LegacyBranchLexer()
    {
        var lexer = new LegacySqlLexer(_script);
        var count = 0;
        while (true)
        {
            var token = lexer.NextToken();
            count++;
            if (token.Kind == TokenKind.EndOfFile)
                return count;
        }
    }

    [Benchmark(Description = "SearchValues lexer")]
    public int SearchValuesLexer()
    {
        var lexer = new SqlLexer(_script);
        var count = 0;
        while (true)
        {
            var token = lexer.NextToken();
            count++;
            if (token.Kind == TokenKind.EndOfFile)
                return count;
        }
    }

    private sealed class LegacySqlLexer
    {
        private static readonly Dictionary<string, TokenKind> _keywords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["create"] = TokenKind.KeywordCreate,
            ["measurement"] = TokenKind.KeywordMeasurement,
            ["insert"] = TokenKind.KeywordInsert,
            ["into"] = TokenKind.KeywordInto,
            ["values"] = TokenKind.KeywordValues,
            ["select"] = TokenKind.KeywordSelect,
            ["from"] = TokenKind.KeywordFrom,
            ["where"] = TokenKind.KeywordWhere,
            ["group"] = TokenKind.KeywordGroup,
            ["by"] = TokenKind.KeywordBy,
            ["time"] = TokenKind.KeywordTime,
            ["delete"] = TokenKind.KeywordDelete,
            ["and"] = TokenKind.KeywordAnd,
            ["or"] = TokenKind.KeywordOr,
            ["not"] = TokenKind.KeywordNot,
            ["as"] = TokenKind.KeywordAs,
            ["null"] = TokenKind.KeywordNull,
            ["default"] = TokenKind.KeywordDefault,
            ["true"] = TokenKind.KeywordTrue,
            ["false"] = TokenKind.KeywordFalse,
            ["tag"] = TokenKind.KeywordTag,
            ["field"] = TokenKind.KeywordField,
            ["float"] = TokenKind.KeywordFloat,
            ["int"] = TokenKind.KeywordInt,
            ["bool"] = TokenKind.KeywordBool,
            ["string"] = TokenKind.KeywordString,
            ["vector"] = TokenKind.KeywordVector,
            ["geopoint"] = TokenKind.KeywordGeoPoint,
            ["user"] = TokenKind.KeywordUser,
            ["password"] = TokenKind.KeywordPassword,
            ["grant"] = TokenKind.KeywordGrant,
            ["revoke"] = TokenKind.KeywordRevoke,
            ["on"] = TokenKind.KeywordOn,
            ["to"] = TokenKind.KeywordTo,
            ["with"] = TokenKind.KeywordWith,
            ["read"] = TokenKind.KeywordRead,
            ["write"] = TokenKind.KeywordWrite,
            ["admin"] = TokenKind.KeywordAdmin,
            ["database"] = TokenKind.KeywordDatabase,
            ["drop"] = TokenKind.KeywordDrop,
            ["alter"] = TokenKind.KeywordAlter,
            ["show"] = TokenKind.KeywordShow,
            ["users"] = TokenKind.KeywordUsers,
            ["grants"] = TokenKind.KeywordGrants,
            ["databases"] = TokenKind.KeywordDatabases,
            ["for"] = TokenKind.KeywordFor,
            ["superuser"] = TokenKind.KeywordSuperuser,
            ["tokens"] = TokenKind.KeywordTokens,
            ["token"] = TokenKind.KeywordToken,
            ["issue"] = TokenKind.KeywordIssue,
            ["measurements"] = TokenKind.KeywordMeasurements,
            ["tables"] = TokenKind.KeywordTables,
            ["describe"] = TokenKind.KeywordDescribe,
            ["desc"] = TokenKind.KeywordDesc,
            ["order"] = TokenKind.KeywordOrder,
            ["asc"] = TokenKind.KeywordAsc,
            ["offset"] = TokenKind.KeywordOffset,
            ["fetch"] = TokenKind.KeywordFetch,
            ["limit"] = TokenKind.KeywordLimit,
        };

        private readonly string _source;
        private int _position;

        internal LegacySqlLexer(string source)
        {
            _source = source;
        }

        internal Token NextToken()
        {
            SkipWhitespaceAndComments();

            if (_position >= _source.Length)
                return new Token(TokenKind.EndOfFile, string.Empty, _position);

            var start = _position;
            var ch = _source[_position];

            if (IsIdentifierStart(ch))
                return ScanIdentifierOrKeyword(start);

            if (char.IsAsciiDigit(ch))
                return ScanNumber(start);

            if (ch == '\'')
                return ScanString(start);

            if (ch == '"')
                return ScanQuotedIdentifier(start);

            switch (ch)
            {
                case '(': _position++; return new Token(TokenKind.LeftParen, "(", start);
                case ')': _position++; return new Token(TokenKind.RightParen, ")", start);
                case '[': _position++; return new Token(TokenKind.LeftBracket, "[", start);
                case ']': _position++; return new Token(TokenKind.RightBracket, "]", start);
                case ',': _position++; return new Token(TokenKind.Comma, ",", start);
                case ';': _position++; return new Token(TokenKind.Semicolon, ";", start);
                case '.': _position++; return new Token(TokenKind.Dot, ".", start);
                case '*': _position++; return new Token(TokenKind.Star, "*", start);
                case '+': _position++; return new Token(TokenKind.Plus, "+", start);
                case '-': _position++; return new Token(TokenKind.Minus, "-", start);
                case '/': _position++; return new Token(TokenKind.Slash, "/", start);
                case '%': _position++; return new Token(TokenKind.Percent, "%", start);
                case '=': _position++; return new Token(TokenKind.Equal, "=", start);
                case '!':
                    if (Peek(1) == '=') { _position += 2; return new Token(TokenKind.NotEqual, "!=", start); }
                    throw new SqlParseException("无法识别的字符 '!'", start);
                case '<':
                    if (Peek(1) == '=' && Peek(2) == '>')
                    {
                        _position += 3;
                        return new Token(TokenKind.VectorCosineDistance, "<=>", start);
                    }
                    if (Peek(1) == '-' && Peek(2) == '>')
                    {
                        _position += 3;
                        return new Token(TokenKind.VectorL2Distance, "<->", start);
                    }
                    if (Peek(1) == '#' && Peek(2) == '>')
                    {
                        _position += 3;
                        return new Token(TokenKind.VectorInnerProduct, "<#>", start);
                    }
                    if (Peek(1) == '=') { _position += 2; return new Token(TokenKind.LessThanOrEqual, "<=", start); }
                    if (Peek(1) == '>') { _position += 2; return new Token(TokenKind.NotEqual, "<>", start); }
                    _position++;
                    return new Token(TokenKind.LessThan, "<", start);
                case '>':
                    if (Peek(1) == '=') { _position += 2; return new Token(TokenKind.GreaterThanOrEqual, ">=", start); }
                    _position++;
                    return new Token(TokenKind.GreaterThan, ">", start);
                default:
                    throw new SqlParseException($"无法识别的字符 '{ch}'", start);
            }
        }

        private Token ScanIdentifierOrKeyword(int start)
        {
            while (_position < _source.Length && IsIdentifierContinue(_source[_position]))
                _position++;

            var text = _source.Substring(start, _position - start);
            return _keywords.TryGetValue(text, out var keyword)
                ? new Token(keyword, text, start)
                : new Token(TokenKind.IdentifierLiteral, text, start);
        }

        private Token ScanNumber(int start)
        {
            while (_position < _source.Length && char.IsAsciiDigit(_source[_position]))
                _position++;

            var isFloat = false;

            if (_position < _source.Length && _source[_position] == '.' && char.IsAsciiDigit(Peek(1)))
            {
                isFloat = true;
                _position++;
                while (_position < _source.Length && char.IsAsciiDigit(_source[_position]))
                    _position++;
            }

            if (_position < _source.Length && (_source[_position] == 'e' || _source[_position] == 'E'))
            {
                isFloat = true;
                _position++;
                if (_position < _source.Length && (_source[_position] == '+' || _source[_position] == '-'))
                    _position++;
                if (_position >= _source.Length || !char.IsAsciiDigit(_source[_position]))
                    throw new SqlParseException("浮点数指数缺少数字", start);
                while (_position < _source.Length && char.IsAsciiDigit(_source[_position]))
                    _position++;
            }

            var numericText = _source.Substring(start, _position - start);
            if (!isFloat && _position < _source.Length && IsDurationSuffixStart(_source[_position]))
            {
                var suffixStart = _position;
                string suffix;
                if (_position + 1 < _source.Length && IsDurationTwoCharSuffix(_source, _position))
                {
                    suffix = _source.Substring(_position, 2);
                    _position += 2;
                }
                else
                {
                    suffix = _source[_position].ToString();
                    _position++;
                }

                if (_position < _source.Length && IsIdentifierContinue(_source[_position]))
                    throw new SqlParseException($"无效的 duration 后缀 '{suffix}{_source[_position]}'", suffixStart);

                if (!long.TryParse(numericText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawValue) || rawValue < 0)
                    throw new SqlParseException($"非法的 duration 数值 '{numericText}'", start);

                return new Token(TokenKind.DurationLiteral, numericText, start, IntegerValue: ConvertToMilliseconds(rawValue, suffix, suffixStart));
            }

            return isFloat
                ? new Token(TokenKind.FloatLiteral, numericText, start, DoubleValue: double.Parse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture))
                : new Token(TokenKind.IntegerLiteral, numericText, start, IntegerValue: long.Parse(numericText, CultureInfo.InvariantCulture));
        }

        private Token ScanQuotedIdentifier(int start)
        {
            _position++;
            var sb = new StringBuilder();
            while (_position < _source.Length)
            {
                var ch = _source[_position];
                if (ch == '"')
                {
                    if (Peek(1) == '"')
                    {
                        sb.Append('"');
                        _position += 2;
                        continue;
                    }
                    _position++;
                    return new Token(TokenKind.IdentifierLiteral, sb.ToString(), start);
                }
                sb.Append(ch);
                _position++;
            }
            throw new SqlParseException("未闭合的引号标识符", start);
        }

        private Token ScanString(int start)
        {
            _position++;
            var sb = new StringBuilder();
            while (_position < _source.Length)
            {
                var ch = _source[_position];
                if (ch == '\'')
                {
                    if (Peek(1) == '\'')
                    {
                        sb.Append('\'');
                        _position += 2;
                        continue;
                    }
                    _position++;
                    return new Token(TokenKind.StringLiteral, sb.ToString(), start);
                }
                sb.Append(ch);
                _position++;
            }
            throw new SqlParseException("未闭合的字符串字面量", start);
        }

        private void SkipWhitespaceAndComments()
        {
            while (_position < _source.Length)
            {
                var ch = _source[_position];
                if (char.IsWhiteSpace(ch))
                {
                    _position++;
                    continue;
                }
                if (IsLineCommentStart())
                {
                    SkipToEndOfLine();
                    continue;
                }
                if (ch == '/' && Peek(1) == '*')
                {
                    var commentStart = _position;
                    _position += 2;
                    while (_position < _source.Length && !(_source[_position] == '*' && Peek(1) == '/'))
                        _position++;
                    if (_position >= _source.Length)
                        throw new SqlParseException("未闭合的块注释", commentStart);
                    _position += 2;
                    continue;
                }
                return;
            }
        }

        private bool IsLineCommentStart()
        {
            var ch = _source[_position];
            if (ch == '-' && Peek(1) == '-')
                return true;

            if (ch == '/' && Peek(1) == '/')
                return true;

            return IsRemCommentStart();
        }

        private bool IsRemCommentStart()
        {
            if (!StartsWithIgnoreCase("rem"))
                return false;

            var next = Peek(3);
            if (next != '\0' && !char.IsWhiteSpace(next))
                return false;

            return IsAtLineStartOrAfterStatementTerminator();
        }

        private void SkipToEndOfLine()
        {
            _position += StartsWithIgnoreCase("rem") ? 3 : 2;
            while (_position < _source.Length && _source[_position] != '\n' && _source[_position] != '\r')
                _position++;
        }

        private bool StartsWithIgnoreCase(string value)
        {
            if (_position + value.Length > _source.Length)
                return false;

            for (var i = 0; i < value.Length; i++)
            {
                if (char.ToUpperInvariant(_source[_position + i]) != char.ToUpperInvariant(value[i]))
                    return false;
            }

            return true;
        }

        private bool IsAtLineStartOrAfterStatementTerminator()
        {
            for (var index = _position - 1; index >= 0; index--)
            {
                var ch = _source[index];
                if (ch == '\n' || ch == '\r')
                    return true;

                if (!char.IsWhiteSpace(ch))
                    return ch == ';';
            }

            return true;
        }

        private char Peek(int offset)
        {
            var index = _position + offset;
            return index < _source.Length ? _source[index] : '\0';
        }

        private static bool IsIdentifierStart(char ch) => ch == '_' || char.IsLetter(ch);

        private static bool IsIdentifierContinue(char ch) => ch == '_' || char.IsLetterOrDigit(ch);

        private static bool IsDurationSuffixStart(char ch)
            => ch is 'n' or 'u' or 'm' or 's' or 'h' or 'd';

        private static bool IsDurationTwoCharSuffix(string s, int index)
        {
            var a = s[index];
            var b = s[index + 1];
            return (a == 'n' && b == 's') || (a == 'u' && b == 's') || (a == 'm' && b == 's');
        }

        private static long ConvertToMilliseconds(long value, string suffix, int position)
        {
            try
            {
                return suffix switch
                {
                    "ns" => checked(value / 1_000_000L),
                    "us" => checked(value / 1_000L),
                    "ms" => value,
                    "s" => checked(value * 1_000L),
                    "m" => checked(value * 60_000L),
                    "h" => checked(value * 3_600_000L),
                    "d" => checked(value * 86_400_000L),
                    _ => throw new SqlParseException($"未知的 duration 单位 '{suffix}'", position),
                };
            }
            catch (OverflowException)
            {
                throw new SqlParseException($"duration 计算溢出（{value}{suffix}）", position);
            }
        }
    }
}
