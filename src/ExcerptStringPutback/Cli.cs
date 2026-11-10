namespace ExcerptStringPutback;

public static class Cli
{
    public const string Help =
        """
        使い方:
          excerptStringPutback excerpt <正規表現> <入力ファイル> <抜粋ファイル>
          excerptStringPutback putback <正規表現> <入力ファイル> <抜粋ファイル> <出力ファイル>

        excerpt  正規表現に一致した部分を TSV へ書き出す。
        putback  TSV の edited を、同じ正規表現の一致位置へ書き戻す。
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h" || args[0] == "help"))
        {
            stdout.WriteLine(Help);
            return 0;
        }

        if (args.Length == 0)
        {
            stderr.WriteLine(Help);
            return 1;
        }

        try
        {
            switch (args[0])
            {
                case "excerpt" when args.Length == 4:
                    Excerpt(args[1], args[2], args[3], stdout);
                    return 0;
                case "putback" when args.Length == 5:
                    PutBack(args[1], args[2], args[3], args[4], stdout);
                    return 0;
                default:
                    stderr.WriteLine(Help);
                    return 1;
            }
        }
        catch (ToolException exception)
        {
            stderr.WriteLine($"エラー: {exception.Message}");
            return 2;
        }
    }

    private static void Excerpt(string pattern, string inputPath, string excerptPath, TextWriter stdout)
    {
        if (IsSamePath(inputPath, excerptPath))
        {
            throw new ToolException("抜粋ファイルは入力ファイルと別のパスにしてください。");
        }

        var input = TextFile.Read(inputPath);
        var document = ExcerptDocument.Create(pattern, input.Text);
        TextFile.Write(excerptPath, new TextFile(document.ToTsv(pattern), HasUtf8Bom: false));
        stdout.WriteLine($"{document.Records.Count} 件を抜粋しました: {excerptPath}");
    }

    private static void PutBack(string pattern, string inputPath, string excerptPath, string outputPath, TextWriter stdout)
    {
        if (IsSamePath(excerptPath, outputPath))
        {
            throw new ToolException("出力ファイルは抜粋ファイルと別のパスにしてください。");
        }

        var input = TextFile.Read(inputPath);
        var excerpt = TextFile.Read(excerptPath);
        var document = ExcerptDocument.Parse(excerpt.Text);
        if (document.Pattern is not null && document.Pattern != pattern)
        {
            throw new ToolException("抜粋ファイルの正規表現と指定した正規表現が違います。");
        }

        var found = RegexExcerpt.Collect(input.Text, pattern);
        if (found.Count != document.Records.Count)
        {
            throw new ToolException($"正規表現の一致は {found.Count} 件ですが、抜粋ファイルは {document.Records.Count} 件です。");
        }

        var merged = new List<ExcerptRecord>(found.Count);
        for (var index = 0; index < found.Count; index++)
        {
            var record = document.Records[index];
            if (found[index].Raw != record.Raw)
            {
                throw new ToolException($"文字列 {record.Id} の raw が、現在の正規表現の一致と違います。");
            }

            merged.Add(found[index] with { Edited = record.Edited });
        }

        var replaced = Putback.Apply(input.Text, merged);
        TextFile.Write(outputPath, new TextFile(replaced, input.HasUtf8Bom));
        stdout.WriteLine($"{document.Records.Count} 件を書き戻しました: {outputPath}");
    }

    private static bool IsSamePath(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
