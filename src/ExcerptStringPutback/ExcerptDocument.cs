using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ExcerptStringPutback;

public sealed record ExcerptRecord(int Id, int Start, int Length, string Raw, string Edited);

public sealed class ExcerptDocument
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private ExcerptDocument(string? source, IReadOnlyList<ExcerptRecord> records)
    {
        Source = source;
        Records = records;
    }

    public string? Source { get; }

    public IReadOnlyList<ExcerptRecord> Records { get; }

    public static ExcerptDocument Create(string source, string text)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(text);
        var records = new List<ExcerptRecord>();
        foreach (var span in QuotedStringScanner.Scan(text))
        {
            var raw = text.Substring(span.Start, span.Length);
            records.Add(new ExcerptRecord(records.Count + 1, span.Start, span.Length, raw, raw));
        }

        return new ExcerptDocument(source, records);
    }

    public static ExcerptDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        string? source = null;
        var seenMeta = false;
        var seenRecord = false;
        var records = new List<ExcerptRecord>();
        var lineNumber = 0;
        foreach (var line in content.Split('\n'))
        {
            lineNumber++;
            var trimmed = line.EndsWith('\r') ? line[..^1].Trim() : line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(trimmed);
            }
            catch (JsonException)
            {
                throw new ToolException($"抜粋ファイルの {lineNumber} 行目が JSON ではありません。");
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new ToolException($"抜粋ファイルの {lineNumber} 行目は JSON オブジェクトである必要があります。");
                }

                if (root.TryGetProperty("kind", out var kind))
                {
                    if (kind.ValueKind != JsonValueKind.String || kind.GetString() != "meta")
                    {
                        throw new ToolException($"抜粋ファイルの {lineNumber} 行目の kind が不正です。");
                    }

                    if (seenMeta)
                    {
                        throw new ToolException("meta 行が重複しています。");
                    }

                    if (seenRecord)
                    {
                        throw new ToolException("meta 行は抜粋ファイルの先頭である必要があります。");
                    }

                    if (root.TryGetProperty("tool", out var tool)
                        && (tool.ValueKind != JsonValueKind.String || tool.GetString() != "excerptStringPutback"))
                    {
                        throw new ToolException("meta の tool が excerptStringPutback ではありません。");
                    }

                    if (root.TryGetProperty("encoding", out var encoding)
                        && (encoding.ValueKind != JsonValueKind.String
                            || !string.Equals(encoding.GetString(), "utf-8", StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new ToolException("meta の encoding は utf-8 である必要があります。");
                    }

                    if (root.TryGetProperty("source", out var sourceProperty))
                    {
                        if (sourceProperty.ValueKind != JsonValueKind.String)
                        {
                            throw new ToolException("meta の source は文字列である必要があります。");
                        }

                        source = sourceProperty.GetString();
                    }

                    seenMeta = true;
                    continue;
                }

                var record = ReadRecord(root, lineNumber);
                if (record.Id <= 0)
                {
                    throw new ToolException($"抜粋ファイルの {lineNumber} 行目の id が不正です。");
                }

                if (record.Start < 0 || record.Length < 1 || record.Length != record.Raw.Length)
                {
                    throw new ToolException($"抜粋ファイルの {lineNumber} 行目の範囲が不正です。");
                }

                records.Add(record);
                seenRecord = true;
            }
        }

        for (var index = 0; index < records.Count; index++)
        {
            if (records[index].Id != index + 1)
            {
                throw new ToolException($"id は 1 から順である必要があります。実際の値: {records[index].Id}");
            }
        }

        return new ExcerptDocument(source, records);
    }

    public string ToJsonLines()
    {
        var builder = new StringBuilder();
        builder.Append(JsonSerializer.Serialize(new MetaLine("meta", "excerptStringPutback", "utf-8", Source ?? ""), JsonOptions));
        builder.Append('\n');
        foreach (var record in Records)
        {
            builder.Append(JsonSerializer.Serialize(record, JsonOptions));
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static ExcerptRecord ReadRecord(JsonElement root, int lineNumber)
    {
        return new ExcerptRecord(
            ReadInt(root, "id", lineNumber),
            ReadInt(root, "start", lineNumber),
            ReadInt(root, "length", lineNumber),
            ReadString(root, "raw", lineNumber),
            ReadString(root, "edited", lineNumber));
    }

    private static int ReadInt(JsonElement root, string name, int lineNumber)
    {
        if (!root.TryGetProperty(name, out var property))
        {
            throw new ToolException($"抜粋ファイルの {lineNumber} 行目に {name} がありません。");
        }

        if (!property.TryGetInt32(out var value))
        {
            throw new ToolException($"抜粋ファイルの {lineNumber} 行目の {name} は整数である必要があります。");
        }

        return value;
    }

    private static string ReadString(JsonElement root, string name, int lineNumber)
    {
        if (!root.TryGetProperty(name, out var property))
        {
            throw new ToolException($"抜粋ファイルの {lineNumber} 行目に {name} がありません。");
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new ToolException($"抜粋ファイルの {lineNumber} 行目の {name} は文字列である必要があります。");
        }

        return property.GetString() ?? "";
    }

    private sealed record MetaLine(string Kind, string Tool, string Encoding, string Source);
}
