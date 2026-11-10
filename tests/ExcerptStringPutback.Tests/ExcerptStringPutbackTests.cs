using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ExcerptStringPutback.Tests;

public class ExcerptStringPutbackTests
{
    [Fact]
    public void Extract_splits_named_keys_and_the_first_unnamed_capture()
    {
        using var dir = Temp();
        var text = Path.Combine(dir, "script.txt");
        File.WriteAllText(text, "1\t15\tHello\n");
        var json = Path.Combine(dir, "out.json");
        var (code, stdout, stderr) = Run(
            "extract", "-encoding", "utf-8",
            "-pattern", "(?m)^(?P<chapter>\\d+)\\t(?P<id>\\d+)\\t(.*)",
            "-o", json, text);

        Assert.Equal(0, code);
        Assert.Equal("", stdout);
        Assert.Contains("1 件", stderr);
        var catalog = CatalogJson.Parse(File.ReadAllText(json));
        Assert.Equal(new[] { "chapter", "id" }, catalog.Keys);
        var entry = Assert.Single(catalog.Entries);
        Assert.Equal(new[] { "1", "15" }, entry.Key);
        Assert.Equal("1\t15\t", entry.Before);
        Assert.Equal("Hello", entry.Source);
        Assert.Equal("", entry.After);
        Assert.Equal(new[] { "Hello" }, entry.Target);
        Assert.Equal(5, entry.ByteStart);
        Assert.Equal(5, entry.ByteLength);
        Assert.Equal("", entry.Status);
        Assert.Equal("", entry.MatchedBy);
        Assert.Equal("", entry.PreviousSource);
        Assert.Equal(new[] { "changed" }, catalog.Names);
        Assert.Equal(1, catalog.Used);
        Assert.Equal("", catalog.FileVersion);
        Assert.Equal("excerptStringPutback", catalog.Program);
        Assert.Equal("1.0.0", catalog.ToolVersion);
        Assert.Equal(1, catalog.FormatVersion);
        Assert.Equal(ToolInfo.Methods, catalog.Methods);
        var raw = File.ReadAllText(json);
        Assert.StartsWith("{\n  \"program\": \"excerptStringPutback\"", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u003c", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Crlf_line_keeps_the_carriage_return_in_the_source()
    {
        var catalog = Extract("MSG=Hi\r\n", "(?m)^(?P<id>[^=]+)=(.*)$");
        var entry = Assert.Single(catalog.Entries);
        Assert.Equal(new[] { "MSG" }, entry.Key);
        Assert.Equal("MSG=", entry.Before);
        Assert.Equal("Hi\r", entry.Source);
        Assert.Equal("", entry.After);
    }

    [Fact]
    public void Final_empty_match_is_dropped_and_a_middle_blank_line_is_kept()
    {
        Assert.Equal(new[] { "a" }, Extract("a\n", "(?m)^(.*)").Entries.Select(entry => entry.Source));
        Assert.Equal(new[] { "a", "" }, Extract("a\n\n", "(?m)^(.*)").Entries.Select(entry => entry.Source));
    }

    [Fact]
    public void Ungreedy_flag_swaps_quantifiers()
    {
        Assert.Equal(new[] { "a", "a", "a" }, Extract("aaa", "(?U)a+").Entries.Select(entry => entry.Source));
        Assert.Equal(new[] { "aaa" }, Extract("aaa", "(?U)a+?").Entries.Select(entry => entry.Source));
        Assert.Equal(new[] { "B" }, Extract("B", "(?i)a|b").Entries.Select(entry => entry.Source));
        Assert.Empty(Extract("a\n", "a$").Entries);
        Assert.Equal(new[] { "a" }, Extract("a", "a$").Entries.Select(entry => entry.Source));
    }

    [Fact]
    public void Illegal_syntax_and_duplicate_names_are_rejected()
    {
        var (code, stderr) = ExtractCode("abc", "(?=a)");
        Assert.Equal(1, code);
        Assert.Contains("先読み", stderr);
        Assert.Equal(1, ExtractCode("abc", "(?P<a>.)(?P<a>.)").Code);
        Assert.Contains("同じ名前", ExtractCode("abc", "(?P<a>.)(?P<a>.)").Stderr);
    }

    [Fact]
    public void Byte_positions_include_the_bom()
    {
        using var dir = Temp();
        var utf8 = Path.Combine(dir, "utf8.txt");
        File.WriteAllBytes(utf8, new byte[] { 0xEF, 0xBB, 0xBF, (byte)'A', (byte)'B' });
        var json = Path.Combine(dir, "utf8.json");
        Assert.Equal(0, Run("extract", "-encoding", "utf-16le", "-pattern", "(.*)", "-o", json, utf8).Code);
        var entry = Assert.Single(CatalogJson.Parse(File.ReadAllText(json)).Entries);
        Assert.Equal(3, entry.ByteStart);
        Assert.Equal(2, entry.ByteLength);
        Assert.Equal("utf-8", CatalogJson.Parse(File.ReadAllText(json)).Encoding);
        Assert.True(CatalogJson.Parse(File.ReadAllText(json)).Bom);
        Assert.Contains("BOM を優先", Run("extract", "-encoding", "utf-16le", "-pattern", "(.*)", "-o", json, utf8).Stderr);

        var utf16 = Path.Combine(dir, "utf16.txt");
        File.WriteAllBytes(utf16, new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x42, 0x00 });
        var json16 = Path.Combine(dir, "utf16.json");
        Assert.Equal(0, Run("extract", "-encoding", "utf-16le", "-pattern", "(.*)", "-o", json16, utf16).Code);
        var wide = Assert.Single(CatalogJson.Parse(File.ReadAllText(json16)).Entries);
        Assert.Equal("AB", wide.Source);
        Assert.Equal(2, wide.ByteStart);
        Assert.Equal(4, wide.ByteLength);
    }

    [Fact]
    public void Encoding_errors_cover_bom_rules()
    {
        using var dir = Temp();
        var bare = Path.Combine(dir, "bare.txt");
        File.WriteAllBytes(bare, "AB"u8.ToArray());
        var json = Path.Combine(dir, "out.json");
        Assert.Equal(1, Run("extract", "-pattern", "(.*)", "-o", json, bare).Code);
        Assert.Equal(1, Run("extract", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, Path.Combine(dir, "missing.txt")).Code);

        var bad = Path.Combine(dir, "bad.txt");
        File.WriteAllBytes(bad, new byte[] { 0xFF });
        Assert.Equal(1, Run("extract", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, bad).Code);

        var utf32 = Path.Combine(dir, "utf32.txt");
        File.WriteAllBytes(utf32, new byte[] { 0xFF, 0xFE, 0x00, 0x00 });
        Assert.Contains("UTF-32", Run("extract", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, utf32).Stderr);

        var odd = Path.Combine(dir, "odd.txt");
        File.WriteAllBytes(odd, new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x42 });
        Assert.Contains("奇数", Run("extract", "-encoding", "utf-16le", "-pattern", "(.*)", "-o", json, odd).Stderr);

        var lone = Path.Combine(dir, "lone.txt");
        File.WriteAllBytes(lone, new byte[] { 0xFF, 0xFE, 0x00, 0xD8 });
        Assert.Contains("サロゲート", Run("extract", "-encoding", "utf-16le", "-pattern", "(.*)", "-o", json, lone).Stderr);
    }

