using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ExcerptStringPutback;

internal sealed class Entry
{
    public List<string> Key { get; set; } = new();

    public string Before { get; set; } = "";

    public string Source { get; set; } = "";

    public string After { get; set; } = "";

    public string PreviousSource { get; set; } = "";

    public List<string> Target { get; set; } = new();

    public int ByteStart { get; set; }

    public int ByteLength { get; set; }

    public string Status { get; set; } = "";

    public string MatchedBy { get; set; } = "";
}

internal sealed class Catalog
{
    public string Program { get; set; } = "";

    public int FormatVersion { get; set; } = ToolInfo.FormatVersion;

    public string ToolVersion { get; set; } = "";

    public string FileVersion { get; set; } = "";

    public string File { get; set; } = "";

    public string Encoding { get; set; } = "";

    public bool Bom { get; set; }

    public string Pattern { get; set; } = "";

    public List<string> Keys { get; set; } = new();

    public List<string> Names { get; set; } = new();

    public int Used { get; set; }

    public List<string> Methods { get; set; } = new();

    public List<Entry> Entries { get; set; } = new();
}

internal static class ToolInfo
{
    public const string ProgramName = "excerptStringPutback";

    public const string Version = "1.0.0";

    public const int FormatVersion = 1;

    public static readonly string[] Methods = ["key", "offset", "exact", "context", "align", "revised"];

    public static List<string> Order(IEnumerable<string> names)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!Methods.Contains(name, StringComparer.Ordinal))
            {
                throw new ToolException($"未知の方式名です: {name}");
            }

            set.Add(name);
        }

        return Methods.Where(set.Contains).ToList();
    }

    public static HashSet<string> ParseDisable(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in text.Split(','))
        {
            var name = part.Trim();
            if (name.Length == 0 || !Methods.Contains(name, StringComparer.Ordinal))
            {
                throw new ToolException($"未知の方式名です: {name}");
            }

            set.Add(name);
        }

        return set;
    }
}

internal static class CatalogJson
{
    public static Catalog Read(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            throw new ToolException($"ファイルが見つかりません: {path}");
        }
        catch (DirectoryNotFoundException)
        {
            throw new ToolException($"ファイルが見つかりません: {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ToolException($"ファイルを読めません: {path}");
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            throw new ToolException("JSON は BOM なしの UTF-8 です。");
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new ToolException("JSON を UTF-8 として読めません。");
        }

        return Parse(text);
    }

