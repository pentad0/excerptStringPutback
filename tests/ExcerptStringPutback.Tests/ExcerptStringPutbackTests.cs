using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ExcerptStringPutback.Tests;

public class ExcerptStringPutbackTests
{
    [Fact]
    public void Extract_splits_named_keys_and_named_sources()
    {
        using var dir = Temp();
        var text = Path.Combine(dir, "script.txt");
        File.WriteAllText(text, "1\t15\tHello\n");
        var json = Path.Combine(dir, "out.json");
        var (code, stdout, stderr) = Run(
            "excerpt", "-encoding", "utf-8",
            "-pattern", "(?m)^(?Key<chapter>\\d+)\\t(?Key<id>\\d+)\\t(?Source<line>.*)",
            "-o", json, text);

        Assert.Equal(0, code);
        Assert.Equal("", stdout);
        Assert.Contains("1 件", stderr);
        var catalog = CatalogJson.Parse(File.ReadAllText(json));
        Assert.Equal(new[] { "chapter", "id" }, catalog.KeyNames);
        Assert.Equal(new[] { "line" }, catalog.SourceNames);
        var entry = Assert.Single(catalog.Entries);
        Assert.Equal(new[] { "1", "15" }, entry.Key);
        Assert.Equal(new[] { "1\t15\t" }, entry.Before);
        Assert.Equal(new[] { "Hello" }, entry.Source);
        Assert.Equal(new[] { "" }, entry.After);
        Assert.Equal(new[] { "Hello" }, Assert.Single(entry.Replacements));
        Assert.Equal(new[] { 5 }, entry.ByteStart);
        Assert.Equal(new[] { 5 }, entry.ByteLength);
        Assert.Equal("", entry.Status);
        Assert.Equal("", entry.MatchedBy);
        Assert.Equal(new[] { "changed" }, catalog.ReplacementNames);
        Assert.Equal(0, catalog.Used);
        Assert.Equal("1", catalog.FileVersion.Json);
        Assert.Equal("\"\"", catalog.Note.Json);
        Assert.Equal("excerptStringPutback", catalog.Program);
        Assert.Equal("1.2.0", catalog.ToolVersion);
        Assert.Equal(3, catalog.FormatVersion);
        Assert.Equal(ToolInfo.Methods, catalog.Methods);
        var raw = File.ReadAllText(json);
        Assert.StartsWith("{\n  \"program\": \"excerptStringPutback\"", raw, StringComparison.Ordinal);
        Assert.Contains("  \"fileVersion\": 1,\n  \"note\": \"\",\n", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u003c", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Crlf_line_keeps_the_carriage_return_in_the_source()
    {
        var catalog = Extract("MSG=Hi\r\n", "(?m)^(?Key<id>[^=]+)=(?Source<value>.*)$");
        var entry = Assert.Single(catalog.Entries);
        Assert.Equal(new[] { "MSG" }, entry.Key);
        Assert.Equal(new[] { "MSG=" }, entry.Before);
        Assert.Equal(new[] { "Hi\r" }, entry.Source);
        Assert.Equal(new[] { "" }, entry.After);
    }

    [Fact]
    public void Final_empty_match_is_dropped_and_a_middle_blank_line_is_kept()
    {
        Assert.Equal(new[] { "a" }, Extract("a\n", "(?m)^(.*)").Entries.Select(entry => entry.Source.Single()));
        Assert.Equal(new[] { "a", "" }, Extract("a\n\n", "(?m)^(.*)").Entries.Select(entry => entry.Source.Single()));
    }

    [Fact]
    public void Ungreedy_flag_swaps_quantifiers()
    {
        Assert.Equal(new[] { "a", "a", "a" }, Extract("aaa", "(?U)a+").Entries.Select(entry => entry.Source.Single()));
        Assert.Equal(new[] { "aaa" }, Extract("aaa", "(?U)a+?").Entries.Select(entry => entry.Source.Single()));
        Assert.Equal(new[] { "B" }, Extract("B", "(?i)a|b").Entries.Select(entry => entry.Source.Single()));
        Assert.Empty(Extract("a\n", "a$").Entries);
        Assert.Equal(new[] { "a" }, Extract("a", "a$").Entries.Select(entry => entry.Source.Single()));
    }

    [Fact]
    public void Illegal_syntax_and_duplicate_names_are_rejected()
    {
        var (code, stderr) = ExtractCode("abc", "(?=a)");
        Assert.Equal(1, code);
        Assert.Contains("先読み", stderr);
        Assert.Equal(1, ExtractCode("abc", "(?Key<a>.)(?Key<a>.)").Code);
        Assert.Contains("同じ名前", ExtractCode("abc", "(?Key<a>.)(?Key<a>.)").Stderr);
        Assert.Equal(1, ExtractCode("abc", "(?Key<a>.)(?Source<a>.)").Code);
        Assert.Contains("重な", ExtractCode("ab", "(?Source<outer>a(?Source<inner>b))").Stderr);
    }

    [Fact]
    public void Groups_other_than_key_and_source_are_not_extracted()
    {
        var catalog = Extract("Hello", "(?P<id>He)(?Note<x>ll)(o)");
        Assert.Empty(catalog.KeyNames);
        Assert.Equal(new[] { "" }, catalog.SourceNames);
        var entry = Assert.Single(catalog.Entries);
        Assert.Empty(entry.Key);
        Assert.Equal(new[] { "Hello" }, entry.Source);
        Assert.Equal(new[] { "" }, entry.Before);
        Assert.Equal(new[] { "" }, entry.After);
    }

    [Fact]
    public void One_match_keeps_every_source_in_one_entry()
    {
        var catalog = Extract("15\tHello\tWorld", "(?m)^(?Key<id>\\d+)\t(?Source<title>[^\\t]*)\t(?Source<body>.*)");
        Assert.Equal(new[] { "id" }, catalog.KeyNames);
        Assert.Equal(new[] { "title", "body" }, catalog.SourceNames);
        var entry = Assert.Single(catalog.Entries);
        Assert.Equal(new[] { "15" }, entry.Key);
        Assert.Equal(new[] { "15\t", "15\tHello\t" }, entry.Before);
        Assert.Equal(new[] { "Hello", "World" }, entry.Source);
        Assert.Equal(new[] { "\tWorld", "" }, entry.After);
        Assert.Equal(new[] { 3, 9 }, entry.ByteStart);
        Assert.Equal(new[] { 5, 5 }, entry.ByteLength);
        Assert.Equal(new[] { "Hello", "World" }, Assert.Single(entry.Replacements));

        entry.Replacements = new List<List<string>>
        {
            new() { "こんにちは", "世界" },
            new() { "やあ", "ワールド" },
        };
        var first = Apply(catalog, "15\tHello\tWorld");
        Assert.Equal("15\tこんにちは\t世界", Encoding.UTF8.GetString(first.Bytes));
        Assert.Equal(new[] { "Hello", "World" }, first.Catalog.Entries[0].Source);
    }

    [Fact]
    public void A_shorter_replacement_row_writes_only_the_present_slots()
    {
        var catalog = Extract("15\tHello\tWorld", "(?m)^(?Key<id>\\d+)\t(?Source<title>[^\\t]*)\t(?Source<body>.*)");
        catalog.Entries[0].Replacements = new List<List<string>> { new() { "こんにちは" } };
        var output = Apply(catalog, "15\tHello\tWorld");
        Assert.Equal("15\tこんにちは\tWorld", Encoding.UTF8.GetString(output.Bytes));
    }

    [Fact]
    public void An_absent_source_keeps_its_index_and_is_not_written()
    {
        var catalog = Extract("B", "(?Source<a>A)?(?Source<b>B)");
        var entry = Assert.Single(catalog.Entries);
        Assert.Equal(new[] { "a", "b" }, catalog.SourceNames);
        Assert.Equal(new[] { "", "B" }, entry.Source);
        Assert.Equal(new[] { -1, 0 }, entry.ByteStart);
        Assert.Equal(new[] { 0, 1 }, entry.ByteLength);
        entry.Replacements[0][0] = "GONE";
        entry.Replacements[0][1] = "C";
        var output = Apply(catalog, "B");
        Assert.Equal("C", Encoding.UTF8.GetString(output.Bytes));
    }

    [Fact]
    public void Putback_keeps_the_catalog_source_when_the_file_text_differs()
    {
        var catalog = Extract("15\tHello\tWorld", "(?m)^(?Key<id>\\d+)\t(?Source<title>[^\\t]*)\t(?Source<body>.*)");
        catalog.Entries[0].Replacements[0][0] = "こんにちは";
        var output = Apply(catalog, "15\tBye\tWorld");
        Assert.Equal("15\tこんにちは\tWorld", Encoding.UTF8.GetString(output.Bytes));
        Assert.Equal("revised", output.Catalog.Entries[0].Status);
        Assert.Equal(new[] { "Hello", "World" }, output.Catalog.Entries[0].Source);
        Assert.Equal(new[] { 3, 9 }, output.Catalog.Entries[0].ByteStart);
        Assert.Equal(new[] { "15" }, output.Catalog.Entries[0].Key);
    }

    [Fact]
    public void Byte_positions_include_the_bom()
    {
        using var dir = Temp();
        var utf8 = Path.Combine(dir, "utf8.txt");
        File.WriteAllBytes(utf8, new byte[] { 0xEF, 0xBB, 0xBF, (byte)'A', (byte)'B' });
        var json = Path.Combine(dir, "utf8.json");
        Assert.Equal(0, Run("excerpt", "-encoding", "utf-16le", "-pattern", "(.*)", "-o", json, utf8).Code);
        var entry = Assert.Single(CatalogJson.Parse(File.ReadAllText(json)).Entries);
        Assert.Equal(new[] { 3 }, entry.ByteStart);
        Assert.Equal(new[] { 2 }, entry.ByteLength);
        Assert.Equal("utf-8", CatalogJson.Parse(File.ReadAllText(json)).Encoding);
        Assert.True(CatalogJson.Parse(File.ReadAllText(json)).Bom);
        Assert.Contains("BOM を優先", Run("excerpt", "-encoding", "utf-16le", "-pattern", "(.*)", "-o", json, utf8).Stderr);

        var utf16 = Path.Combine(dir, "utf16.txt");
        File.WriteAllBytes(utf16, new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x42, 0x00 });
        var json16 = Path.Combine(dir, "utf16.json");
        Assert.Equal(0, Run("excerpt", "-encoding", "utf-16le", "-pattern", "(.*)", "-o", json16, utf16).Code);
        var wide = Assert.Single(CatalogJson.Parse(File.ReadAllText(json16)).Entries);
        Assert.Equal(new[] { "AB" }, wide.Source);
        Assert.Equal(new[] { 2 }, wide.ByteStart);
        Assert.Equal(new[] { 4 }, wide.ByteLength);
    }

    [Fact]
    public void Encoding_errors_cover_bom_rules()
    {
        using var dir = Temp();
        var bare = Path.Combine(dir, "bare.txt");
        File.WriteAllBytes(bare, "AB"u8.ToArray());
        var json = Path.Combine(dir, "out.json");
        Assert.Equal(1, Run("excerpt", "-pattern", "(.*)", "-o", json, bare).Code);
        Assert.Equal(1, Run("excerpt", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, Path.Combine(dir, "missing.txt")).Code);

        var bad = Path.Combine(dir, "bad.txt");
        File.WriteAllBytes(bad, new byte[] { 0xFF });
        Assert.Equal(1, Run("excerpt", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, bad).Code);

        var utf32 = Path.Combine(dir, "utf32.txt");
        File.WriteAllBytes(utf32, new byte[] { 0xFF, 0xFE, 0x00, 0x00 });
        Assert.Contains("UTF-32", Run("excerpt", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, utf32).Stderr);

        var odd = Path.Combine(dir, "odd.txt");
        File.WriteAllBytes(odd, new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x42 });
        Assert.Contains("奇数", Run("excerpt", "-encoding", "utf-16le", "-pattern", "(.*)", "-o", json, odd).Stderr);

        var lone = Path.Combine(dir, "lone.txt");
        File.WriteAllBytes(lone, new byte[] { 0xFF, 0xFE, 0x00, 0xD8 });
        Assert.Contains("サロゲート", Run("excerpt", "-encoding", "utf-16le", "-pattern", "(.*)", "-o", json, lone).Stderr);
    }

