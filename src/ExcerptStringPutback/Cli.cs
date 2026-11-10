using System.Globalization;

namespace ExcerptStringPutback;

public static class Cli
{
    public const string Help =
        """
        使い方:
          excerptStringPutback extract -encoding <符号化> -pattern <正規表現> -o <出力.json> <テキストファイル>
          excerptStringPutback extract -encoding <符号化> -pattern-file <ファイル> -o <出力.json> <テキストファイル>
          excerptStringPutback write -number <番号> -o <出力ファイル> [-json-out <JSON>] [-disable <方式>] <カタログ.json> <テキストファイル>
          excerptStringPutback version
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        try
        {
            if (args.Length == 0)
            {
                stderr.WriteLine(Help);
                return 2;
            }

            switch (args[0])
            {
                case "version" when args.Length == 1:
                    stdout.WriteLine(ToolInfo.Version);
                    return 0;
                case "help" or "--help" or "-h" when args.Length == 1:
                    stdout.WriteLine(Help);
                    return 0;
                case "extract":
                    Extract(args.Skip(1).ToArray(), stderr);
                    return 0;
                case "write":
                    Write(args.Skip(1).ToArray(), stderr);
                    return 0;
                default:
                    throw new UsageException("未知のコマンドです。");
            }
        }
        catch (UsageException ex)
        {
            stderr.WriteLine("エラー: " + ex.Message);
            stderr.WriteLine(Help);
            return 2;
        }
        catch (ToolException ex)
        {
            stderr.WriteLine("エラー: " + ex.Message);
            return 1;
        }
    }

    private static void Extract(string[] args, TextWriter stderr)
    {
        var (flags, positionals) = Parse(args, "-encoding", "-pattern", "-pattern-file", "-o", "-disable");
        if (positionals.Count != 1 || !flags.ContainsKey("-o"))
        {
            throw new UsageException("引数が足りません。");
        }

        var hasPattern = flags.ContainsKey("-pattern");
        var hasPatternFile = flags.ContainsKey("-pattern-file");
        if (hasPattern == hasPatternFile)
        {
            throw new UsageException("-pattern と -pattern-file はどちらか一方を指定します。");
        }

        var pattern = hasPattern ? flags["-pattern"] : TextCodec.ReadPatternFile(flags["-pattern-file"]);
        var methods = ToolInfo.Methods.ToList();
        if (flags.TryGetValue("-disable", out var disabled))
        {
            var off = ToolInfo.ParseDisable(disabled);
            methods = methods.Where(method => !off.Contains(method)).ToList();
        }

        flags.TryGetValue("-encoding", out var encoding);
        var catalog = Engine.Extract(positionals[0], pattern, encoding, methods, stderr);
        CatalogJson.Write(flags["-o"], catalog);
        stderr.WriteLine($"{catalog.Entries.Count} 件を書き出しました。");
    }

    private static void Write(string[] args, TextWriter stderr)
    {
        var (flags, positionals) = Parse(args, "-number", "-o", "-json-out", "-disable");
        if (positionals.Count != 2 || !flags.ContainsKey("-o"))
        {
            throw new UsageException("引数が足りません。");
        }

        var catalogPath = positionals[0];
        var textPath = positionals[1];
        var textOut = flags["-o"];
        var jsonOut = flags.TryGetValue("-json-out", out var jsonPath) ? jsonPath : catalogPath;
        if (IsSamePath(textOut, textPath))
        {
            throw new ToolException("元のテキストファイルは変更しません。別の出力先を指定してください。");
        }

        if (IsSamePath(jsonOut, textOut) || IsSamePath(jsonOut, textPath))
        {
            throw new ToolException("JSON の出力先がテキストファイルと同じです。");
        }

        var catalog = CatalogJson.Read(catalogPath);
        int number;
        if (flags.TryGetValue("-number", out var numberText))
        {
            if (!int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < 1)
            {
                throw new ToolException("-number は 1 以上の整数です。");
            }
        }
        else if (catalog.Used < 1)
        {
            throw new ToolException("-number が無く、used が 0 です。");
        }
        else
        {
            number = catalog.Used;
        }

        var disabled = flags.TryGetValue("-disable", out var disabledText)
            ? ToolInfo.ParseDisable(disabledText)
            : new HashSet<string>(StringComparer.Ordinal);
        var output = Engine.Write(catalog, textPath, number, disabled, stderr);
        TextCodec.WriteBytes(textOut, output);
        CatalogJson.Write(jsonOut, catalog);
        var unmatched = catalog.Entries.Count(entry => entry.Status == "unmatched");
        stderr.WriteLine(unmatched == 0
            ? $"{catalog.Entries.Count - unmatched} 件を書き戻しました。"
            : $"{catalog.Entries.Count - unmatched} 件を書き戻しました。対応しなかった項目は {unmatched} 件です。");
    }

    private static (Dictionary<string, string> Flags, List<string> Positionals) Parse(string[] args, params string[] allowed)
    {
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        var rest = new List<string>();
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                rest.AddRange(args.Skip(i + 1));
                break;
            }

            if (arg.StartsWith('-') && arg.Length > 1)
            {
                var name = arg;
                string value;
                var equals = arg.IndexOf('=');
                if (equals > 0)
                {
                    name = arg[..equals];
                    value = arg[(equals + 1)..];
                }
                else
                {
                    if (i + 1 >= args.Length)
                    {
                        throw new UsageException("引数が足りません。");
                    }

                    value = args[++i];
                }

                if (!allowedSet.Contains(name))
                {
                    throw new UsageException("未知の引数です。");
                }

                if (!flags.TryAdd(name, value))
                {
                    throw new UsageException("同じ引数が2回あります。");
                }

                continue;
            }

            rest.Add(arg);
        }

        return (flags, rest);
    }

    private static bool IsSamePath(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }
}
