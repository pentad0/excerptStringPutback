using System.Text;

namespace ExcerptStringPutback;

public sealed record ExcerptRecord(int Id, int Start, int Length, string Raw, string Edited);

public sealed class ExcerptDocument
{
    public const string Header = "id\tstart\tlength\traw\tedited";

    private ExcerptDocument(string? pattern, IReadOnlyList<ExcerptRecord> records)
    {
        Pattern = pattern;
        Records = records;
    }

    public string? Pattern { get; }

    public IReadOnlyList<ExcerptRecord> Records { get; }

    public static ExcerptDocument Create(string pattern, string text)
    {
        return new ExcerptDocument(pattern, RegexExcerpt.Collect(text, pattern));
    }

    public static ExcerptDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        string? pattern = null;
        var seenHeader = false;
        var records = new List<ExcerptRecord>();
        var lineNumber = 0;
        foreach (var rawLine in content.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('#'))
            {
                const string prefix = "# pattern: ";
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    if (pattern is not null)
                    {
                        throw new ToolException("pattern 行が重複しています。");
                    }

                    pattern = line[prefix.Length..];
                }

                continue;
            }

            if (!seenHeader)
            {
                if (line != Header)
                {
                    throw new ToolException("抜粋ファイルの見出しは id、start、length、raw、edited である必要があります。");
                }

                seenHeader = true;
                continue;
            }

            var fields = SplitFields(line, lineNumber);
            if (fields.Count != 5)
            {
                throw new ToolException($"抜粋ファイルの {lineNumber} 行目は 5 列である必要があります。");
            }

            var record = new ExcerptRecord(
                ParseInt(fields[0], lineNumber, "id"),
                ParseInt(fields[1], lineNumber, "start"),
                ParseInt(fields[2], lineNumber, "length"),
                fields[3],
                fields[4]);
            if (record.Id <= 0)
            {
                throw new ToolException($"抜粋ファイルの {lineNumber} 行目の id が不正です。");
            }

            if (record.Start < 0 || record.Length < 1 || record.Length != record.Raw.Length)
            {
                throw new ToolException($"抜粋ファイルの {lineNumber} 行目の範囲が不正です。");
            }

            records.Add(record);
        }

        if (!seenHeader)
        {
            throw new ToolException("抜粋ファイルに見出し行がありません。");
        }

        for (var index = 0; index < records.Count; index++)
        {
            if (records[index].Id != index + 1)
            {
                throw new ToolException($"id は 1 から順である必要があります。実際の値: {records[index].Id}");
            }
        }

        return new ExcerptDocument(pattern, records);
    }

    public string ToTsv(string pattern)
    {
        var builder = new StringBuilder();
        if (!pattern.Contains('\n') && !pattern.Contains('\r'))
        {
            builder.Append("# pattern: ").Append(pattern).Append('\n');
        }

        builder.Append(Header).Append('\n');
        foreach (var record in Records)
        {
            builder
                .Append(record.Id).Append('\t')
                .Append(record.Start).Append('\t')
                .Append(record.Length).Append('\t')
                .Append(FormatField(record.Raw)).Append('\t')
                .Append(FormatField(record.Edited)).Append('\n');
        }

        return builder.ToString();
    }

    private static int ParseInt(string text, int lineNumber, string name)
    {
        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            throw new ToolException($"抜粋ファイルの {lineNumber} 行目の {name} は整数である必要があります。");
        }

        return value;
    }

    private static string FormatField(string value)
    {
        if (!value.Contains('"') && !value.Contains('\t') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static List<string> SplitFields(string line, int lineNumber)
    {
        var fields = new List<string>();
        var index = 0;
        while (true)
        {
            if (index >= line.Length)
            {
                fields.Add("");
                break;
            }

            if (line[index] == '"')
            {
                var value = new StringBuilder();
                index++;
                var closed = false;
                while (index < line.Length)
                {
                    if (line[index] == '"')
                    {
                        if (index + 1 < line.Length && line[index + 1] == '"')
                        {
                            value.Append('"');
                            index += 2;
                            continue;
                        }

                        index++;
                        closed = true;
                        break;
                    }

                    value.Append(line[index]);
                    index++;
                }

                if (!closed)
                {
                    throw new ToolException($"抜粋ファイルの {lineNumber} 行目の引用符が閉じられていません。");
                }

                fields.Add(value.ToString());
                if (index >= line.Length)
                {
                    break;
                }

                if (line[index] != '\t')
                {
                    throw new ToolException($"抜粋ファイルの {lineNumber} 行目の列区切りが不正です。");
                }

                index++;
                continue;
            }

            var next = line.IndexOf('\t', index);
            if (next < 0)
            {
                fields.Add(line[index..]);
                break;
            }

            fields.Add(line[index..next]);
            index = next + 1;
        }

        return fields;
    }
}
