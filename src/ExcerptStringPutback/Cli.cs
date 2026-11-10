using System.Globalization;

namespace ExcerptStringPutback;

public static class Cli
{
    private static readonly string[] ValueFlags =
    [
        "-encoding",
        "-pattern",
        "-pattern-file",
        "-o",
        "-disable",
        "-number",
        "-json-out",
    ];

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        Text.Use(Messages.ForCulture(CultureInfo.CurrentUICulture));
        try
        {
            var (language, rest) = SplitLanguage(args);
            if (language is not null)
            {
                Text.Use(Messages.ForOption(language));
            }

            var text = Text.Current;
            if (rest.Length == 0)
            {
                stderr.WriteLine(text.Help);
                return 2;
            }

            switch (rest[0])
            {
                case "version" when rest.Length == 1:
                    stdout.WriteLine(ToolInfo.Version);
                    return 0;
                case "help" or "--help" or "-h" when rest.Length == 1:
                    stdout.WriteLine(text.Help);
                    return 0;
                case "extract":
                    Extract(rest.Skip(1).ToArray(), stderr);
                    return 0;
                case "write":
                    Write(rest.Skip(1).ToArray(), stderr);
                    return 0;
                default:
                    throw new UsageException(text.UnknownCommand);
            }
        }
        catch (UsageException ex)
        {
            stderr.WriteLine(Text.Current.ErrorPrefix + ex.Message);
            stderr.WriteLine(Text.Current.Help);
            return 2;
        }
        catch (ToolException ex)
        {
            stderr.WriteLine(Text.Current.ErrorPrefix + ex.Message);
            return 1;
        }
    }

    private static void Extract(string[] args, TextWriter stderr)
    {
        var text = Text.Current;
        var (flags, positionals) = Parse(args, "-encoding", "-pattern", "-pattern-file", "-o", "-disable");
        if (positionals.Count != 1 || !flags.ContainsKey("-o"))
        {
            throw new UsageException(text.MissingArguments);
        }

        var hasPattern = flags.ContainsKey("-pattern");
        var hasPatternFile = flags.ContainsKey("-pattern-file");
        if (hasPattern == hasPatternFile)
        {
            throw new UsageException(text.PatternChoice);
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
        stderr.WriteLine(text.Format(text.Extracted, catalog.Entries.Count));
    }

    private static void Write(string[] args, TextWriter stderr)
    {
        var text = Text.Current;
        var (flags, positionals) = Parse(args, "-number", "-o", "-json-out", "-disable");
        if (positionals.Count != 2 || !flags.ContainsKey("-o"))
        {
            throw new UsageException(text.MissingArguments);
        }

        var catalogPath = positionals[0];
        var textPath = positionals[1];
        var textOut = flags["-o"];
        var jsonOut = flags.TryGetValue("-json-out", out var jsonPath) ? jsonPath : catalogPath;
        if (IsSamePath(textOut, textPath))
        {
            throw new ToolException(text.KeepOriginalText);
        }

        if (IsSamePath(jsonOut, textOut) || IsSamePath(jsonOut, textPath))
        {
            throw new ToolException(text.JsonPathIsText);
        }

        var catalog = CatalogJson.Read(catalogPath);
        int number;
        if (flags.TryGetValue("-number", out var numberText))
        {
            if (!int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < 1)
            {
                throw new ToolException(text.NumberRange);
            }
        }
        else if (catalog.Used < 1)
        {
            throw new ToolException(text.UsedZero);
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
            ? text.Format(text.Wrote, catalog.Entries.Count)
            : text.Format(text.WroteWithUnmatched, catalog.Entries.Count - unmatched, unmatched));
    }

    private static (string? Language, string[] Remaining) SplitLanguage(string[] args)
    {
        string? language = null;
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                rest.AddRange(args.Skip(i));
                break;
            }

            var name = arg;
            string? inline = null;
            var equals = arg.StartsWith('-') ? arg.IndexOf('=') : -1;
            if (equals > 0)
            {
                name = arg[..equals];
                inline = arg[(equals + 1)..];
            }

            if (name == "-language")
            {
                if (language is not null)
                {
                    throw new UsageException(Text.Current.DuplicateArgument);
                }

                if (inline is not null)
                {
                    language = inline;
                }
                else
                {
                    if (i + 1 >= args.Length)
                    {
                        throw new UsageException(Text.Current.MissingArguments);
                    }

                    language = args[++i];
                }

                continue;
            }

            rest.Add(arg);
            if (inline is null && ValueFlags.Contains(name, StringComparer.Ordinal) && i + 1 < args.Length)
            {
                rest.Add(args[++i]);
            }
        }

        return (language, rest.ToArray());
    }

    private static (Dictionary<string, string> Flags, List<string> Positionals) Parse(string[] args, params string[] allowed)
    {
        var text = Text.Current;
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
                        throw new UsageException(text.MissingArguments);
                    }

                    value = args[++i];
                }

                if (!allowedSet.Contains(name))
                {
                    throw new UsageException(text.UnknownArgument);
                }

                if (!flags.TryAdd(name, value))
                {
                    throw new UsageException(text.DuplicateArgument);
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
