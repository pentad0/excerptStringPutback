using System.Text.RegularExpressions;

namespace ExcerptStringPutback;

public static class RegexExcerpt
{
    public static IReadOnlyList<ExcerptRecord> Collect(string text, string pattern)
    {
        ArgumentNullException.ThrowIfNull(text);
        var regex = Compile(pattern);
        var records = new List<ExcerptRecord>();
        foreach (Match match in regex.Matches(text))
        {
            if (!match.Success)
            {
                continue;
            }

            if (match.Length == 0)
            {
                throw new ToolException($"空の一致は書き出せません。位置 {match.Index}（0 始まり）");
            }

            var raw = text.Substring(match.Index, match.Length);
            records.Add(new ExcerptRecord(records.Count + 1, match.Index, match.Length, raw, raw));
        }

        return records;
    }

    public static Regex Compile(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            throw new ToolException("正規表現が空です。");
        }

        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(30));
        }
        catch (ArgumentException exception)
        {
            throw new ToolException($"正規表現が不正です: {exception.Message}");
        }
    }
}