    [Fact]
    public void Json_puts_each_array_element_on_its_own_line()
    {
        var catalog = Extract(") ( subhead = ", "(.*)");
        var raw = CatalogJson.ToJson(catalog);
        Assert.Contains("  \"names\": [\n    \"changed\"\n  ],\n", raw, StringComparison.Ordinal);
        Assert.Contains("  \"methods\": [\n    \"key\",\n    \"offset\",\n    \"exact\",\n    \"context\",\n    \"align\",\n    \"revised\"\n  ],\n", raw, StringComparison.Ordinal);
        Assert.Contains("      \"key\": [],\n", raw, StringComparison.Ordinal);
        Assert.Contains("      \"target\": [\n        \") ( subhead = \"\n      ],\n", raw, StringComparison.Ordinal);
        var entry = Assert.Single(catalog.Entries);
        Assert.Equal("", entry.Status);
        Assert.Equal("", entry.MatchedBy);
    }

    [Fact]
    public void Angle_brackets_are_not_escaped_and_newlines_are()
    {
        var catalog = Extract("a<b&c\n", "(?s)(.*)");
        var raw = CatalogJson.ToJson(catalog);
        Assert.Contains("\"source\": \"a<b&c\\n\"", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u003c", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u0026", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Pattern_file_strips_bom_and_one_trailing_newline()
    {
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        File.WriteAllText(text, "AB");
        var pattern = Path.Combine(dir, "pat.txt");
        var bytes = new List<byte> { 0xEF, 0xBB, 0xBF };
        bytes.AddRange(Encoding.UTF8.GetBytes("(.*)\n"));
        File.WriteAllBytes(pattern, bytes.ToArray());
        var json = Path.Combine(dir, "out.json");
        Assert.Equal(0, Run("extract", "-encoding", "utf-8", "-pattern-file", pattern, "-o", json, text).Code);
        Assert.Equal("AB", Assert.Single(CatalogJson.Parse(File.ReadAllText(json)).Entries).Source);
    }

    [Fact]
    public void Write_replaces_only_the_source_capture()
    {
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        File.WriteAllBytes(text, "MSG=Hello\n"u8.ToArray());
        var original = File.ReadAllBytes(text);
        var json = Path.Combine(dir, "cat.json");
        Assert.Equal(0, Run("extract", "-encoding", "utf-8", "-pattern", "(?m)^(?P<id>[^=]+)=(.*)$", "-o", json, text).Code);
        var catalog = CatalogJson.Parse(File.ReadAllText(json));
        catalog.Entries[0].Target[0] = "こんにちは";
        catalog.Names.Add("常体");
        catalog.FileVersion = "3";
        catalog.Program = "excerptTextPutback";
        File.WriteAllText(json, CatalogJson.ToJson(catalog));
        var output = Path.Combine(dir, "out.txt");
        var (code, stdout, stderr) = Run("write", "-o", output, json, text);
        Assert.Equal(0, code);
        Assert.Equal("", stdout);
        Assert.Contains("1 件を書き戻しました。", stderr);
        Assert.Equal("MSG=こんにちは\n", File.ReadAllText(output));
        Assert.Equal(original, File.ReadAllBytes(text));
        var updated = CatalogJson.Parse(File.ReadAllText(json));
        Assert.Equal("excerptTextPutback", updated.Program);
        Assert.Equal("3", updated.FileVersion);
        Assert.Equal("1.0.0", updated.ToolVersion);
        Assert.Equal(new[] { "changed", "常体" }, updated.Names);
        Assert.Equal("exact", updated.Entries[0].Status);
        Assert.Equal("key", updated.Entries[0].MatchedBy);
        Assert.Equal("", updated.Entries[0].PreviousSource);
        Assert.Equal("Hello", updated.Entries[0].Source);
    }

    [Fact]
    public void Key_matches_when_the_wording_changed()
    {
        var catalog = Extract("MSG=Hello\n", "(?m)^(?P<id>[^=]+)=(.*)$");
        catalog.Entries[0].Target[0] = "こんにちは";
        catalog.Methods = new List<string> { "key" };
        var output = Apply(catalog, "MSG=Hello!\n");
        Assert.Equal("MSG=こんにちは\n", Encoding.UTF8.GetString(output.Bytes));
        Assert.Equal("revised", output.Catalog.Entries[0].Status);
        Assert.Equal("key", output.Catalog.Entries[0].MatchedBy);
        Assert.Equal("Hello", output.Catalog.Entries[0].PreviousSource);
        Assert.Equal("Hello!", output.Catalog.Entries[0].Source);
    }

    [Fact]
    public void Duplicate_keys_are_not_paired_by_key()
    {
        var catalog = Extract("MSG=A\nMSG=B\n", "(?m)^(?P<id>[^=]+)=(.*)$");
        catalog.Methods = new List<string> { "key" };
        catalog.Entries[0].Target[0] = "X";
        catalog.Entries[1].Target[0] = "Y";
        var output = Apply(catalog, "MSG=A\nMSG=B\n");
        Assert.Equal("MSG=A\nMSG=B\n", Encoding.UTF8.GetString(output.Bytes));
        Assert.All(output.Catalog.Entries, entry => Assert.Equal("unmatched", entry.Status));
    }

    [Fact]
    public void Offset_pairs_before_later_methods_and_disable_keeps_the_saved_method()
    {
        var catalog = Extract("Hello", "(Hello)");
        catalog.Entries[0].Target[0] = "Bye";
        var same = Apply(catalog, "Hello");
        Assert.Equal("offset", same.Catalog.Entries[0].MatchedBy);
        Assert.Equal("Bye", Encoding.UTF8.GetString(same.Bytes));

        var moved = Apply(catalog, "XHello");
        Assert.Equal("exact", moved.Catalog.Entries[0].MatchedBy);
        Assert.Equal("XBye", Encoding.UTF8.GetString(moved.Bytes));

        var disabled = Apply(catalog, "Hello", disabled: new HashSet<string> { "offset" });
        Assert.Equal("exact", disabled.Catalog.Entries[0].MatchedBy);
        Assert.Contains("offset", disabled.Catalog.Methods);
    }

    [Fact]
    public void Context_uses_before_and_after()
    {
        var catalog = Extract("[Hello]", "\\[(.*)\\]");
        catalog.Entries[0].Target[0] = "Bye";
        catalog.Methods = new List<string> { "context" };
        var output = Apply(catalog, "xx[Hello]yy");
        Assert.Equal("context", output.Catalog.Entries[0].MatchedBy);
        Assert.Equal("xx[Bye]yy", Encoding.UTF8.GetString(output.Bytes));
    }

    [Fact]
    public void Anchors_keep_insertions_and_deletions_on_opposite_sides()
    {
        var catalog = Extract("keep-a\nmove-me\nkeep-b\n", "(?m)^(.*)");
        foreach (var entry in catalog.Entries)
        {
            entry.Target[0] = entry.Source.ToUpperInvariant();
        }

        var output = Apply(catalog, "NEW\nkeep-a\nkeep-b\n");
        Assert.Equal("NEW\nKEEP-A\nKEEP-B\n", Encoding.UTF8.GetString(output.Bytes));
        Assert.Equal("unmatched", output.Catalog.Entries[1].Status);
        Assert.Equal("", output.Catalog.Entries[1].MatchedBy);
        Assert.Equal("move-me", output.Catalog.Entries[1].Source);
        Assert.Equal(new[] { "MOVE-ME" }, output.Catalog.Entries[1].Target);
    }

    [Fact]
    public void Revised_pairs_equal_counts_inside_a_gap()
    {
        var catalog = Extract("a\nb\n", "(?m)^(.*)");
        catalog.Entries[1].Target[0] = "X";
        var output = Apply(catalog, "a\nB\n");
        Assert.Equal("a\nX\n", Encoding.UTF8.GetString(output.Bytes));
        Assert.Equal("revised", output.Catalog.Entries[1].Status);
        Assert.Equal("revised", output.Catalog.Entries[1].MatchedBy);
        Assert.Equal("b", output.Catalog.Entries[1].PreviousSource);
        Assert.Equal("B", output.Catalog.Entries[1].Source);
    }

    [Fact]
    public void Empty_or_missing_target_is_not_written()
    {
        var catalog = Extract("Hello", "(Hello)");
        catalog.Entries[0].Target[0] = "";
        var output = Apply(catalog, "Hello");
        Assert.Equal("Hello", Encoding.UTF8.GetString(output.Bytes));
        Assert.Equal("unmatched", output.Catalog.Entries[0].Status);
        Assert.Equal("", output.Catalog.Entries[0].MatchedBy);
        Assert.Equal(new[] { "" }, output.Catalog.Entries[0].Target);

        catalog.Entries[0].Target = new List<string> { "one", "two" };
        catalog.Used = 2;
        var second = Apply(catalog, "Hello");
        Assert.Equal("two", Encoding.UTF8.GetString(second.Bytes));
        Assert.Equal(2, second.Catalog.Used);
    }

    [Fact]
    public void Nfc_is_applied_only_to_the_replacement()
    {
        var decomposed = "\u304B\u3099";
        var catalog = Extract(decomposed, "(.*)");
        var output = Apply(catalog, decomposed);
        Assert.Equal("\u304C", Encoding.UTF8.GetString(output.Bytes));
        Assert.Equal(decomposed, output.Catalog.Entries[0].Source);
        Assert.Equal("exact", output.Catalog.Entries[0].Status);
    }

    [Fact]
    public void Unchanged_write_keeps_the_original_bytes()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'A', (byte)'B' };
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        File.WriteAllBytes(text, bytes);
        var json = Path.Combine(dir, "cat.json");
        Assert.Equal(0, Run("extract", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, text).Code);
        var output = Path.Combine(dir, "out.txt");
        Assert.Equal(0, Run("write", "-o", output, json, text).Code);
        Assert.Equal(bytes, File.ReadAllBytes(output));
    }

