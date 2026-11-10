using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ExcerptStringPutback;

internal sealed class Hit
{
    public required int CharIndex { get; init; }

    public required int CharLength { get; init; }

    public required string Before { get; init; }

    public required string Source { get; init; }

    public required string After { get; init; }

    public required string[] Key { get; init; }

    public required int ByteStart { get; init; }

    public required int ByteLength { get; init; }
}

internal sealed class Re2Pattern
{
    private static string Invalid => Text.Current.InvalidPattern;

    private static string Lookaround => Text.Current.Lookaround;

    private static readonly Dictionary<string, string> Posix = new(StringComparer.Ordinal)
    {
        ["alnum"] = "0-9A-Za-z",
        ["alpha"] = "A-Za-z",
        ["ascii"] = "\\x00-\\x7F",
        ["blank"] = "\\t ",
        ["cntrl"] = "\\x00-\\x1F\\x7F",
        ["digit"] = "0-9",
        ["graph"] = "!-~",
        ["lower"] = "a-z",
        ["print"] = " -~",
        ["punct"] = "!-/:-@[-\\u0060{-~",
        ["space"] = "\\t\\n\\v\\f\\r ",
        ["upper"] = "A-Z",
        ["word"] = "0-9A-Za-z_",
        ["xdigit"] = "0-9A-Fa-f",
    };

    private readonly string pattern;
    private readonly StringBuilder output = new();
    private readonly List<string> keys = new();
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private readonly Stack<Flags> frames = new();
    private readonly Stack<Flags> emittedStack = new();
    private Flags current;
    private Flags emitted;
    private const string SourceName = "excerptSource";

    private int index;
    private int groupNumber;
    private string? sourceName;
    private bool haveAtom;
    private bool lastRepeat;

    private Re2Pattern(string pattern)
    {
        this.pattern = pattern;
    }

    public static Compiled Compile(string pattern)
    {
        var parser = new Re2Pattern(pattern);
        parser.Parse();
        var translated = parser.output.ToString();
        try
        {
            var regex = new Regex(translated, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(30));
            return new Compiled(regex, parser.keys, parser.sourceName);
        }
        catch (ArgumentException)
        {
            throw new ToolException(Invalid);
        }
    }

    private void Parse()
    {
        while (index < pattern.Length)
        {
            var c = pattern[index];
            if (c is '*' or '+' or '?')
            {
                ParseSimpleQuantifier(c);
                continue;
            }

            if (c == '{')
            {
                ParseBraceOrLiteral();
                continue;
            }

            if (c == '|')
            {
                output.Append('|');
                index++;
                haveAtom = false;
                lastRepeat = false;
                continue;
            }

            if (c == ')')
            {
                CloseGroup();
                continue;
            }

            if (c == '(')
            {
                Open();
                continue;
            }

            if (c == '^')
            {
                SyncFlags();
                output.Append(current.Multiline ? "^" : "\\A");
                index++;
                haveAtom = true;
                lastRepeat = false;
                continue;
            }

            if (c == '$')
            {
                SyncFlags();
                output.Append(current.Multiline ? "$" : "\\z");
                index++;
                haveAtom = true;
                lastRepeat = false;
                continue;
            }

            if (c == '.')
            {
                SyncFlags();
                output.Append('.');
                index++;
                haveAtom = true;
                lastRepeat = false;
                continue;
            }

            if (c == '[')
            {
                SyncFlags();
                ParseClass();
                haveAtom = true;
                lastRepeat = false;
                continue;
            }

            if (c == '\\')
            {
                SyncFlags();
                ParseEscapeAtom();
                continue;
            }

            SyncFlags();
            output.Append(c);
            index++;
            haveAtom = true;
            lastRepeat = false;
        }

        if (frames.Count != 0)
        {
            throw new ToolException(Invalid);
        }
    }

    private void Open()
    {
        if (index + 1 < pattern.Length && pattern[index + 1] == '?')
        {
            OpenSpecial();
            return;
        }

        index++;
        groupNumber++;
        if (sourceName is null)
        {
            sourceName = SourceName;
            if (!seen.Add(SourceName))
            {
                throw new ToolException(Text.Current.DuplicateCaptureName);
            }

            PushGroup("(?<" + SourceName + ">");
            return;
        }

        PushGroup("(");
    }

