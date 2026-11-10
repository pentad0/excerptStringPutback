namespace ExcerptStringPutback;

public static class Cli
{
    public const string Help =
        """
        使い方:
          excerptStringPutback excerpt <入力ファイル> <抜粋ファイル>
          excerptStringPutback putback <入力ファイル> <抜粋ファイル> <出力ファイル>

        excerpt  入力ファイル中の二重引用符で囲まれた文字列を抜粋ファイルへ書き出す。
        putback  抜粋ファイルの edited を、元の位置へ書き戻す。
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
                case "excerpt" when args.Length == 3:
                    Excerpt(args[1], args[2], stdout);
                    return 0;
                case "putback" when args.Length == 4:
                    PutBack(args[1], args[2], args[3], stdout);
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

    private static void Excerpt(string inputPath, string excerptPath, TextWriter stdout)
    {
        if (IsSamePath(inputPath, excerptPath))
        {
            throw new ToolException("抜粋ファイルは入力ファイルと別のパスにしてください。");
        }

        var input = TextFile.Read(inputPath);
        var document = ExcerptDocument.Create(inputPath, input.Text);
        TextFile.Write(excerptPath, new TextFile(document.ToJsonLines(), HasUtf8Bom: false));
        stdout.WriteLine($"{document.Records.Count} 件の文字列を抜粋しました: {excerptPath}");
    }

    private static void PutBack(string inputPath, string excerptPath, string outputPath, TextWriter stdout)
    {
        if (IsSamePath(excerptPath, outputPath))
        {
            throw new ToolException("出力ファイルは抜粋ファイルと別のパスにしてください。");
        }

        var input = TextFile.Read(inputPath);
        var excerpt = TextFile.Read(excerptPath);
        var document = ExcerptDocument.Parse(excerpt.Text);
        var replaced = Putback.Apply(input.Text, document.Records);
        TextFile.Write(outputPath, new TextFile(replaced, input.HasUtf8Bom));
        stdout.WriteLine($"{document.Records.Count} 件の文字列を書き戻しました: {outputPath}");
    }

    private static bool IsSamePath(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