    [Fact]
    public void Json_puts_each_array_element_on_its_own_line()
    {
        var catalog = Extract(") ( subhead = ", "(.*)");
        var raw = CatalogJson.ToJson(catalog);
        Assert.Contains("  \"sourceNames\": [\n    \"\"\n  ],\n", raw, StringComparison.Ordinal);
        Assert.Contains("  \"replacementNames\": [\n    \"changed\"\n  ],\n", raw, StringComparison.Ordinal);
        Assert.Contains("  \"methods\": [\n    \"sourceByte\",\n    \"key\",\n    \"exact\",\n    \"context\",\n    \"align\",\n    \"revised\"\n  ]\n}\n", raw, StringComparison.Ordinal);
        Assert.True(raw.IndexOf("\"entries\"", StringComparison.Ordinal) < raw.IndexOf("\"used\"", StringComparison.Ordinal));
        Assert.True(raw.IndexOf("\"status\"", StringComparison.Ordinal) < raw.LastIndexOf("\"matchedBy\"", StringComparison.Ordinal));
        Assert.Contains("      \"key\": [],\n", raw, StringComparison.Ordinal);
        Assert.Contains("      \"replacements\": [\n        [\n          \") ( subhead = \"\n        ]\n      ],\n", raw, StringComparison.Ordinal);
        var entry = Assert.Single(catalog.Entries);
        Assert.Equal("", entry.Status);
        Assert.Equal("", entry.MatchedBy);
    }