    private void OpenSpecial()
    {
        index += 2;
        if (index >= pattern.Length)
        {
            throw new ToolException(Invalid);
        }

        var c = pattern[index];
        if (c == ':')
        {
            index++;
            PushGroup("(?:");
            return;
        }

        if (c == 'P' || c == '<')
        {
            ParseNamed();
            return;
        }

        if (c is '=' or '!')
        {
            throw new ToolException(Lookaround);
        }

        if (c == '#')
        {
            throw new ToolException(Invalid);
        }

        ParseFlags();
    }

    private void ParseNamed()
    {
        if (pattern[index] == 'P')
        {
            index++;
            if (index >= pattern.Length || pattern[index] != '<')
            {
                throw new ToolException(Invalid);
            }
        }
        else if (index + 1 < pattern.Length && pattern[index + 1] is '=' or '!')
        {
            throw new ToolException(Lookaround);
        }

        index++;
        var start = index;
        while (index < pattern.Length && pattern[index] != '>')
        {
            index++;
        }

        if (index >= pattern.Length)
        {
            throw new ToolException(Invalid);
        }

        var name = pattern[start..index];
        index++;
        if (!IsCaptureName(name))
        {
            throw new ToolException(Invalid);
        }

        if (!seen.Add(name))
        {
            throw new ToolException(Text.Current.DuplicateCaptureName);
        }

        groupNumber++;
        keys.Add(name);
        PushGroup("(?<" + name + ">");
    }

    private void ParseFlags()
    {
        var next = current;
        var negate = false;
        var saw = false;
        while (index < pattern.Length)
        {
            var c = pattern[index];
            if (c is 'i' or 'm' or 's' or 'U')
            {
                next = next.With(c, !negate);
                saw = true;
                index++;
                continue;
            }

            if (c == '-')
            {
                if (negate)
                {
                    break;
                }

                negate = true;
                saw = false;
                index++;
                continue;
            }

            break;
        }

        if (index >= pattern.Length || (negate && !saw) || pattern[index] is not ':' and not ')')
        {
            throw new ToolException(Invalid);
        }

        if (pattern[index] == ':')
        {
            index++;
            var saved = current;
            current = next;
            PushGroup("(?:");
            current = next;
            frames.Pop();
            frames.Push(saved);
            return;
        }

        index++;
        current = next;
    }

    private void PushGroup(string opener)
    {
        frames.Push(current);
        emittedStack.Push(emitted);
        output.Append(opener);
        haveAtom = false;
        lastRepeat = false;
    }

    private void CloseGroup()
    {
        if (frames.Count == 0)
        {
            throw new ToolException(Invalid);
        }

        current = frames.Pop();
        emitted = emittedStack.Pop();
        output.Append(')');
        index++;
        haveAtom = true;
        lastRepeat = false;
    }

    private void ParseSimpleQuantifier(char op)
    {
        if (lastRepeat || !haveAtom)
        {
            throw new ToolException(Invalid);
        }

        index++;
        var lazy = false;
        if (index < pattern.Length && pattern[index] == '?')
        {
            lazy = true;
            index++;
        }

        if (current.Ungreedy)
        {
            lazy = !lazy;
        }

        output.Append(op);
        if (lazy)
        {
            output.Append('?');
        }

        lastRepeat = true;
        haveAtom = false;
    }

    private void ParseBraceOrLiteral()
    {
        var kind = ScanRepeat(out var min, out var max, out var end);
        if (kind == RepeatKind.Literal)
        {
            SyncFlags();
            output.Append('{');
            index++;
            haveAtom = true;
            lastRepeat = false;
            return;
        }

        if (lastRepeat || !haveAtom || kind == RepeatKind.Bad)
        {
            throw new ToolException(Invalid);
        }

        index = end;
        var lazy = false;
        if (index < pattern.Length && pattern[index] == '?')
        {
            lazy = true;
            index++;
        }

        if (current.Ungreedy)
        {
            lazy = !lazy;
        }

        output.Append('{');
        output.Append(min.ToString(CultureInfo.InvariantCulture));
        if (max != min)
        {
            output.Append(',');
            if (max >= 0)
            {
                output.Append(max.ToString(CultureInfo.InvariantCulture));
            }
        }

        output.Append('}');
        if (lazy)
        {
            output.Append('?');
        }

        lastRepeat = true;
        haveAtom = false;
    }

