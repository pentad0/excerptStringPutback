namespace ExcerptStringPutback.Tests;

public class ExcerptStringPutbackTests
{
    [Fact]
    public void Excerpt_uses_the_regular_expression()
    {
        const string text = "名前:太郎\n名前:花子";
        var document = ExcerptDocument.Create(@"名前:\S+", text);

        Assert.Equal(2, document.Records.Count);
        Assert.Equal("名前:太郎", document.Records[0].Raw);
        Assert.Equal("名前:花子", document.Records[1].Raw);
        Assert.Equal(document.Records[0].Raw, document.Records[0].Edited);
        Assert.Equal(0, document.Records[0].Start);
        Assert.Equal(5, document.Records[0].Length);
    }

    [Fact]
    public void Tsv_round_trip_keeps_quotes_tabs_and_edits()
    {
        const string text = "say \"a\tb\" and \"c\"";
        const string pattern = "\"[^\"]*\"";
        var document = ExcerptDocument.Create(pattern, text);
        var parsed = ExcerptDocument.Parse(document.ToTsv(pattern));

        Assert.Equal(pattern, parsed.Pattern);
        Assert.Equal(document.Records, parsed.Records);

        var edited = parsed.Records[0] with { Edited = "\"A\tB\"" };
        var records = new[] { edited, parsed.Records[1] };
        var found = RegexExcerpt.Collect(text, pattern);
        var merged = new[] { found[0] with { Edited = records[0].Edited }, found[1] };
        Assert.Equal("say \"A\tB\" and \"c\"", Putback.Apply(text, merged));
    }

    [Fact]
    public void Putback_replaces_matches_from_the_fresh_scan()
    {
        const string text = "名前:太郎\n名前:花子";
        const string pattern = @"名前:\S+";
        var document = ExcerptDocument.Create(pattern, text);
        var edited = document.Records[0] with { Edited = "名前:次郎さん" };
        var found = RegexExcerpt.Collect(text, pattern);
        var merged = new[] { found[0] with { Edited = edited.Edited }, found[1] };

        Assert.Equal("名前:次郎さん\n名前:花子", Putback.Apply(text, merged));
    }

    [Fact]
    public void Parse_rejects_a_bad_header_and_a_gap_in_ids()
    {
        var header = Assert.Throws<ToolException>(() => ExcerptDocument.Parse("id\traw\n"));
        Assert.Contains("見出し", header.Message, StringComparison.Ordinal);

        var gap = Assert.Throws<ToolException>(() => ExcerptDocument.Parse(
            """
            id	start	length	raw	edited
            2	0	2	ab	ab
            """));
        Assert.Contains("id は 1 から順", gap.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_pattern_and_empty_match_are_rejected()
    {
        var empty = Assert.Throws<ToolException>(() => RegexExcerpt.Collect("abc", ""));
        Assert.Contains("空です", empty.Message, StringComparison.Ordinal);

        var zero = Assert.Throws<ToolException>(() => RegexExcerpt.Collect("abc", "x*"));
        Assert.Contains("空の一致", zero.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_excerpt_and_putback_preserve_bom()
    {
        var directory = CreateDirectory();
        try
        {
            var inputPath = Path.Combine(directory, "input.txt");
            var excerptPath = Path.Combine(directory, "excerpt.tsv");
            var outputPath = Path.Combine(directory, "output.txt");
            const string text = "label 雪 tail";
            File.WriteAllBytes(inputPath, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes(text)).ToArray());

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            Assert.Equal(0, Cli.Run(["excerpt", "雪", inputPath, excerptPath], stdout, stderr));
            Assert.Contains("1 件を抜粋しました", stdout.ToString(), StringComparison.Ordinal);

            var excerpt = File.ReadAllText(excerptPath).Replace("雪\t雪", "雪\t霜", StringComparison.Ordinal);
            Assert.NotEqual(File.ReadAllText(excerptPath), excerpt);
            File.WriteAllText(excerptPath, excerpt);

            stdout = new StringWriter();
            Assert.Equal(0, Cli.Run(["putback", "雪", inputPath, excerptPath, outputPath], stdout, stderr));
            var outputBytes = File.ReadAllBytes(outputPath);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, outputBytes.Take(3));
            Assert.Equal("label 霜 tail", System.Text.Encoding.UTF8.GetString(outputBytes[3..]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Cli_rejects_bad_usage_invalid_utf8_and_a_different_pattern()
    {
        var directory = CreateDirectory();
        try
        {
            var inputPath = Path.Combine(directory, "input.txt");
            File.WriteAllBytes(inputPath, [0xFF, 0xFE]);

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            Assert.Equal(0, Cli.Run(["--help"], stdout, stderr));
            Assert.Contains("excerpt <正規表現>", stdout.ToString(), StringComparison.Ordinal);

            stdout = new StringWriter();
            stderr = new StringWriter();
            Assert.Equal(1, Cli.Run([], stdout, stderr));

            stdout = new StringWriter();
            stderr = new StringWriter();
            Assert.Equal(2, Cli.Run(["excerpt", "a", inputPath, Path.Combine(directory, "out.tsv")], stdout, stderr));
            Assert.Contains("UTF-8", stderr.ToString(), StringComparison.Ordinal);

            File.WriteAllText(inputPath, "alpha");
            var excerptPath = Path.Combine(directory, "excerpt.tsv");
            Assert.Equal(0, Cli.Run(["excerpt", "alpha", inputPath, excerptPath], new StringWriter(), new StringWriter()));
            stderr = new StringWriter();
            Assert.Equal(2, Cli.Run(["putback", "beta", inputPath, excerptPath, Path.Combine(directory, "out.txt")], new StringWriter(), stderr));
            Assert.Contains("正規表現が違います", stderr.ToString(), StringComparison.Ordinal);

            stderr = new StringWriter();
            Assert.Equal(2, Cli.Run(["excerpt", "(", inputPath, Path.Combine(directory, "bad.tsv")], new StringWriter(), stderr));
            Assert.Contains("正規表現が不正", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Putback_can_clear_a_match_in_place()
    {
        var directory = CreateDirectory();
        try
        {
            var inputPath = Path.Combine(directory, "input.txt");
            var excerptPath = Path.Combine(directory, "excerpt.tsv");
            File.WriteAllText(inputPath, "A one B two");
            Assert.Equal(0, Cli.Run(["excerpt", "one|two", inputPath, excerptPath], new StringWriter(), new StringWriter()));
            var updated = File.ReadAllText(excerptPath).Replace("one\tone", "one\t", StringComparison.Ordinal);
            File.WriteAllText(excerptPath, updated);

            var stderr = new StringWriter();
            Assert.Equal(0, Cli.Run(["putback", "one|two", inputPath, excerptPath, inputPath], new StringWriter(), stderr));
            Assert.Equal("", stderr.ToString());
            Assert.Equal("A  B two", File.ReadAllText(inputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "excerpt-string-putback-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
