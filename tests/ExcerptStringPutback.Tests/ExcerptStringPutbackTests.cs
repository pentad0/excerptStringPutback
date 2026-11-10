namespace ExcerptStringPutback.Tests;

public class ExcerptStringPutbackTests
{
    [Fact]
    public void Excerpt_finds_escaped_quotes_and_japanese()
    {
        const string text = "var s = \"say \\\"hi\\\"\";\r\n{\"name\":\"太郎\"}";
        var document = ExcerptDocument.Create("sample.txt", text);

        Assert.Equal(3, document.Records.Count);
        Assert.Equal("\"say \\\"hi\\\"\"", document.Records[0].Raw);
        Assert.Equal(document.Records[0].Raw, document.Records[0].Edited);
        Assert.Equal("\"name\"", document.Records[1].Raw);
        Assert.Equal("\"太郎\"", document.Records[2].Raw);
        Assert.Equal(text.Substring(document.Records[0].Start, document.Records[0].Length), document.Records[0].Raw);
        Assert.Equal(text.Substring(document.Records[2].Start, document.Records[2].Length), document.Records[2].Raw);
    }

    [Fact]
    public void Putback_round_trip_keeps_text_and_edits_only_the_selected_span()
    {
        const string text = "prefix \"a\"\r\n suffix \"b\"";
        var document = ExcerptDocument.Create("in.txt", text);
        var unchanged = Putback.Apply(text, document.Records);
        Assert.Equal(text, unchanged);

        var edited = document.Records[0] with { Edited = "\"ABCD\"" };
        var records = new[] { edited, document.Records[1] };
        var replaced = Putback.Apply(text, records);

        Assert.Equal("prefix \"ABCD\"\r\n suffix \"b\"", replaced);
        Assert.Equal("\"b\"", replaced.Substring(document.Records[1].Start + ("ABCD".Length - "a".Length), document.Records[1].Length));
    }

