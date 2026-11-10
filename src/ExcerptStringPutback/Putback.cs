using System.Text;

namespace ExcerptStringPutback;

public static class Putback
{
    public static string Apply(string text, IReadOnlyList<ExcerptRecord> records)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return text;
        }

        var ordered = records.OrderBy(record => record.Start).ThenBy(record => record.Id).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var record = ordered[index];
            if (record.Length < 1 || record.Length != record.Raw.Length)
            {
                throw new ToolException($"文字列 {record.Id} の length が raw の長さと一致しません。");
            }

            if (record.Start < 0 || (long)record.Start + record.Length > text.Length)
            {
                throw new ToolException($"文字列 {record.Id} の範囲がテキストの外です。");
            }

            if (text.Substring(record.Start, record.Length) != record.Raw)
            {
                throw new ToolException($"文字列 {record.Id} の raw が入力ファイルの該当範囲と一致しません。");
            }

            if (index > 0)
            {
                var previous = ordered[index - 1];
                if ((long)previous.Start + previous.Length > record.Start)
                {
                    throw new ToolException($"文字列 {previous.Id} と {record.Id} の範囲が重なっています。");
                }
            }
        }

        var builder = new StringBuilder(text);
        foreach (var record in ordered.OrderByDescending(record => record.Start).ThenByDescending(record => record.Id))
        {
            builder.Remove(record.Start, record.Length);
            builder.Insert(record.Start, record.Edited);
        }

        return builder.ToString();
    }
}
