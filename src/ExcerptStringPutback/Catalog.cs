using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ExcerptStringPutback;

internal sealed class Entry
{
    public List<string> Key { get; set; } = new();

    public List<string> Before { get; set; } = new();

    public List<string> Source { get; set; } = new();

    public List<string> After { get; set; } = new();

    public List<string> PreviousSource { get; set; } = new();

    public List<List<string>> Replacements { get; set; } = new();

    public List<int> ByteStart { get; set; } = new();

    public List<int> ByteLength { get; set; } = new();

    public string Status { get; set; } = "";

    public string MatchedBy { get; set; } = "";
}

internal sealed class Catalog
{
    public string Program { get; set; } = "";

    public int FormatVersion { get; set; } = ToolInfo.FormatVersion;

    public string ToolVersion { get; set; } = "";

    public string FileVersion { get; set; } = "";

    public NoteValue Note { get; set; } = new();

    public string File { get; set; } = "";

    public string Encoding { get; set; } = "";

    public bool Bom { get; set; }

    public string Pattern { get; set; } = "";

    public List<string> Keys { get; set; } = new();

    public List<string> SourceNames { get; set; } = new();

    public List<string> ReplacementNames { get; set; } = new();

    public int Used { get; set; }

    public List<string> Methods { get; set; } = new();

    public List<Entry> Entries { get; set; } = new();
}

internal sealed class NoteValue
{
    public string Json { get; set; } = "\"\"";
}

internal static class ToolInfo
{
    public const string ProgramName = "excerptStringPutback";

    public const string Version = "1.1.0";

    public const int FormatVersion = 2;

    public static readonly string[] Methods = ["key", "offset", "exact", "context", "align", "revised"];