    [Fact]
    public void Unknown_fields_are_dropped_and_methods_are_reordered()
    {
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        File.WriteAllText(text, "Hello");
        var json = Path.Combine(dir, "cat.json");
        File.WriteAllText(json, """
        {
          "program": "excerptTextPutback",
          "formatVersion": 1,
          "toolVersion": "9.9.9",
          "fileVersion": "3",
          "note": "drop me",
          "file": "old.txt",
          "encoding": "utf-8",
          "bom": false,
          "pattern": "(Hello)",
          "keys": [],
          "names": ["changed"],
          "used": 1,
          "methods": ["revised", "exact"],
          "entries": [
            {
              "key": [],
              "before": "",
              "source": "Hello",
              "after": "",
              "previousSource": "",
              "target": ["Bye"],
              "byteStart": 0,
              "byteLength": 5,
              "status": "",
              "matchedBy": ""
            }
          ]
        }
        """);
        var output = Path.Combine(dir, "out.txt");
        var updated = Path.Combine(dir, "new.json");
        Assert.Equal(0, Run("write", "-disable", "offset", "-o", output, "-json-out", updated, json, text).Code);
        Assert.Equal("Bye", File.ReadAllText(output));
        var raw = File.ReadAllText(updated);
        Assert.DoesNotContain("note", raw, StringComparison.Ordinal);
        Assert.Equal("Hello", File.ReadAllText(text));
        Assert.Contains("\"toolVersion\": \"9.9.9\"", File.ReadAllText(json), StringComparison.Ordinal);
        var catalog = CatalogJson.Parse(raw);
        Assert.Equal(new[] { "exact", "revised" }, catalog.Methods);
        Assert.Equal("1.0.0", catalog.ToolVersion);
        Assert.Equal("3", catalog.FileVersion);
        Assert.Contains("未知の方式名です: nope", Run("write", "-disable", "nope", "-o", output, json, text).Stderr);
    }