    [Fact]
    public void Putback_rejects_a_span_that_no_longer_matches()
    {
        var document = ExcerptDocument.Create("in.txt", "\"ab\" \"cd\"");
        var error = Assert.Throws<ToolException>(() => Putback.Apply("\"xx\" \"cd\"", document.Records));
        Assert.Contains("一致しません", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Putback_rejects_overlapping_spans()
    {
        var records = new[]
        {
            new ExcerptRecord(1, 0, 4, "\"ab\"", "\"ab\""),
            new ExcerptRecord(2, 2, 4, "b\" \"", "b\" \""),
        };

        var error = Assert.Throws<ToolException>(() => Putback.Apply("\"ab\" \"cd\"", records));
        Assert.Contains("重な", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scanner_rejects_an_unterminated_string()
    {
        var error = Assert.Throws<ToolException>(() => ExcerptDocument.Create("in.txt", "before \"abc"));
        Assert.Contains("閉じられていません", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_lines_round_trip()
    {
        const string text = "{\"note\":\"say \\\"hi\\\"\"}\n";
        var document = ExcerptDocument.Create("game.json", text);
        var parsed = ExcerptDocument.Parse(document.ToJsonLines());

        Assert.Equal("game.json", parsed.Source);
        Assert.Equal(document.Records, parsed.Records);
        Assert.Equal(text, Putback.Apply(text, parsed.Records));
    }

    [Fact]
    public void Parse_rejects_a_missing_id_and_a_late_meta_line()
    {
        var gap = Assert.Throws<ToolException>(() => ExcerptDocument.Parse(
            """
            {"id":2,"start":0,"length":2,"raw":"ab","edited":"ab"}

            """));
        Assert.Contains("id は 1 から順", gap.Message, StringComparison.Ordinal);

        var lateMeta = Assert.Throws<ToolException>(() => ExcerptDocument.Parse(
            """
            {"id":1,"start":0,"length":2,"raw":"ab","edited":"ab"}
            {"kind":"meta","tool":"excerptStringPutback","encoding":"utf-8","source":"a"}
            """));
        Assert.Contains("先頭", lateMeta.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_excerpt_and_putback_preserve_bom_and_report_counts()
    {
        var directory = CreateDirectory();
        var inputPath = Path.Combine(directory, "input.txt");
        var excerptPath = Path.Combine(directory, "excerpt.jsonl");
        var outputPath = Path.Combine(directory, "output.txt");
        var text = "label \"雪\" tail";
        File.WriteAllBytes(inputPath, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes(text)).ToArray());

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var excerptCode = Cli.Run(["excerpt", inputPath, excerptPath], stdout, stderr);
        Assert.Equal(0, excerptCode);
        Assert.Contains("1 件の文字列を抜粋しました", stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal("", stderr.ToString());

        var excerpt = File.ReadAllText(excerptPath);
        excerpt = excerpt.Replace("\"edited\":\"\\\"雪\\\"\"", "\"edited\":\"\\\"霜\\\"\"", StringComparison.Ordinal);
        File.WriteAllText(excerptPath, excerpt);

        stdout = new StringWriter();
        var putbackCode = Cli.Run(["putback", inputPath, excerptPath, outputPath], stdout, stderr);
        Assert.Equal(0, putbackCode);
        var outputBytes = File.ReadAllBytes(outputPath);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, outputBytes.Take(3));
        Assert.Equal("label \"霜\" tail", System.Text.Encoding.UTF8.GetString(outputBytes[3..]));
    }

    [Fact]
    public void Cli_rejects_bad_usage_invalid_utf8_and_same_paths()
    {
        var directory = CreateDirectory();
        var inputPath = Path.Combine(directory, "input.txt");
        File.WriteAllBytes(inputPath, [0xFF, 0xFE]);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Assert.Equal(0, Cli.Run(["--help"], stdout, stderr));
        Assert.Contains("excerptStringPutback excerpt", stdout.ToString(), StringComparison.Ordinal);

        stdout = new StringWriter();
        stderr = new StringWriter();
        Assert.Equal(1, Cli.Run([], stdout, stderr));
        Assert.Contains("使い方", stderr.ToString(), StringComparison.Ordinal);

        stdout = new StringWriter();
        stderr = new StringWriter();
        Assert.Equal(2, Cli.Run(["excerpt", inputPath, Path.Combine(directory, "out.jsonl")], stdout, stderr));
        Assert.Contains("UTF-8", stderr.ToString(), StringComparison.Ordinal);

        File.WriteAllText(inputPath, "\"a\"");
        stdout = new StringWriter();
        stderr = new StringWriter();
        Assert.Equal(2, Cli.Run(["excerpt", inputPath, inputPath], stdout, stderr));
        Assert.Contains("別のパス", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Putback_can_remove_a_span_and_replace_the_input_file()
    {
        var directory = CreateDirectory();
        try
        {
            var inputPath = Path.Combine(directory, "input.txt");
            var excerptPath = Path.Combine(directory, "excerpt.jsonl");
            File.WriteAllText(inputPath, "A \"one\" B \"two\"");
            Assert.Equal(0, Cli.Run(["excerpt", inputPath, excerptPath], new StringWriter(), new StringWriter()));

            var json = File.ReadAllText(excerptPath);
            var updated = json.Replace("\"edited\":\"\\\"one\\\"\"", "\"edited\":\"\"", StringComparison.Ordinal);
            Assert.NotEqual(json, updated);
            File.WriteAllText(excerptPath, updated);

            var stderr = new StringWriter();
            Assert.Equal(0, Cli.Run(["putback", inputPath, excerptPath, inputPath], new StringWriter(), stderr));
            Assert.Equal("", stderr.ToString());
            Assert.Equal("A  B \"two\"", File.ReadAllText(inputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Empty_input_excerpts_nothing_and_putback_copies_it()
    {
        var document = ExcerptDocument.Create("empty.txt", "");
        Assert.Empty(document.Records);
        Assert.Equal("plain text", Putback.Apply("plain text", document.Records));
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "excerpt-string-putback-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
