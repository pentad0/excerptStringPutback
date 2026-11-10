using System.Text;

namespace ExcerptStringPutback;

internal static class Engine
{
    public static Catalog Extract(string textPath, string pattern, string? encoding, IReadOnlyList<string> methods, TextWriter stderr)
    {
        var decoded = TextCodec.Read(textPath, encoding, stderr);
        var compiled = Re2Pattern.Compile(pattern);
        var hits = compiled.Find(decoded);
        EnsureNoOverlap(hits);
        var catalog = new Catalog
        {
            Program = ToolInfo.ProgramName,
            FormatVersion = ToolInfo.FormatVersion,
            ToolVersion = ToolInfo.Version,
            FileVersion = "",
            File = StorePath(textPath),
            Encoding = decoded.EncodingName,
            Bom = decoded.Bom,
            Pattern = pattern,
            Keys = compiled.Keys.ToList(),
            Names = new List<string> { "changed" },
            Used = 1,
            Methods = methods.ToList(),
        };
        foreach (var hit in hits)
        {
            catalog.Entries.Add(new Entry
            {
                Key = hit.Key.ToList(),
                Before = hit.Before,
                Source = hit.Source,
                After = hit.After,
                PreviousSource = "",
                Target = new List<string> { hit.Source },
                ByteStart = hit.ByteStart,
                ByteLength = hit.ByteLength,
                Status = "",
                MatchedBy = "",
            });
        }

        return catalog;
    }

    public static byte[] Write(Catalog catalog, string textPath, int number, IReadOnlySet<string> disabled, TextWriter stderr)
    {
        if (string.IsNullOrEmpty(catalog.Pattern))
        {
            throw new ToolException(Text.Current.PatternMissing);
        }

        catalog.Methods = ToolInfo.Order(catalog.Methods);
        var compiled = Re2Pattern.Compile(catalog.Pattern);
        var requested = string.IsNullOrEmpty(catalog.Encoding) ? null : catalog.Encoding;
        var decoded = TextCodec.Read(textPath, requested, stderr);
        var hits = compiled.Find(decoded);
        EnsureNoOverlap(hits);

        var active = catalog.Methods.Where(method => !disabled.Contains(method)).ToHashSet(StringComparer.Ordinal);
        var keyNamesOk = catalog.Keys.All(compiled.KeySet.Contains);
        if (active.Contains("key") && catalog.Keys.Count > 0 && !keyNamesOk)
        {
            stderr.WriteLine(Text.Current.KeyNameMissing);
        }

        var pairs = new Dictionary<int, (int Span, string Method)>();
        var usedSpans = new HashSet<int>();
        void Pair(int entry, int span, string method)
        {
            pairs[entry] = (span, method);
            usedSpans.Add(span);
        }

        if (active.Contains("key") && catalog.Keys.Count > 0 && keyNamesOk)
        {
            PairKey(catalog, compiled, hits, Pair);
        }

        if (active.Contains("offset"))
        {
            PairOffset(catalog, decoded, hits, pairs, usedSpans, Pair);
        }

        if (active.Contains("exact"))
        {
            PairExact(catalog, hits, pairs, usedSpans, Pair);
        }

        if (active.Contains("context"))
        {
            PairContext(catalog, hits, pairs, usedSpans, Pair);
        }

        if (active.Contains("align") || active.Contains("revised"))
        {
            PairAlignRevised(catalog, hits, pairs, usedSpans, active, Pair);
        }

        var replacements = new List<Replacement>();
        var written = new Dictionary<int, (int Span, string Method)>();
        foreach (var (entryIndex, pair) in pairs)
        {
            var entry = catalog.Entries[entryIndex];
            if (number - 1 >= entry.Target.Count)
            {
                continue;
            }

            var target = entry.Target[number - 1];
            if (target.Length == 0)
            {
                continue;
            }

            var hit = hits[pair.Span];
            replacements.Add(new Replacement(hit.ByteStart, hit.ByteLength, decoded.Encoding.GetBytes(ToNfc(target))));
            written[entryIndex] = pair;
        }

        for (var i = 0; i < catalog.Entries.Count; i++)
        {
            var entry = catalog.Entries[i];
            if (!written.TryGetValue(i, out var pair))
            {
                entry.Status = "unmatched";
                entry.MatchedBy = "";
                entry.PreviousSource = "";
                continue;
            }

            var hit = hits[pair.Span];
            var oldSource = entry.Source;
            entry.Before = hit.Before;
            entry.Source = hit.Source;
            entry.After = hit.After;
            entry.ByteStart = hit.ByteStart;
            entry.ByteLength = hit.ByteLength;
            if (keyNamesOk)
            {
                entry.Key = KeyInCatalogOrder(catalog, compiled, hit);
            }

            if (string.Equals(hit.Source, oldSource, StringComparison.Ordinal))
            {
                entry.Status = "exact";
                entry.PreviousSource = "";
            }
            else
            {
                entry.Status = "revised";
                entry.PreviousSource = oldSource;
            }

            entry.MatchedBy = pair.Method;
        }

        catalog.ToolVersion = ToolInfo.Version;
        catalog.Used = number;
        catalog.Encoding = decoded.EncodingName;
        catalog.Bom = decoded.Bom;
        catalog.File = StorePath(textPath);
        return Splice(decoded.Bytes, replacements);
    }