    [Fact]
    public void Format_and_usage_errors_use_the_spec_exit_codes()
    {
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        File.WriteAllText(text, "Hello");
        var json = Path.Combine(dir, "cat.json");
        var output = Path.Combine(dir, "out.txt");
        File.WriteAllText(json, """{"program":"x","entries":[]}""");
        Assert.Equal(1, Run("write", "-o", output, json, text).Code);
        Assert.False(File.Exists(output));

        File.WriteAllText(json, """
        {"formatVersion":2,"pattern":"(.*)","encoding":"utf-8","entries":[]}
        """);
        Assert.Equal(1, Run("write", "-o", output, json, text).Code);

        File.WriteAllText(json, """
        {"formatVersion":1,"pattern":"(.*)","encoding":"utf-8","used":0,"entries":[]}
        """);
        Assert.Contains("used", Run("write", "-o", output, json, text).Stderr);
        Assert.False(File.Exists(output));

        Assert.Equal(2, Run().Code);
        Assert.Equal(2, Run("nope").Code);
        Assert.Equal(0, Run("version").Code);
        Assert.Equal("1.0.0\n", Run("version").Stdout);
        Assert.Equal("", Run("extract", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, text).Stdout);
    }

    [Fact]
    public void Write_refuses_to_replace_the_input_text()
    {
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        File.WriteAllText(text, "Hello");
        var json = Path.Combine(dir, "cat.json");
        Assert.Equal(0, Run("extract", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, text).Code);
        Assert.Equal(1, Run("write", "-o", text, json, text).Code);
        Assert.Equal("Hello", File.ReadAllText(text));
    }