    [Fact]
    public void Angle_brackets_are_not_escaped_and_newlines_are()
    {
        var catalog = Extract("a<b&c\n", "(?s)(.*)");
        var raw = CatalogJson.ToJson(catalog);
        Assert.Contains("\"a<b&c\\n\"", raw, StringComparison.Ordinal);
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
        Assert.Equal(0, Run("excerpt", "-encoding", "utf-8", "-pattern-file", pattern, "-o", json, text).Code);
        Assert.Equal(new[] { "AB" }, Assert.Single(CatalogJson.Parse(File.ReadAllText(json)).Entries).Source);
    }

    [Fact]
    public void Write_replaces_only_the_source_capture()
    {
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        File.WriteAllBytes(text, "MSG=Hello\n"u8.ToArray());
        var original = File.ReadAllBytes(text);
        var json = Path.Combine(dir, "cat.json");
        Assert.Equal(0, Run("excerpt", "-encoding", "utf-8", "-pattern", "(?m)^(?Key<id>[^=]+)=(?Source<value>.*)$", "-o", json, text).Code);
        var catalog = CatalogJson.Parse(File.ReadAllText(json));
        catalog.Entries[0].Replacements[0][0] = "こんにちは";
        catalog.ReplacementNames.Add("常体");
        catalog.FileVersion.Json = "\"3\"";
        catalog.Program = "excerptTextPutback";
        File.WriteAllText(json, CatalogJson.ToJson(catalog));
        var output = Path.Combine(dir, "out.txt");
        var (code, stdout, stderr) = Run("putback", "-o", output, json, text);
        Assert.Equal(0, code);
        Assert.Equal("", stdout);
        Assert.Contains("1 件を書き戻しました。", stderr);
        Assert.Equal("MSG=こんにちは\n", File.ReadAllText(output));
        Assert.Equal(original, File.ReadAllBytes(text));
        var updated = CatalogJson.Parse(File.ReadAllText(json));
        Assert.Equal("excerptTextPutback", updated.Program);
        Assert.Equal("\"3\"", updated.FileVersion.Json);
        Assert.Equal("1.2.0", updated.ToolVersion);
        Assert.Equal(new[] { "changed", "常体" }, updated.ReplacementNames);
        Assert.Equal("exact", updated.Entries[0].Status);
        Assert.Equal("sourceByte", updated.Entries[0].MatchedBy);
        Assert.Equal(new[] { "Hello" }, updated.Entries[0].Source);
    }

    [Fact]
    public void Key_matches_when_the_wording_changed()
    {
        var catalog = Extract("MSG=Hello\n", "(?m)^(?Key<id>[^=]+)=(?Source<value>.*)$");
        catalog.Entries[0].Replacements[0][0] = "こんにちは";
        catalog.Methods = new List<string> { "key" };
        var output = Apply(catalog, "MSG=Hello!\n");
        Assert.Equal("MSG=こんにちは\n", Encoding.UTF8.GetString(output.Bytes));
        Assert.Equal("revised", output.Catalog.Entries[0].Status);
        Assert.Equal("key", output.Catalog.Entries[0].MatchedBy);
        Assert.Equal(new[] { "MSG" }, output.Catalog.Entries[0].Key);
        Assert.Equal(new[] { "Hello" }, output.Catalog.Entries[0].Source);
        Assert.Equal(new[] { "MSG=" }, output.Catalog.Entries[0].Before);
    }

    [Fact]
    public void Duplicate_keys_are_not_paired_by_key()
    {
        var catalog = Extract("MSG=A\nMSG=B\n", "(?m)^(?Key<id>[^=]+)=(?Source<value>.*)$");
        catalog.Methods = new List<string> { "key" };
        catalog.Entries[0].Replacements[0][0] = "X";
        catalog.Entries[1].Replacements[0][0] = "Y";
        var output = Apply(catalog, "MSG=A\nMSG=B\n");
        Assert.Equal("MSG=A\nMSG=B\n", Encoding.UTF8.GetString(output.Bytes));
        Assert.All(output.Catalog.Entries, entry => Assert.Equal("unmatched", entry.Status));
    }

