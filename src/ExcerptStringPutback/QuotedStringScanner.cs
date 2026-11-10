namespace ExcerptStringPutback;

public readonly record struct QuotedStringSpan(int Start, int Length);

public static class QuotedStringScanner
{
    public static IReadOnlyList<QuotedStringSpan> Scan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var spans = new List<QuotedStringSpan>();
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] != '"')
            {
                index++;
                continue;
            }

            var start = index;
            index++;
            var closed = false;
            while (index < text.Length)
            {
                if (text[index] == '\\')
                {
                    if (index + 1 >= text.Length)
                    {
                        break;
                    }

                    // The character after a backslash, including a quote, is literal content.
                    index += 2;
                    continue;
                }

                if (text[index] == '"')
                {
                    index++;
                    closed = true;
                    break;
                }

                index++;
            }

            if (!closed)
            {
                throw new ToolException($"引用符が閉じられていません。位置 {start}（0 始まり）");
            }

            spans.Add(new QuotedStringSpan(start, index - start));
        }

        return spans;
    }
}