    [Fact]
    public void Extract_disable_drops_methods_from_the_new_catalog()
    {
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        File.WriteAllText(text, "Hello");
        var json = Path.Combine(dir, "out.json");
        Assert.Equal(0, Run("extract", "-encoding", "utf-8", "-pattern", "(Hello)", "-disable", "offset,revised", "-o", json, text).Code);
        Assert.Equal(new[] { "key", "exact", "context", "align" }, CatalogJson.Parse(File.ReadAllText(json)).Methods);
    }

    [Fact]
    public void Literal_backslash_n_in_a_target_is_not_a_newline()
    {
        var catalog = Extract("Hello", "(Hello)");
        catalog.Entries[0].Target[0] = "やあ\\n世界";
        var output = Apply(catalog, "Hello");
        Assert.Equal("やあ\\n世界", Encoding.UTF8.GetString(output.Bytes));
    }

    private static Catalog Extract(string text, string pattern)
    {
        using var dir = Temp();
        var input = Path.Combine(dir, "in.txt");
        File.WriteAllText(input, text);
        var json = Path.Combine(dir, "out.json");
        var result = Run("extract", "-encoding", "utf-8", "-pattern", pattern, "-o", json, input);
        Assert.True(result.Code == 0, result.Stderr);
        return CatalogJson.Parse(File.ReadAllText(json));
    }