    private RepeatKind ScanRepeat(out int min, out int max, out int end)
    {
        min = 0;
        max = 0;
        end = index;
        var p = index + 1;
        if (!TryInt(ref p, out min))
        {
            return RepeatKind.Literal;
        }

        if (p >= pattern.Length)
        {
            return RepeatKind.Literal;
        }

        if (pattern[p] == ',')
        {
            p++;
            if (p < pattern.Length && pattern[p] == '}')
            {
                max = -1;
                p++;
            }
            else if (!TryInt(ref p, out max) || p >= pattern.Length || pattern[p] != '}')
            {
                return RepeatKind.Literal;
            }
            else
            {
                p++;
            }
        }
        else if (pattern[p] == '}')
        {
            max = min;
            p++;
        }
        else
        {
            return RepeatKind.Literal;
        }

        end = p;
        if (min < 0 || min > 1000 || (max >= 0 && (max > 1000 || min > max)))
        {
            return RepeatKind.Bad;
        }

        return RepeatKind.Ok;
    }

    private bool TryInt(ref int p, out int value)
    {
        value = 0;
        if (p >= pattern.Length || pattern[p] is < '0' or > '9')
        {
            return false;
        }

        if (pattern[p] == '0' && p + 1 < pattern.Length && pattern[p + 1] is >= '0' and <= '9')
        {
            return false;
        }

        var digits = 0;
        while (p < pattern.Length && pattern[p] is >= '0' and <= '9')
        {
            digits++;
            if (digits > 4)
            {
                value = -1;
                while (p < pattern.Length && pattern[p] is >= '0' and <= '9')
                {
                    p++;
                }

                return true;
            }

            value = value * 10 + (pattern[p] - '0');
            p++;
        }

        return true;
    }

    private void ParseClass()
    {
        index++;
        var body = new StringBuilder();
        var negate = false;
        if (index < pattern.Length && pattern[index] == '^')
        {
            negate = true;
            index++;
        }

        var first = true;
        var closed = false;
        while (index < pattern.Length)
        {
            if (pattern[index] == ']' && !first)
            {
                index++;
                closed = true;
                break;
            }

            first = false;
            if (pattern[index] == '\\')
            {
                ParseClassEscape(body, ref negate);
                continue;
            }

            if (pattern[index] == '[' && index + 1 < pattern.Length && pattern[index + 1] == ':')
            {
                ParsePosix(body, ref negate);
                continue;
            }

            body.Append(pattern[index]);
            index++;
        }

        if (!closed)
        {
            throw new ToolException(Invalid);
        }

        output.Append('[');
        if (negate)
        {
            output.Append('^');
        }

        output.Append(body);
        output.Append(']');
    }

    private void ParseClassEscape(StringBuilder body, ref bool negate)
    {
        if (index + 1 >= pattern.Length)
        {
            throw new ToolException(Invalid);
        }

        var n = pattern[index + 1];
        if (n is 'd' or 'w' or 's')
        {
            body.Append(PositiveClass(n));
            index += 2;
            return;
        }

        if (n is 'D' or 'W' or 'S')
        {
            if (body.Length != 0)
            {
                throw new ToolException(Invalid);
            }

            negate = !negate;
            body.Append(PositiveClass(char.ToLowerInvariant(n)));
            index += 2;
            return;
        }

        if (n is 'b' or 'B' or 'A' or 'z' or 'Q')
        {
            throw new ToolException(Invalid);
        }

        if (n is 'k' or '8' or '9')
        {
            throw new ToolException(Lookaround);
        }

        AppendRune(body, ReadRuneEscape());
    }

    private void ParsePosix(StringBuilder body, ref bool negate)
    {
        var close = pattern.IndexOf(":]", index + 2, StringComparison.Ordinal);
        if (close < 0)
        {
            throw new ToolException(Invalid);
        }

        var name = pattern[(index + 2)..close];
        index = close + 2;
        var neg = false;
        if (name.StartsWith('^'))
        {
            neg = true;
            name = name[1..];
        }

        if (!Posix.TryGetValue(name, out var range))
        {
            throw new ToolException(Invalid);
        }

        if (neg)
        {
            if (body.Length != 0)
            {
                throw new ToolException(Invalid);
            }

            negate = !negate;
        }

        body.Append(range);
    }