    public static Catalog Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new ToolException("JSON の形式が違います。");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ToolException("JSON の形式が違います。");
            }

            var root = document.RootElement;
            if (!root.TryGetProperty("formatVersion", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out var version) ||
                version != ToolInfo.FormatVersion)
            {
                throw new ToolException("formatVersion が 1 ではありません。");
            }

            var catalog = new Catalog
            {
                Program = OptionalString(root, "program"),
                FormatVersion = version,
                ToolVersion = OptionalString(root, "toolVersion"),
                FileVersion = OptionalString(root, "fileVersion"),
                File = OptionalString(root, "file"),
                Encoding = OptionalString(root, "encoding"),
                Bom = OptionalBool(root, "bom"),
                Pattern = OptionalString(root, "pattern"),
                Keys = OptionalStrings(root, "keys"),
                Names = OptionalStrings(root, "names"),
                Used = OptionalInt(root, "used"),
                Methods = root.TryGetProperty("methods", out var methods)
                    ? ToolInfo.Order(RequiredStrings(methods, "methods"))
                    : ToolInfo.Methods.ToList(),
                Entries = ReadEntries(root),
            };
            if (catalog.Used < 0)
            {
                throw new ToolException("used が不正です。");
            }

            return catalog;
        }
    }

    public static void Write(string path, Catalog catalog)
    {
        TextCodec.WriteBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(ToJson(catalog)));
    }

    public static string ToJson(Catalog catalog)
    {
        var builder = new StringBuilder();
        builder.Append("{\n");
        StringProp(builder, 1, "program", catalog.Program);
        IntProp(builder, 1, "formatVersion", catalog.FormatVersion);
        StringProp(builder, 1, "toolVersion", catalog.ToolVersion);
        StringProp(builder, 1, "fileVersion", catalog.FileVersion);
        StringProp(builder, 1, "file", catalog.File);
        StringProp(builder, 1, "encoding", catalog.Encoding);
        BoolProp(builder, 1, "bom", catalog.Bom);
        StringProp(builder, 1, "pattern", catalog.Pattern);
        StringsProp(builder, 1, "keys", catalog.Keys);
        StringsProp(builder, 1, "names", catalog.Names);
        IntProp(builder, 1, "used", catalog.Used);
        StringsProp(builder, 1, "methods", catalog.Methods);
        builder.Append("  \"entries\": ");
        if (catalog.Entries.Count == 0)
        {
            builder.Append("[]\n");
        }
        else
        {
            builder.Append("[\n");
            for (var i = 0; i < catalog.Entries.Count; i++)
            {
                WriteEntry(builder, catalog.Entries[i]);
                if (i + 1 < catalog.Entries.Count)
                {
                    builder.Append(',');
                }

                builder.Append('\n');
            }

            builder.Append("  ]\n");
        }

        builder.Append("}\n");
        return builder.ToString();
    }

    private static void WriteEntry(StringBuilder builder, Entry entry)
    {
        builder.Append("    {\n");
        StringsProp(builder, 3, "key", entry.Key);
        StringProp(builder, 3, "before", entry.Before);
        StringProp(builder, 3, "source", entry.Source);
        StringProp(builder, 3, "after", entry.After);
        StringProp(builder, 3, "previousSource", entry.PreviousSource);
        StringsProp(builder, 3, "target", entry.Target);
        IntProp(builder, 3, "byteStart", entry.ByteStart);
        IntProp(builder, 3, "byteLength", entry.ByteLength);
        StringProp(builder, 3, "status", entry.Status);
        StringProp(builder, 3, "matchedBy", entry.MatchedBy, last: true);
        builder.Append("    }");
    }

    private static List<Entry> ReadEntries(JsonElement root)
    {
        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            throw new ToolException("entries は配列です。");
        }

        var list = new List<Entry>();
        foreach (var item in entries.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new ToolException("JSON の形式が違います。");
            }

            list.Add(new Entry
            {
                Key = OptionalStrings(item, "key"),
                Before = OptionalString(item, "before"),
                Source = OptionalString(item, "source"),
                After = OptionalString(item, "after"),
                PreviousSource = OptionalString(item, "previousSource"),
                Target = OptionalStrings(item, "target"),
                ByteStart = OptionalInt(item, "byteStart"),
                ByteLength = OptionalInt(item, "byteLength"),
                Status = OptionalString(item, "status"),
                MatchedBy = OptionalString(item, "matchedBy"),
            });
        }

        return list;
    }

    private static string OptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return "";
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new ToolException("JSON の形式が違います。");
        }

        return element.GetString() ?? "";
    }

    private static bool OptionalBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        if (element.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new ToolException("JSON の形式が違います。");
        }

        return element.GetBoolean();
    }

    private static int OptionalInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return 0;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value) || value < 0)
        {
            throw new ToolException("JSON の形式が違います。");
        }

        return value;
    }

    private static List<string> OptionalStrings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return new List<string>();
        }

        return RequiredStrings(element, name);
    }

    private static List<string> RequiredStrings(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ToolException($"{name} は配列です。");
        }

        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new ToolException($"{name} は文字列の配列です。");
            }

            list.Add(item.GetString() ?? "");
        }

        return list;
    }

    private static void StringProp(StringBuilder builder, int indent, string name, string value, bool last = false)
    {
        Prefix(builder, indent, name);
        WriteString(builder, value);
        End(builder, last);
    }

    private static void IntProp(StringBuilder builder, int indent, string name, int value, bool last = false)
    {
        Prefix(builder, indent, name);
        builder.Append(value.ToString(CultureInfo.InvariantCulture));
        End(builder, last);
    }

    private static void BoolProp(StringBuilder builder, int indent, string name, bool value, bool last = false)
    {
        Prefix(builder, indent, name);
        builder.Append(value ? "true" : "false");
        End(builder, last);
    }

    private static void StringsProp(StringBuilder builder, int indent, string name, IReadOnlyList<string> values, bool last = false)
    {
        Prefix(builder, indent, name);
        builder.Append('[');
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            WriteString(builder, values[i]);
        }

        builder.Append(']');
        End(builder, last);
    }

    private static void Prefix(StringBuilder builder, int indent, string name)
    {
        builder.Append(' ', indent * 2);
        builder.Append('"');
        builder.Append(name);
        builder.Append("\": ");
    }

    private static void End(StringBuilder builder, bool last)
    {
        if (!last)
        {
            builder.Append(',');
        }

        builder.Append('\n');
    }

    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < 0x20)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