    public static List<string> Order(IEnumerable<string> names)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!Methods.Contains(name, StringComparer.Ordinal))
            {
                throw new ToolException(Text.Current.Format(Text.Current.UnknownMethod, name));
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
                throw new ToolException(Text.Current.Format(Text.Current.UnknownMethod, name));
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
            throw new ToolException(Text.Current.Format(Text.Current.FileNotFound, path));
        }
        catch (DirectoryNotFoundException)
        {
            throw new ToolException(Text.Current.Format(Text.Current.FileNotFound, path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ToolException(Text.Current.Format(Text.Current.CannotRead, path));
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            throw new ToolException(Text.Current.JsonBom);
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new ToolException(Text.Current.JsonUtf8);
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
            throw new ToolException(Text.Current.JsonShape);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ToolException(Text.Current.JsonShape);
            }

            var root = document.RootElement;
            if (!root.TryGetProperty("formatVersion", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out var version) ||
                version != ToolInfo.FormatVersion)
            {
                throw new ToolException(Text.Current.FormatVersion);
            }

            var catalog = new Catalog
            {
                Program = OptionalString(root, "program"),
                FormatVersion = version,
                ToolVersion = OptionalString(root, "toolVersion"),
                FileVersion = OptionalString(root, "fileVersion"),
                Note = OptionalNote(root, "note"),
                File = OptionalString(root, "file"),
                Encoding = OptionalString(root, "encoding"),
                Bom = OptionalBool(root, "bom"),
                Pattern = OptionalString(root, "pattern"),
                Keys = OptionalStrings(root, "keys"),
                SourceNames = OptionalStrings(root, "sourceNames"),
                ReplacementNames = OptionalStrings(root, "replacementNames"),
                Used = OptionalInt(root, "used"),
                Methods = root.TryGetProperty("methods", out var methods)
                    ? ToolInfo.Order(RequiredStrings(methods, "methods"))
                    : ToolInfo.Methods.ToList(),
                Entries = ReadEntries(root),
            };
            if (catalog.Used < 0)
            {
                throw new ToolException(Text.Current.UsedInvalid);
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
        NoteProp(builder, 1, "note", catalog.Note);
        StringProp(builder, 1, "file", catalog.File);
        StringProp(builder, 1, "encoding", catalog.Encoding);
        BoolProp(builder, 1, "bom", catalog.Bom);
        StringProp(builder, 1, "pattern", catalog.Pattern);
        StringsProp(builder, 1, "keys", catalog.Keys);
        StringsProp(builder, 1, "sourceNames", catalog.SourceNames);
        StringsProp(builder, 1, "replacementNames", catalog.ReplacementNames);
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
        StringsProp(builder, 3, "before", entry.Before);
        StringsProp(builder, 3, "source", entry.Source);
        StringsProp(builder, 3, "after", entry.After);
        StringsProp(builder, 3, "previousSource", entry.PreviousSource);
        RowsProp(builder, 3, "replacements", entry.Replacements);
        IntsProp(builder, 3, "byteStart", entry.ByteStart);
        IntsProp(builder, 3, "byteLength", entry.ByteLength);
        StringProp(builder, 3, "status", entry.Status);
        StringProp(builder, 3, "matchedBy", entry.MatchedBy, last: true);
        builder.Append("    }");
    }

    private static List<Entry> ReadEntries(JsonElement root)
    {
        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            throw new ToolException(Text.Current.EntriesArray);
        }

        var list = new List<Entry>();
        foreach (var item in entries.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new ToolException(Text.Current.JsonShape);
            }

            list.Add(new Entry
            {
                Key = OptionalStrings(item, "key"),
                Before = OptionalStrings(item, "before"),
                Source = OptionalStrings(item, "source"),
                After = OptionalStrings(item, "after"),
                PreviousSource = OptionalStrings(item, "previousSource"),
                Replacements = OptionalRows(item, "replacements"),
                ByteStart = OptionalInts(item, "byteStart", -1),
                ByteLength = OptionalInts(item, "byteLength", 0),
                Status = OptionalString(item, "status"),
                MatchedBy = OptionalString(item, "matchedBy"),
            });
        }

        return list;
    }

    private static NoteValue OptionalNote(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            return new NoteValue();
        }

        return new NoteValue { Json = element.GetRawText() };
    }

    private static string OptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return "";
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new ToolException(Text.Current.JsonShape);
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
            throw new ToolException(Text.Current.JsonShape);
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
            throw new ToolException(Text.Current.JsonShape);
        }

        return value;
    }

    private static List<List<string>> OptionalRows(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return new List<List<string>>();
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ToolException(Text.Current.Format(Text.Current.MustBeArray, name));
        }

        var rows = new List<List<string>>();
        foreach (var item in element.EnumerateArray())
        {
            rows.Add(RequiredStrings(item, name));
        }

        return rows;
    }

    private static List<int> OptionalInts(JsonElement root, string name, int minimum)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return new List<int>();
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ToolException(Text.Current.Format(Text.Current.MustBeArray, name));
        }

        var list = new List<int>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var value) || value < minimum)
            {
                throw new ToolException(Text.Current.JsonShape);
            }

            list.Add(value);
        }

        return list;
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
            throw new ToolException(Text.Current.Format(Text.Current.MustBeArray, name));
        }

        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new ToolException(Text.Current.Format(Text.Current.MustBeStringArray, name));
            }

            list.Add(item.GetString() ?? "");
        }

        return list;
    }

    private static void NoteProp(StringBuilder builder, int indent, string name, NoteValue note, bool last = false)
    {
        Prefix(builder, indent, name);
        using var document = JsonDocument.Parse(note.Json);
        WriteValue(builder, document.RootElement, indent);
        End(builder, last);
    }

    private static void WriteValue(StringBuilder builder, JsonElement element, int indent)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                WriteString(builder, element.GetString() ?? "");
                break;
            case JsonValueKind.Array:
                WriteArray(builder, element, indent);
                break;
            case JsonValueKind.Object:
                WriteObject(builder, element, indent);
                break;
            default:
                builder.Append(element.GetRawText());
                break;
        }
    }

    private static void WriteArray(StringBuilder builder, JsonElement element, int indent)
    {
        var items = new List<JsonElement>();
        foreach (var item in element.EnumerateArray())
        {
            items.Add(item);
        }

        if (items.Count == 0)
        {
            builder.Append("[]");
            return;
        }

        builder.Append("[\n");
        var child = indent + 1;
        for (var i = 0; i < items.Count; i++)
        {
            builder.Append(' ', child * 2);
            WriteValue(builder, items[i], child);
            if (i + 1 < items.Count)
            {
                builder.Append(',');
            }

            builder.Append('\n');
        }

        builder.Append(' ', indent * 2);
        builder.Append(']');
    }

    private static void WriteObject(StringBuilder builder, JsonElement element, int indent)
    {
        var properties = new List<JsonProperty>();
        foreach (var property in element.EnumerateObject())
        {
            properties.Add(property);
        }

        if (properties.Count == 0)
        {
            builder.Append("{}");
            return;
        }

        builder.Append("{\n");
        var child = indent + 1;
        for (var i = 0; i < properties.Count; i++)
        {
            builder.Append(' ', child * 2);
            WriteString(builder, properties[i].Name);
            builder.Append(": ");
            WriteValue(builder, properties[i].Value, child);
            if (i + 1 < properties.Count)
            {
                builder.Append(',');
            }

            builder.Append('\n');
        }

        builder.Append(' ', indent * 2);
        builder.Append('}');
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
        WriteStrings(builder, indent, values);
        End(builder, last);
    }

    private static void RowsProp(StringBuilder builder, int indent, string name, IReadOnlyList<List<string>> rows, bool last = false)
    {
        Prefix(builder, indent, name);
        if (rows.Count == 0)
        {
            builder.Append("[]");
            End(builder, last);
            return;
        }

        builder.Append("[\n");
        var child = indent + 1;
        for (var i = 0; i < rows.Count; i++)
        {
            builder.Append(' ', child * 2);
            WriteStrings(builder, child, rows[i]);
            if (i + 1 < rows.Count)
            {
                builder.Append(',');
            }

            builder.Append('\n');
        }

        builder.Append(' ', indent * 2);
        builder.Append(']');
        End(builder, last);
    }

    private static void IntsProp(StringBuilder builder, int indent, string name, IReadOnlyList<int> values, bool last = false)
    {
        Prefix(builder, indent, name);
        if (values.Count == 0)
        {
            builder.Append("[]");
            End(builder, last);
            return;
        }

        builder.Append("[\n");
        var child = indent + 1;
        for (var i = 0; i < values.Count; i++)
        {
            builder.Append(' ', child * 2);
            builder.Append(values[i].ToString(CultureInfo.InvariantCulture));
            if (i + 1 < values.Count)
            {
                builder.Append(',');
            }

            builder.Append('\n');
        }

        builder.Append(' ', indent * 2);
        builder.Append(']');
        End(builder, last);
    }

    private static void WriteStrings(StringBuilder builder, int indent, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            builder.Append("[]");
            return;
        }

        builder.Append("[\n");
        var child = indent + 1;
        for (var i = 0; i < values.Count; i++)
        {
            builder.Append(' ', child * 2);
            WriteString(builder, values[i]);
            if (i + 1 < values.Count)
            {
                builder.Append(',');
            }

            builder.Append('\n');
        }

        builder.Append(' ', indent * 2);
        builder.Append(']');
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