    private void ParseEscapeAtom()
    {
        if (index + 1 >= pattern.Length)
        {
            throw new ToolException(Invalid);
        }

        var n = pattern[index + 1];
        if (n == 'Q')
        {
            index += 2;
            var any = false;
            while (index < pattern.Length && !(pattern[index] == '\\' && index + 1 < pattern.Length && pattern[index + 1] == 'E'))
            {
                AppendQuoted(pattern[index]);
                index++;
                any = true;
            }

            if (index < pattern.Length && pattern[index] == '\\')
            {
                index += 2;
            }

            if (any)
            {
                haveAtom = true;
                lastRepeat = false;
            }

            return;
        }

        if (n is 'A' or 'b' or 'B' or 'z')
        {
            output.Append('\\').Append(n);
            index += 2;
            haveAtom = true;
            lastRepeat = false;
            return;
        }

        if (n is 'd' or 'D' or 'w' or 'W' or 's' or 'S')
        {
            output.Append(n is 'd' or 'w' or 's'
                ? "[" + PositiveClass(n) + "]"
                : "[^" + PositiveClass(char.ToLowerInvariant(n)) + "]");
            index += 2;
            haveAtom = true;
            lastRepeat = false;
            return;
        }

        if (n is 'k' or '8' or '9')
        {
            throw new ToolException(Lookaround);
        }

        AppendRune(output, ReadRuneEscape());
        haveAtom = true;
        lastRepeat = false;
    }

    private int ReadRuneEscape()
    {
        if (index + 1 >= pattern.Length)
        {
            throw new ToolException(Invalid);
        }

        var c = pattern[index + 1];
        index += 2;
        switch (c)
        {
            case 'a':
                return '\a';
            case 'f':
                return '\f';
            case 'n':
                return '\n';
            case 'r':
                return '\r';
            case 't':
                return '\t';
            case 'v':
                return '\v';
            case 'x':
                return ReadHex();
            case '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7':
                return ReadOctal(c);
            case '8' or '9' or 'k':
                throw new ToolException(Lookaround);
            default:
                if (c < 128 && !IsAsciiAlnum(c))
                {
                    return c;
                }

                throw new ToolException(Invalid);
        }
    }

    private int ReadOctal(char first)
    {
        if (first != '0' && (index >= pattern.Length || pattern[index] is < '0' or > '7'))
        {
            throw new ToolException(Lookaround);
        }

        var value = first - '0';
        for (var n = 1; n < 3; n++)
        {
            if (index >= pattern.Length || pattern[index] is < '0' or > '7')
            {
                break;
            }

            value = value * 8 + (pattern[index] - '0');
            index++;
        }

        return value;
    }

    private int ReadHex()
    {
        if (index >= pattern.Length)
        {
            throw new ToolException(Invalid);
        }

        if (pattern[index] == '{')
        {
            index++;
            var value = 0;
            var digits = 0;
            while (index < pattern.Length && pattern[index] != '}')
            {
                var hex = Unhex(pattern[index]);
                if (hex < 0)
                {
                    throw new ToolException(Invalid);
                }

                value = value * 16 + hex;
                if (value > 0x10FFFF)
                {
                    throw new ToolException(Invalid);
                }

                digits++;
                index++;
            }

            if (digits == 0 || index >= pattern.Length || pattern[index] != '}')
            {
                throw new ToolException(Invalid);
            }

            index++;
            return value;
        }

        if (index + 1 >= pattern.Length)
        {
            throw new ToolException(Invalid);
        }

        var hi = Unhex(pattern[index]);
        var lo = Unhex(pattern[index + 1]);
        if (hi < 0 || lo < 0)
        {
            throw new ToolException(Invalid);
        }

        index += 2;
        return hi * 16 + lo;
    }

    private void SyncFlags()
    {
        if (current.IgnoreCase == emitted.IgnoreCase &&
            current.Multiline == emitted.Multiline &&
            current.DotAll == emitted.DotAll)
        {
            return;
        }

        var on = new StringBuilder();
        var off = new StringBuilder();
        if (current.IgnoreCase)
        {
            on.Append('i');
        }
        else
        {
            off.Append('i');
        }

        if (current.Multiline)
        {
            on.Append('m');
        }
        else
        {
            off.Append('m');
        }

        if (current.DotAll)
        {
            on.Append('s');
        }
        else
        {
            off.Append('s');
        }

        output.Append("(?");
        output.Append(on);
        if (off.Length > 0)
        {
            output.Append('-');
            output.Append(off);
        }

        output.Append(')');
        emitted = current with { Ungreedy = false };
    }