    private static (int Code, string Stderr) ExtractCode(string text, string pattern)
    {
        using var dir = Temp();
        var input = Path.Combine(dir, "in.txt");
        File.WriteAllText(input, text);
        var result = Run("extract", "-encoding", "utf-8", "-pattern", pattern, "-o", Path.Combine(dir, "out.json"), input);
        return (result.Code, result.Stderr);
    }

    private static (byte[] Bytes, Catalog Catalog) Apply(Catalog catalog, string text, HashSet<string>? disabled = null)
    {
        using var dir = Temp();
        var input = Path.Combine(dir, "in.txt");
        File.WriteAllText(input, text);
        var json = Path.Combine(dir, "cat.json");
        File.WriteAllText(json, CatalogJson.ToJson(catalog));
        var output = Path.Combine(dir, "out.txt");
        var args = new List<string> { "write", "-o", output };
        if (disabled is not null)
        {
            args.Add("-disable");
            args.Add(string.Join(',', disabled));
        }

        args.Add(json);
        args.Add(input);
        var result = Run(args.ToArray());
        Assert.True(result.Code == 0, result.Stderr);
        return (File.ReadAllBytes(output), CatalogJson.Parse(File.ReadAllText(json)));
    }

    [Fact]
    public void Message_language_follows_the_os_and_the_option()
    {
        Assert.Contains("Unknown command.", RunInCulture("fr", "nope").Stderr);
        Assert.Contains("Unknown command.", RunInCulture("en", "nope").Stderr);
        Assert.Contains("未知のコマンドです。", RunInCulture("ja", "nope").Stderr);
        Assert.Contains("未知のコマンドです。", RunInCulture("en", "-language", "ja", "nope").Stderr);
        Assert.Contains("Unknown command.", RunInCulture("ja", "nope", "-language", "en").Stderr);
        Assert.Contains("Unknown command.", RunInCulture("ja", "-language=en-US", "nope").Stderr);
        Assert.Contains("未知のコマンドです。", RunInCulture("en", "-language", "ja-JP", "nope").Stderr);
        Assert.Contains("言語は en か ja です。", RunInCulture("ja", "-language", "fr").Stderr);
        Assert.Contains("Language must be en or ja.", RunInCulture("fr", "-language", "de").Stderr);
        Assert.Equal(2, RunInCulture("ja", "-language", "fr").Code);
        Assert.Contains("Usage:", RunInCulture("fr").Stderr);
        Assert.Contains("使い方:", RunInCulture("ja-JP").Stderr);
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        var json = Path.Combine(dir, "out.json");
        File.WriteAllText(text, "a");
        Assert.Contains("Extracted 1 entries.", RunInCulture("en", "extract", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, text).Stderr);
    }

    private static (int Code, string Stdout, string Stderr) Run(params string[] args)
    {
        if (!args.Contains("-language") && !args.Any(arg => arg.StartsWith("-language=", StringComparison.Ordinal)))
        {
            args = args.Append("-language").Append("ja").ToArray();
        }

        return RunRaw(args);
    }

    private static (int Code, string Stdout, string Stderr) RunInCulture(string culture, params string[] args)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return RunRaw(args);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static (int Code, string Stdout, string Stderr) RunRaw(string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = Cli.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static TempDir Temp()
    {
        return new TempDir();
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = Directory.CreateTempSubdirectory("excerptStringPutback").FullName;
        }

        public string Path { get; }

        public static implicit operator string(TempDir dir)
        {
            return dir.Path;
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