    private static void PairKey(Catalog catalog, Compiled compiled, IReadOnlyList<Hit> hits, Action<int, int, string> pair)
    {
        var entries = Group(catalog.Entries.Select((entry, index) => (index, Key: entry.Key.ToArray()))
            .Where(item => item.Key.Length == catalog.Keys.Count));
        var spans = Group(hits.Select((hit, index) => (index, Key: KeyInCatalogOrder(catalog, compiled, hit).ToArray())));
        foreach (var (key, entryIndexes) in entries)
        {
            if (entryIndexes.Count == 1 && spans.TryGetValue(key, out var spanIndexes) && spanIndexes.Count == 1)
            {
                pair(entryIndexes[0], spanIndexes[0], "key");
            }
        }
    }

    private static List<string> KeyInCatalogOrder(Catalog catalog, Compiled compiled, Hit hit)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = compiled.Keys;
        for (var i = 0; i < names.Count && i < hit.Key.Length; i++)
        {
            map[names[i]] = hit.Key[i];
        }

        return catalog.Keys.Select(name => map.TryGetValue(name, out var value) ? value : "").ToList();
    }

    private static void PairOffset(
        Catalog catalog,
        DecodedText decoded,
        IReadOnlyList<Hit> hits,
        Dictionary<int, (int Span, string Method)> pairs,
        HashSet<int> usedSpans,
        Action<int, int, string> pair)
    {
        var byStart = new Dictionary<int, List<int>>();
        for (var i = 0; i < hits.Count; i++)
        {
            if (!byStart.TryGetValue(hits[i].ByteStart, out var list))
            {
                list = new List<int>();
                byStart[hits[i].ByteStart] = list;
            }

            list.Add(i);
        }

        for (var i = 0; i < catalog.Entries.Count; i++)
        {
            if (pairs.ContainsKey(i))
            {
                continue;
            }

            var entry = catalog.Entries[i];
            if (!byStart.TryGetValue(entry.ByteStart, out var spans) || spans.Count != 1 || usedSpans.Contains(spans[0]))
            {
                continue;
            }

            var hit = hits[spans[0]];
            if (hit.ByteLength != entry.ByteLength || !string.Equals(hit.Source, entry.Source, StringComparison.Ordinal))
            {
                continue;
            }

            if (!BytesMatch(decoded, entry.ByteStart, entry.ByteLength, entry.Source))
            {
                continue;
            }

            pair(i, spans[0], "offset");
        }
    }

    private static bool BytesMatch(DecodedText decoded, int start, int length, string source)
    {
        if (start < 0 || length < 0 || start > decoded.Bytes.Length || start + length > decoded.Bytes.Length)
        {
            return false;
        }

        try
        {
            return string.Equals(decoded.Encoding.GetString(decoded.Bytes, start, length), source, StringComparison.Ordinal);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static void PairExact(
        Catalog catalog,
        IReadOnlyList<Hit> hits,
        Dictionary<int, (int Span, string Method)> pairs,
        HashSet<int> usedSpans,
        Action<int, int, string> pair)
    {
        var entries = GroupFree(catalog.Entries.Select((entry, index) => (index, entry.Source)), pairs.ContainsKey);
        var spans = GroupFree(hits.Select((hit, index) => (index, hit.Source)), usedSpans.Contains);
        foreach (var (source, entryIndexes) in entries)
        {
            if (entryIndexes.Count == 1 && spans.TryGetValue(source, out var spanIndexes) && spanIndexes.Count == 1)
            {
                pair(entryIndexes[0], spanIndexes[0], "exact");
            }
        }
    }

    private static void PairContext(
        Catalog catalog,
        IReadOnlyList<Hit> hits,
        Dictionary<int, (int Span, string Method)> pairs,
        HashSet<int> usedSpans,
        Action<int, int, string> pair)
    {
        var entries = GroupFree(
            catalog.Entries.Select((entry, index) => (index, ContextOf(entry.Before, entry.Source, entry.After))),
            pairs.ContainsKey,
            static context => context.Before.Length > 0 || context.After.Length > 0);
        var spans = GroupFree(
            hits.Select((hit, index) => (index, ContextOf(hit.Before, hit.Source, hit.After))),
            usedSpans.Contains,
            static context => context.Before.Length > 0 || context.After.Length > 0);
        foreach (var (context, entryIndexes) in entries)
        {
            if (entryIndexes.Count == 1 && spans.TryGetValue(context, out var spanIndexes) && spanIndexes.Count == 1)
            {
                pair(entryIndexes[0], spanIndexes[0], "context");
            }
        }
    }

    private static void PairAlignRevised(
        Catalog catalog,
        IReadOnlyList<Hit> hits,
        Dictionary<int, (int Span, string Method)> pairs,
        HashSet<int> usedSpans,
        HashSet<string> active,
        Action<int, int, string> pair)
    {
        var anchors = new List<(int Entry, int Span)> { (-1, -1) };
        var lastSpan = -1;
        foreach (var pairItem in pairs.OrderBy(item => item.Key))
        {
            if (pairItem.Value.Span > lastSpan)
            {
                anchors.Add((pairItem.Key, pairItem.Value.Span));
                lastSpan = pairItem.Value.Span;
            }
        }

        anchors.Add((catalog.Entries.Count, hits.Count));
        for (var anchor = 0; anchor < anchors.Count - 1; anchor++)
        {
            var left = anchors[anchor];
            var right = anchors[anchor + 1];
            var olds = new List<int>();
            for (var i = left.Entry + 1; i < right.Entry; i++)
            {
                if (!pairs.ContainsKey(i))
                {
                    olds.Add(i);
                }
            }

            var news = new List<int>();
            for (var i = left.Span + 1; i < right.Span; i++)
            {
                if (!usedSpans.Contains(i))
                {
                    news.Add(i);
                }
            }

            var aligned = new List<(int Old, int New)>();
            if (active.Contains("align"))
            {
                var lcs = Lcs(
                    olds.Select(index => catalog.Entries[index].Source).ToList(),
                    news.Select(index => hits[index].Source).ToList());
                foreach (var (oldIndex, newIndex) in lcs)
                {
                    pair(olds[oldIndex], news[newIndex], "align");
                    aligned.Add((oldIndex, newIndex));
                }
            }

            if (active.Contains("revised"))
            {
                PairRevised(catalog, olds, news, aligned, pair);
            }
        }
    }

    private static void PairRevised(
        Catalog catalog,
        List<int> olds,
        List<int> news,
        List<(int Old, int New)> aligned,
        Action<int, int, string> pair)
    {
        _ = catalog;
        var inner = new List<(int Old, int New)> { (-1, -1) };
        var last = -1;
        foreach (var item in aligned)
        {
            if (item.New > last)
            {
                inner.Add(item);
                last = item.New;
            }
        }

        inner.Add((olds.Count, news.Count));
        for (var i = 0; i < inner.Count - 1; i++)
        {
            var oldFrom = inner[i].Old + 1;
            var oldTo = inner[i + 1].Old;
            var newFrom = inner[i].New + 1;
            var newTo = inner[i + 1].New;
            var count = oldTo - oldFrom;
            if (count > 0 && count == newTo - newFrom)
            {
                for (var offset = 0; offset < count; offset++)
                {
                    pair(olds[oldFrom + offset], news[newFrom + offset], "revised");
                }
            }
        }
    }

    private static List<(int Old, int New)> Lcs(IReadOnlyList<string> oldSources, IReadOnlyList<string> newSources)
    {
        var rows = oldSources.Count;
        var columns = newSources.Count;
        var scores = new int[rows + 1, columns + 1];
        for (var row = rows - 1; row >= 0; row--)
        {
            for (var column = columns - 1; column >= 0; column--)
            {
                scores[row, column] = string.Equals(oldSources[row], newSources[column], StringComparison.Ordinal)
                    ? scores[row + 1, column + 1] + 1
                    : Math.Max(scores[row + 1, column], scores[row, column + 1]);
            }
        }

        var pairs = new List<(int, int)>();
        var x = 0;
        var y = 0;
        while (x < rows && y < columns)
        {
            if (string.Equals(oldSources[x], newSources[y], StringComparison.Ordinal))
            {
                pairs.Add((x, y));
                x++;
                y++;
            }
            else if (scores[x + 1, y] >= scores[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }

        return pairs;
    }

    private static Dictionary<string[], List<int>> Group(IEnumerable<(int Index, string[] Key)> items)
    {
        var groups = new Dictionary<string[], List<int>>(StringArrayComparer.Instance);
        foreach (var (index, key) in items)
        {
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<int>();
                groups[key] = list;
            }

            list.Add(index);
        }

        return groups;
    }

    private static Dictionary<string, List<int>> GroupFree(IEnumerable<(int Index, string Value)> items, Func<int, bool> used)
    {
        var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var (index, value) in items)
        {
            if (used(index))
            {
                continue;
            }

            if (!groups.TryGetValue(value, out var list))
            {
                list = new List<int>();
                groups[value] = list;
            }

            list.Add(index);
        }

        return groups;
    }

    private static Dictionary<Context, List<int>> GroupFree(
        IEnumerable<(int Index, Context Value)> items,
        Func<int, bool> used,
        Func<Context, bool> include)
    {
        var groups = new Dictionary<Context, List<int>>();
        foreach (var (index, value) in items)
        {
            if (used(index) || !include(value))
            {
                continue;
            }

            if (!groups.TryGetValue(value, out var list))
            {
                list = new List<int>();
                groups[value] = list;
            }

            list.Add(index);
        }

        return groups;
    }

    private static Context ContextOf(string before, string source, string after)
    {
        return new Context(before, source, after);
    }

    private static void EnsureNoOverlap(IReadOnlyList<Hit> hits)
    {
        for (var i = 1; i < hits.Count; i++)
        {
            var previous = hits[i - 1];
            if (previous.CharLength > 0 && hits[i].CharIndex < previous.CharIndex + previous.CharLength)
            {
                throw new ToolException(Text.Current.OverlappingSource);
            }
        }
    }

    private static byte[] Splice(byte[] input, List<Replacement> replacements)
    {
        if (replacements.Count == 0)
        {
            return input;
        }

        replacements.Sort((left, right) => left.Start.CompareTo(right.Start));
        using var stream = new MemoryStream(input.Length);
        var cursor = 0;
        foreach (var replacement in replacements)
        {
            if (replacement.Start < cursor || replacement.Length < 0 || replacement.Start > input.Length || replacement.Start + replacement.Length > input.Length)
            {
                throw new ToolException(Text.Current.OverlappingSource);
            }

            stream.Write(input, cursor, replacement.Start - cursor);
            stream.Write(replacement.Data, 0, replacement.Data.Length);
            cursor = replacement.Start + replacement.Length;
        }

        stream.Write(input, cursor, input.Length - cursor);
        return stream.ToArray();
    }

    private static string ToNfc(string text)
    {
        try
        {
            return text.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            throw new ToolException(Text.Current.CannotNormalize);
        }
    }

    private static string StorePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private readonly record struct Context(string Before, string Source, string After);

    private readonly record struct Replacement(int Start, int Length, byte[] Data);

    private sealed class StringArrayComparer : IEqualityComparer<string[]>
    {
        public static readonly StringArrayComparer Instance = new();

        public bool Equals(string[]? x, string[]? y)
        {
            if (x is null || y is null)
            {
                return x == y;
            }

            if (x.Length != y.Length)
            {
                return false;
            }

            for (var i = 0; i < x.Length; i++)
            {
                if (!string.Equals(x[i], y[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(string[] obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Length);
            foreach (var item in obj)
            {
                hash.Add(item, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }
}