    [Fact]
    public void Offset_pairs_before_later_methods_and_disable_keeps_the_saved_method()
    {
        var catalog = Extract("Hello", "(Hello)");
        catalog.Entries[0].Replacements[0][0] = "Bye";
        var same = Apply(catalog, "Hello");
        Assert.Equal("sourceByte", same.Catalog.Entries[0].MatchedBy);
        Assert.Equal("Bye", Encoding.UTF8.GetString(same.Bytes));

        var moved = Apply(catalog, "XHello");
        Assert.Equal("exact", moved.Catalog.Entries[0].MatchedBy);
        Assert.Equal("XBye", Encoding.UTF8.GetString(moved.Bytes));

        var disabled = Apply(catalog, "Hello", disabled: new HashSet<string> { "sourceByte" });
        Assert.Equal("exact", disabled.Catalog.Entries[0].MatchedBy);
        Assert.Contains("sourceByte", disabled.Catalog.Methods);
    }

    [Fact]
    public void Context_uses_before_and_after()
    {
        var catalog = Extract("[Hello]", "\\[(?Source<text>.*)\\]");
        catalog.Entries[0].Replacements[0][0] = "Bye";
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
            entry.Replacements[0][0] = entry.Source[0].ToUpperInvariant();
        }

        var output = Apply(catalog, "NEW\nkeep-a\nkeep-b\n");
        Assert.Equal("NEW\nKEEP-A\nKEEP-B\n", Encoding.UTF8.GetString(output.Bytes));
        Assert.Equal("unmatched", output.Catalog.Entries[1].Status);
        Assert.Equal("", output.Catalog.Entries[1].MatchedBy);
        Assert.Equal(new[] { "move-me" }, output.Catalog.Entries[1].Source);
        Assert.Equal(new[] { "MOVE-ME" }, Assert.Single(output.Catalog.Entries[1].Replacements));
    }

    [Fact]
    public void Revised_pairs_equal_counts_inside_a_gap()
    {
        var catalog = Extract("a\nb\n", "(?m)^(.*)");
        catalog.Entries[1].Replacements[0][0] = "X";
        var output = Apply(catalog, "a\nB\n");
        Assert.Equal("a\nX\n", Encoding.UTF8.GetString(output.Bytes));
        Assert.Equal("revised", output.Catalog.Entries[1].Status);
        Assert.Equal("revised", output.Catalog.Entries[1].MatchedBy);
        Assert.Equal(new[] { "b" }, output.Catalog.Entries[1].Source);
    }

    [Fact]
    public void Empty_target_clears_the_source_and_a_missing_target_is_not_written()
    {
        var catalog = Extract("Hello", "(Hello)");
        catalog.Entries[0].Replacements[0][0] = "";
        var output = Apply(catalog, "Hello");
        Assert.Equal("", Encoding.UTF8.GetString(output.Bytes));
        Assert.Equal("exact", output.Catalog.Entries[0].Status);
        Assert.Equal("sourceByte", output.Catalog.Entries[0].MatchedBy);
        Assert.Equal(new[] { "Hello" }, output.Catalog.Entries[0].Source);
        Assert.Equal(new[] { "" }, Assert.Single(output.Catalog.Entries[0].Replacements));

        var kept = Extract("MSG=Hello\n", "(?m)^(?Key<id>[^=]+)=(?Source<value>.*)$");
        kept.Entries[0].Replacements[0][0] = "";
        var cleared = Apply(kept, "MSG=Hello\n");
        Assert.Equal("MSG=\n", Encoding.UTF8.GetString(cleared.Bytes));
        Assert.Equal("sourceByte", cleared.Catalog.Entries[0].MatchedBy);
        Assert.Equal(new[] { "Hello" }, cleared.Catalog.Entries[0].Source);

        catalog.Entries[0].Replacements = new List<List<string>> { new() { "one" }, new() { "two" } };
        catalog.Used = 1;
        var second = Apply(catalog, "Hello");
        Assert.Equal("two", Encoding.UTF8.GetString(second.Bytes));
        Assert.Equal(1, second.Catalog.Used);

        catalog.Entries[0].Replacements = new List<List<string>> { new() { "one" } };
        var missing = Apply(catalog, "Hello");
        Assert.Equal("Hello", Encoding.UTF8.GetString(missing.Bytes));
        Assert.Equal("unmatched", missing.Catalog.Entries[0].Status);
        Assert.Equal("", missing.Catalog.Entries[0].MatchedBy);
        Assert.Equal(new[] { "one" }, Assert.Single(missing.Catalog.Entries[0].Replacements));
    }

    [Fact]
    public void Nfc_is_applied_only_to_the_replacement()
    {
        var decomposed = "\u304B\u3099";
        var catalog = Extract(decomposed, "(.*)");
        var output = Apply(catalog, decomposed);
        Assert.Equal("\u304C", Encoding.UTF8.GetString(output.Bytes));
        Assert.Equal(new[] { decomposed }, output.Catalog.Entries[0].Source);
        Assert.Equal("exact", output.Catalog.Entries[0].Status);
    }

    [Fact]
    public void Source_flag_writes_the_catalog_source_and_keeps_it()
    {
        var catalog = Extract("15\tHello\tWorld", "(?m)^(?Key<id>\\d+)\t(?Source<title>[^\\t]*)\t(?Source<body>.*)");
        catalog.Entries[0].Replacements[0][0] = "こんにちは";
        catalog.Entries[0].Replacements[0][1] = "世界";
        catalog.Used = 1;
        var output = Apply(catalog, "15\tBye\tWorld", source: true);
        Assert.Equal("15\tHello\tWorld", Encoding.UTF8.GetString(output.Bytes));
        var entry = output.Catalog.Entries[0];
        Assert.Equal("revised", entry.Status);
        Assert.Equal("key", entry.MatchedBy);
        Assert.Equal(new[] { "15" }, entry.Key);
        Assert.Equal(new[] { "Hello", "World" }, entry.Source);
        Assert.Equal(new[] { "15\t", "15\tHello\t" }, entry.Before);
        Assert.Equal(new[] { "\tWorld", "" }, entry.After);
        Assert.Equal(new[] { 3, 9 }, entry.ByteStart);
        Assert.Equal(new[] { 5, 5 }, entry.ByteLength);
        Assert.Equal(new[] { "こんにちは", "世界" }, Assert.Single(entry.Replacements));
        Assert.Equal(UsedValue.Source, output.Catalog.Used);

        catalog.Entries[0].Source = new List<string> { "やあ" };
        var shorter = Apply(catalog, "15\tHello\tWorld", source: true);
        Assert.Equal("15\tやあ\tWorld", Encoding.UTF8.GetString(shorter.Bytes));
        Assert.Equal(new[] { "やあ" }, shorter.Catalog.Entries[0].Source);
        Assert.Equal(UsedValue.Source, shorter.Catalog.Used);

        var longer = Extract("15\tHello\tWorld", "(?m)^(?Key<id>\\d+)\t(?Source<title>[^\\t]*)\t(?Source<body>.*)");
        longer.Entries[0].Source.Add("extra");
        var extra = Apply(longer, "15\tHello\tWorld", source: true);
        Assert.Equal("15\tHello\tWorld", Encoding.UTF8.GetString(extra.Bytes));
        Assert.Equal(new[] { "Hello", "World", "extra" }, extra.Catalog.Entries[0].Source);
        Assert.Equal("revised", extra.Catalog.Entries[0].Status);

        var decomposed = "\u304B\u3099";
        var rawCatalog = Extract("file", "(.*)");
        rawCatalog.Entries[0].Source[0] = decomposed;
        rawCatalog.Entries[0].Replacements[0][0] = "KEEP";
        rawCatalog.Used = 1;
        var raw = Apply(rawCatalog, "file", source: true);
        Assert.Equal(Encoding.UTF8.GetBytes(decomposed), raw.Bytes);
        Assert.Equal(new[] { decomposed }, raw.Catalog.Entries[0].Source);
        Assert.Equal(new[] { "KEEP" }, Assert.Single(raw.Catalog.Entries[0].Replacements));
        Assert.Equal(UsedValue.Source, raw.Catalog.Used);

        var absent = Extract("B", "(?Source<a>A)?(?Source<b>B)");
        absent.Entries[0].Source[0] = "GONE";
        absent.Entries[0].Source[1] = "C";
        var skipped = Apply(absent, "B", source: true);
        Assert.Equal("C", Encoding.UTF8.GetString(skipped.Bytes));
        Assert.Equal(new[] { "GONE", "C" }, skipped.Catalog.Entries[0].Source);
        Assert.Equal(new[] { -1, 0 }, skipped.Catalog.Entries[0].ByteStart);

        var clear = Extract("Hello", "(Hello)");
        clear.Entries[0].Source[0] = "";
        var emptied = Apply(clear, "Hello", source: true);
        Assert.Equal("", Encoding.UTF8.GetString(emptied.Bytes));
        Assert.Equal(new[] { "" }, emptied.Catalog.Entries[0].Source);
        Assert.Equal("revised", emptied.Catalog.Entries[0].Status);

        var same = Extract("Hello", "(.*)");
        same.Entries[0].Replacements[0][0] = "Bye";
        same.Used = 1;
        var unchanged = Apply(same, "Hello", source: true);
        Assert.Equal(Encoding.UTF8.GetBytes("Hello"), unchanged.Bytes);
        Assert.Equal("exact", unchanged.Catalog.Entries[0].Status);
        Assert.Equal(new[] { "Hello" }, unchanged.Catalog.Entries[0].Source);
        Assert.Equal(new[] { "Bye" }, Assert.Single(unchanged.Catalog.Entries[0].Replacements));
        Assert.Equal(UsedValue.Source, unchanged.Catalog.Used);
    }

    [Fact]
    public void Unchanged_write_keeps_the_original_bytes()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'A', (byte)'B' };
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        File.WriteAllBytes(text, bytes);
        var json = Path.Combine(dir, "cat.json");
        Assert.Equal(0, Run("excerpt", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, text).Code);
        var output = Path.Combine(dir, "out.txt");
        Assert.Equal(0, Run("putback", "-o", output, json, text).Code);
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
          "formatVersion": 3,
          "toolVersion": "9.9.9",
          "fileVersion": "3",
          "extra": "drop me",
          "file": "old.txt",
          "encoding": "utf-8",
          "bom": false,
          "pattern": "(Hello)",
          "keyNames": [],
          "sourceNames": [""],
          "replacementNames": ["changed"],
          "used": 0,
          "methods": ["revised", "exact"],
          "entries": [
            {
              "key": [],
              "before": [""],
              "source": ["Hello"],
              "after": [""],
              "previousSource": [""],
              "replacements": [["Bye"]],
              "sourceByteStart": [0],
              "sourceByteLength": [5],
              "status": "",
              "matchedBy": ""
            }
          ]
        }
        """);
        var output = Path.Combine(dir, "out.txt");
        var updated = Path.Combine(dir, "new.json");
        Assert.Equal(0, Run("putback", "-disable", "sourceByte", "-o", output, "-json-out", updated, json, text).Code);
        Assert.Equal("Bye", File.ReadAllText(output));
        var raw = File.ReadAllText(updated);
        Assert.DoesNotContain("extra", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("previousSource", raw, StringComparison.Ordinal);
        Assert.Contains("\"note\": \"\"", raw, StringComparison.Ordinal);
        Assert.Equal("Hello", File.ReadAllText(text));
        Assert.Contains("\"toolVersion\": \"9.9.9\"", File.ReadAllText(json), StringComparison.Ordinal);
        var catalog = CatalogJson.Parse(raw);
        Assert.Equal(new[] { "exact", "revised" }, catalog.Methods);
        Assert.Equal("9.9.9", catalog.ToolVersion);
        Assert.Equal("old.txt", catalog.File);
        Assert.Equal("utf-8", catalog.Encoding);
        Assert.False(catalog.Bom);
        Assert.Equal(new[] { "" }, catalog.SourceNames);
        Assert.Equal(new[] { "Hello" }, catalog.Entries[0].Source);
        Assert.Equal(new[] { 0 }, catalog.Entries[0].ByteStart);
        Assert.Equal("\"3\"", catalog.FileVersion.Json);
        Assert.Contains("未知の方式名です: nope", Run("putback", "-disable", "nope", "-o", output, json, text).Stderr);
    }

    [Fact]
    public void File_version_keeps_any_json_value_and_putback_does_not_read_it()
    {
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        File.WriteAllText(text, "Hello");
        var json = Path.Combine(dir, "cat.json");
        var output = Path.Combine(dir, "out.txt");
        File.WriteAllText(json, """
        {
          "formatVersion": 3,
          "fileVersion": {"n": 1, "label": "a<b"},
          "pattern": "(Hello)",
          "encoding": "utf-8",
          "used": 0,
          "entries": [
            {
              "source": ["Hello"],
              "replacements": [["Bye"]],
              "sourceByteStart": [0],
              "sourceByteLength": [5]
            }
          ]
        }
        """);
        Assert.Equal(0, Run("putback", "-o", output, json, text).Code);
        Assert.Equal("Bye", File.ReadAllText(output));
        Assert.DoesNotContain("a<b", File.ReadAllText(output), StringComparison.Ordinal);
        var raw = File.ReadAllText(json);
        Assert.Contains("  \"fileVersion\": {\n    \"n\": 1,\n    \"label\": \"a<b\"\n  },\n", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u003c", raw, StringComparison.Ordinal);

        File.WriteAllText(text, "Hello");
        File.WriteAllText(json, """
        {"formatVersion":3,"fileVersion":"","pattern":"(Hello)","encoding":"utf-8","used":0,"entries":[{"source":["Hello"],"replacements":[["Hello"]],"sourceByteStart":[0],"sourceByteLength":[5]}]}
        """);
        Assert.Equal(0, Run("putback", "-o", output, json, text).Code);
        Assert.Contains("\"fileVersion\": \"\"", File.ReadAllText(json), StringComparison.Ordinal);
        Assert.Equal("Hello", File.ReadAllText(output));
    }

    [Fact]
    public void Note_keeps_any_json_value_and_putback_does_not_read_it()
    {
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        File.WriteAllText(text, "Hello");
        var json = Path.Combine(dir, "cat.json");
        var output = Path.Combine(dir, "out.txt");
        File.WriteAllText(json, """
        {
          "formatVersion": 3,
          "note": "メモ",
          "pattern": "(Hello)",
          "encoding": "utf-8",
          "used": 0,
          "entries": [
            {
              "source": ["Hello"],
              "replacements": [["Bye"]],
              "sourceByteStart": [0],
              "sourceByteLength": [5]
            }
          ]
        }
        """);
        Assert.Equal(0, Run("putback", "-o", output, json, text).Code);
        Assert.Equal("Bye", File.ReadAllText(output));
        Assert.Equal("Hello", File.ReadAllText(text));
        var raw = File.ReadAllText(json);
        Assert.Contains("\"note\": \"メモ\"", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("メモ", File.ReadAllText(output), StringComparison.Ordinal);
        Assert.Equal("\"メモ\"", CatalogJson.Parse(raw).Note.Json);
        Assert.Equal("1", CatalogJson.Parse(raw).FileVersion.Json);
        Assert.Contains("\"fileVersion\": 1", raw, StringComparison.Ordinal);

        File.WriteAllText(text, "Hello");
        File.WriteAllText(json, """
        {
          "formatVersion": 3,
          "note": ["tag", "a<b"],
          "pattern": "(Hello)",
          "encoding": "utf-8",
          "used": 0,
          "entries": [
            {
              "source": ["Hello"],
              "replacements": [["Bye"]],
              "sourceByteStart": [0],
              "sourceByteLength": [5]
            }
          ]
        }
        """);
        Assert.Equal(0, Run("putback", "-o", output, json, text).Code);
        raw = File.ReadAllText(json);
        Assert.Contains("  \"note\": [\n    \"tag\",\n    \"a<b\"\n  ],\n", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u003c", raw, StringComparison.Ordinal);
        File.WriteAllText(json, """
        {"formatVersion":3,"note":[],"used":1,"pattern":"(Hello)","encoding":"utf-8","entries":[]}
        """);
        Assert.Equal(0, Run("putback", "-o", output, json, text).Code);
        Assert.Contains("\"note\": []", File.ReadAllText(json), StringComparison.Ordinal);

        File.WriteAllText(json, """
        {"formatVersion":3,"note":null,"used":1,"pattern":"(Hello)","encoding":"utf-8","entries":[]}
        """);
        Assert.Equal(0, Run("putback", "-o", output, json, text).Code);
        Assert.Contains("\"note\": null", File.ReadAllText(json), StringComparison.Ordinal);

        File.WriteAllText(json, """
        {"formatVersion":3,"note":1.50,"used":1,"pattern":"(Hello)","encoding":"utf-8","entries":[]}
        """);
        Assert.Equal(0, Run("putback", "-o", output, json, text).Code);
        Assert.Contains("\"note\": 1.50", File.ReadAllText(json), StringComparison.Ordinal);

        File.WriteAllText(json, """
        {"formatVersion":3,"note":true,"used":1,"pattern":"(Hello)","encoding":"utf-8","entries":[]}
        """);
        Assert.Equal(0, Run("putback", "-o", output, json, text).Code);
        Assert.Contains("\"note\": true", File.ReadAllText(json), StringComparison.Ordinal);

        File.WriteAllText(json, """
        {"formatVersion":3,"note":[1,true,null],"used":1,"pattern":"(Hello)","encoding":"utf-8","entries":[]}
        """);
        Assert.Equal(0, Run("putback", "-o", output, json, text).Code);
        Assert.Contains("  \"note\": [\n    1,\n    true,\n    null\n  ],\n", File.ReadAllText(json), StringComparison.Ordinal);

        File.WriteAllText(json, """
        {"formatVersion":3,"note":{"k":"a<b","n":1,"ok":false,"items":[{}]},"used":1,"pattern":"(Hello)","encoding":"utf-8","entries":[]}
        """);
        Assert.Equal(0, Run("putback", "-o", output, json, text).Code);
        raw = File.ReadAllText(json);
        Assert.Contains("  \"note\": {\n    \"k\": \"a<b\",\n    \"n\": 1,\n    \"ok\": false,\n    \"items\": [\n      {}\n    ]\n  },\n", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u003c", raw, StringComparison.Ordinal);
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
        Assert.Equal(1, Run("putback", "-o", output, json, text).Code);
        Assert.False(File.Exists(output));

        File.WriteAllText(json, """
        {"formatVersion":1,"pattern":"(.*)","encoding":"utf-8","entries":[]}
        """);
        Assert.Equal(1, Run("putback", "-o", output, json, text).Code);

        File.WriteAllText(json, """
        {"formatVersion":3,"pattern":"(.*)","encoding":"utf-8","used":-1,"entries":[]}
        """);
        Assert.Equal(1, Run("putback", "-o", output, json, text).Code);
        Assert.False(File.Exists(output));

        Assert.Equal(2, Run().Code);
        Assert.Equal(2, Run("nope").Code);
        Assert.Equal(0, Run("version").Code);
        Assert.Equal("1.2.0\n", Run("version").Stdout);
        Assert.Equal("", Run("excerpt", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, text).Stdout);
    }

    [Fact]
    public void Number_is_a_zero_based_index_and_negatives_are_rejected()
    {
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        File.WriteAllText(text, "Hello");
        var json = Path.Combine(dir, "cat.json");
        var output = Path.Combine(dir, "out.txt");
        File.WriteAllText(json, """
        {
          "formatVersion": 3,
          "pattern": "(Hello)",
          "encoding": "utf-8",
          "replacementNames": ["first", "second"],
          "used": 0,
          "entries": [
            {
              "source": ["Hello"],
              "replacements": [["ゼロ"], ["イチ"]],
              "sourceByteStart": [0],
              "sourceByteLength": [5]
            }
          ]
        }
        """);

        Assert.Equal(0, Run("putback", "-o", output, json, text).Code);
        Assert.Equal("ゼロ", File.ReadAllText(output));
        Assert.Equal("Hello", File.ReadAllText(text));
        Assert.Equal(0, CatalogJson.Parse(File.ReadAllText(json)).Used);

        File.WriteAllText(text, "Hello");
        Assert.Equal(0, Run("putback", "-number", "0", "-o", output, json, text).Code);
        Assert.Equal("ゼロ", File.ReadAllText(output));

        File.WriteAllText(text, "Hello");
        Assert.Equal(0, Run("putback", "-number", "1", "-o", output, json, text).Code);
        Assert.Equal("イチ", File.ReadAllText(output));
        Assert.Equal(1, CatalogJson.Parse(File.ReadAllText(json)).Used);

        File.WriteAllText(text, "Hello");
        var negative = Run("putback", "-number", "-1", "-o", output, json, text);
        Assert.Equal(1, negative.Code);
        Assert.Contains("0 以上", negative.Stderr);
        Assert.Equal("Hello", File.ReadAllText(text));
        Assert.Equal("イチ", File.ReadAllText(output));

        var negativeEn = Run("putback", "-language", "en", "-number", "-1", "-o", output, json, text);
        Assert.Equal(1, negativeEn.Code);
        Assert.Contains("0 or more", negativeEn.Stderr);
    }

    [Fact]
    public void Source_flag_records_source_in_used()
    {
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        var json = Path.Combine(dir, "cat.json");
        var output = Path.Combine(dir, "out.txt");
        File.WriteAllText(text, "Hello");
        Assert.Equal(0, Run("excerpt", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, text).Code);
        var catalog = CatalogJson.Parse(File.ReadAllText(json));
        catalog.Entries[0].Replacements[0][0] = "Bye";
        catalog.Used = 1;
        File.WriteAllText(json, CatalogJson.ToJson(catalog));

        var both = Run("putback", "-source", "-number", "0", "-o", output, json, text);
        Assert.Equal(2, both.Code);
        Assert.Contains("-number と -source は同時に指定できません。", both.Stderr);
        Assert.Equal("Hello", File.ReadAllText(text));
        Assert.False(File.Exists(output));
        Assert.Equal(1, CatalogJson.Parse(File.ReadAllText(json)).Used);
        Assert.Equal(new[] { "Bye" }, Assert.Single(CatalogJson.Parse(File.ReadAllText(json)).Entries[0].Replacements));

        var english = Run("putback", "-language", "en", "-number", "0", "-source", "-o", output, json, text);
        Assert.Equal(2, english.Code);
        Assert.Contains("-number and -source cannot be used together.", english.Stderr);

        var valued = Run("putback", "-source=Hello", "-o", output, json, text);
        Assert.Equal(2, valued.Code);
        Assert.Contains("-source に値は付けません。", valued.Stderr);

        var twice = Run("putback", "-source", "-source", "-o", output, json, text);
        Assert.Equal(2, twice.Code);
        Assert.Contains("同じ引数が2回あります。", twice.Stderr);

        Assert.Equal(0, Run("putback", "-source", "-o", output, json, text).Code);
        Assert.Equal("Hello", File.ReadAllText(output));
        Assert.Equal("Hello", File.ReadAllText(text));
        var updated = CatalogJson.Parse(File.ReadAllText(json));
        Assert.Equal(UsedValue.Source, updated.Used);
        Assert.Contains("\"used\": \"source\"", File.ReadAllText(json), StringComparison.Ordinal);
        Assert.Equal("exact", updated.Entries[0].Status);
        Assert.Equal(new[] { "Hello" }, updated.Entries[0].Source);
        Assert.Equal(new[] { "Bye" }, Assert.Single(updated.Entries[0].Replacements));

        File.WriteAllText(text, "CHANGED");
        Assert.Equal(0, Run("putback", "-o", output, json, text).Code);
        Assert.Equal("Hello", File.ReadAllText(output));
        Assert.Equal(UsedValue.Source, CatalogJson.Parse(File.ReadAllText(json)).Used);
        Assert.Equal("revised", CatalogJson.Parse(File.ReadAllText(json)).Entries[0].Status);

        File.WriteAllText(text, "Hello");
        Assert.Equal(0, Run("putback", "-number", "0", "-o", output, json, text).Code);
        Assert.Equal("Bye", File.ReadAllText(output));
        Assert.Equal(0, CatalogJson.Parse(File.ReadAllText(json)).Used);

        File.WriteAllText(json, """
        {"formatVersion":3,"pattern":"(.*)","encoding":"utf-8","used":"source","entries":[{"source":["Hello"],"replacements":[["Bye"]]}]}
        """);
        File.WriteAllText(text, "CHANGED");
        Assert.Equal(0, Run("putback", "-o", output, json, text).Code);
        Assert.Equal("Hello", File.ReadAllText(output));
        Assert.Equal(UsedValue.Source, CatalogJson.Parse(File.ReadAllText(json)).Used);

        File.WriteAllText(json, """
        {"formatVersion":3,"pattern":"(.*)","encoding":"utf-8","used":"other","entries":[]}
        """);
        var invalid = Run("putback", "-o", output, json, text);
        Assert.Equal(1, invalid.Code);
        Assert.Contains("used が不正", invalid.Stderr);
        var invalidEn = Run("putback", "-language", "en", "-o", output, json, text);
        Assert.Equal(1, invalidEn.Code);
        Assert.Contains("used is invalid.", invalidEn.Stderr);
    }

    [Fact]
    public void Write_updates_the_input_text_when_output_is_the_same_path()
    {
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        var json = Path.Combine(dir, "cat.json");
        File.WriteAllText(text, "Hello");
        Assert.Equal(0, Run("excerpt", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, text).Code);
        var catalog = CatalogJson.Parse(File.ReadAllText(json));
        catalog.Entries[0].Replacements[0][0] = "Bye";
        File.WriteAllText(json, CatalogJson.ToJson(catalog));

        Assert.Equal(2, Run("putback", json, text).Code);
        Assert.Equal("Hello", File.ReadAllText(text));

        Assert.Equal(0, Run("putback", "-o", text, json, text).Code);
        Assert.Equal("Bye", File.ReadAllText(text));
        Assert.Equal("exact", CatalogJson.Parse(File.ReadAllText(json)).Entries[0].Status);

        File.WriteAllText(text, "Hello");
        Assert.Equal(0, Run("excerpt", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, text).Code);
        catalog = CatalogJson.Parse(File.ReadAllText(json));
        catalog.Entries[0].Replacements[0][0] = "Ciao";
        File.WriteAllText(json, CatalogJson.ToJson(catalog));
        var same = Path.Combine(dir, ".", "in.txt");
        Assert.Equal(0, Run("putback", "-o", same, json, text).Code);
        Assert.Equal("Ciao", File.ReadAllText(text));
    }

    [Fact]
    public void Extract_disable_drops_methods_from_the_new_catalog()
    {
        using var dir = Temp();
        var text = Path.Combine(dir, "in.txt");
        File.WriteAllText(text, "Hello");
        var json = Path.Combine(dir, "out.json");
        Assert.Equal(0, Run("excerpt", "-encoding", "utf-8", "-pattern", "(Hello)", "-disable", "sourceByte,revised", "-o", json, text).Code);
        Assert.Equal(new[] { "key", "exact", "context", "align" }, CatalogJson.Parse(File.ReadAllText(json)).Methods);
    }

    [Fact]
    public void Literal_backslash_n_in_a_target_is_not_a_newline()
    {
        var catalog = Extract("Hello", "(Hello)");
        catalog.Entries[0].Replacements[0][0] = "やあ\\n世界";
        var output = Apply(catalog, "Hello");
        Assert.Equal("やあ\\n世界", Encoding.UTF8.GetString(output.Bytes));
    }

    private static Catalog Extract(string text, string pattern)
    {
        using var dir = Temp();
        var input = Path.Combine(dir, "in.txt");
        File.WriteAllText(input, text);
        var json = Path.Combine(dir, "out.json");
        var result = Run("excerpt", "-encoding", "utf-8", "-pattern", pattern, "-o", json, input);
        Assert.True(result.Code == 0, result.Stderr);
        return CatalogJson.Parse(File.ReadAllText(json));
    }

    private static (int Code, string Stderr) ExtractCode(string text, string pattern)
    {
        using var dir = Temp();
        var input = Path.Combine(dir, "in.txt");
        File.WriteAllText(input, text);
        var result = Run("excerpt", "-encoding", "utf-8", "-pattern", pattern, "-o", Path.Combine(dir, "out.json"), input);
        return (result.Code, result.Stderr);
    }

    private static (byte[] Bytes, Catalog Catalog) Apply(Catalog catalog, string text, HashSet<string>? disabled = null, bool source = false)
    {
        using var dir = Temp();
        var input = Path.Combine(dir, "in.txt");
        File.WriteAllText(input, text);
        var json = Path.Combine(dir, "cat.json");
        File.WriteAllText(json, CatalogJson.ToJson(catalog));
        var output = Path.Combine(dir, "out.txt");
        var args = new List<string> { "putback" };
        if (source)
        {
            args.Add("-source");
        }

        args.Add("-o");
        args.Add(output);
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
        Assert.Contains("Extracted 1 entries.", RunInCulture("en", "excerpt", "-encoding", "utf-8", "-pattern", "(.*)", "-o", json, text).Stderr);
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