    private static void AppendRune(StringBuilder destination, int rune)
    {
        if (rune is < 0 or > 0x10FFFF)
        {
            throw new ToolException(Invalid);
        }

        if (rune > 0xFFFF)
        {
            var pair = char.ConvertFromUtf32(rune);
            destination.Append("\\u");
            destination.Append(((int)pair[0]).ToString("X4", CultureInfo.InvariantCulture));
            destination.Append("\\u");
            destination.Append(((int)pair[1]).ToString("X4", CultureInfo.InvariantCulture));
            return;
        }

        destination.Append("\\u");
        destination.Append(rune.ToString("X4", CultureInfo.InvariantCulture));
    }

    private void AppendQuoted(char c)
    {
        if (c is '.' or '+' or '*' or '?' or '(' or ')' or '|' or '[' or ']' or '{' or '}' or '^' or '$' or '\\')
        {
            output.Append('\\');
        }

        output.Append(c);
    }

    private static string PositiveClass(char name)
    {
        return name switch
        {
            'd' => "0-9",
            'w' => "0-9A-Za-z_",
            's' => "\\t\\n\\f\\r ",
            _ => throw new ToolException(Invalid),
        };
    }

    private static bool IsCaptureName(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!IsAsciiAlnum(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiAlnum(char c)
    {
        return c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }

    private static int Unhex(char c)
    {
        if (c is >= '0' and <= '9')
        {
            return c - '0';
        }

        if (c is >= 'a' and <= 'f')
        {
            return c - 'a' + 10;
        }

        if (c is >= 'A' and <= 'F')
        {
            return c - 'A' + 10;
        }

        return -1;
    }

    private enum RepeatKind
    {
        Literal,
        Bad,
        Ok,
    }

    private readonly record struct Flags(bool IgnoreCase, bool Multiline, bool DotAll, bool Ungreedy)
    {
        public Flags With(char flag, bool on)
        {
            return flag switch
            {
                'i' => this with { IgnoreCase = on },
                'm' => this with { Multiline = on },
                's' => this with { DotAll = on },
                'U' => this with { Ungreedy = on },
                _ => this,
            };
        }
    }
}

internal sealed class Compiled
{
    private readonly Regex regex;
    private readonly string? sourceName;

    public Compiled(Regex regex, IReadOnlyList<string> keys, string? sourceName)
    {
        this.regex = regex;
        this.sourceName = sourceName;
        Keys = keys;
        KeySet = new HashSet<string>(keys, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> Keys { get; }

    public HashSet<string> KeySet { get; }

    public IReadOnlyList<Hit> Find(DecodedText decoded)
    {
        MatchCollection matches;
        try
        {
            matches = regex.Matches(decoded.Text);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new ToolException(Text.Current.MatchTimeout);
        }

        var text = decoded.Text;
        var hits = new List<Hit>();
        foreach (Match match in matches)
        {
            if (!match.Success || (match.Length == 0 && match.Index == text.Length))
            {
                continue;
            }

            int sourceIndex;
            int sourceLength;
            if (sourceName is null)
            {
                sourceIndex = match.Index;
                sourceLength = match.Length;
            }
            else
            {
                var group = match.Groups[sourceName];
                if (!group.Success || group.Index < match.Index || group.Index + group.Length > match.Index + match.Length)
                {
                    sourceIndex = match.Index + match.Length;
                    sourceLength = 0;
                }
                else
                {
                    sourceIndex = group.Index;
                    sourceLength = group.Length;
                }
            }

            var key = new string[Keys.Count];
            for (var i = 0; i < Keys.Count; i++)
            {
                var group = match.Groups[Keys[i]];
                key[i] = group.Success ? group.Value : "";
            }

            var afterStart = sourceIndex + sourceLength;
            hits.Add(new Hit
            {
                CharIndex = sourceIndex,
                CharLength = sourceLength,
                Before = text.Substring(match.Index, sourceIndex - match.Index),
                Source = text.Substring(sourceIndex, sourceLength),
                After = text.Substring(afterStart, match.Index + match.Length - afterStart),
                Key = key,
                ByteStart = decoded.ByteIndex(sourceIndex),
                ByteLength = decoded.ByteLength(sourceIndex, sourceLength),
            });
        }

        return hits;
    }
}
