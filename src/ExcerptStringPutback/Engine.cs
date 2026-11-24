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
            Note = new NoteValue(),
            File = StorePath(textPath),
            Encoding = decoded.EncodingName,
            Bom = decoded.Bom,
            Pattern = pattern,
            KeyNames = compiled.Keys.ToList(),
            SourceNames = SourceNames(compiled),
            ReplacementNames = new List<string> { "changed" },
            Used = 0,
            Methods = methods.ToList(),
        };
        foreach (var hit in hits)
        {
            catalog.Entries.Add(new Entry
            {
                Key = hit.Key.ToList(),
                Before = hit.Before.ToList(),
                Source = hit.Source.ToList(),
                After = hit.After.ToList(),
                Replacements = [hit.Source.ToList()],
                ByteStart = hit.ByteStart.ToList(),
                ByteLength = hit.ByteLength.ToList(),
                Status = "",
                MatchedBy = "",
            });
        }

        return catalog;
    }

    public static byte[] Write(Catalog catalog, string textPath, int number, IReadOnlySet<string> disabled, TextWriter stderr, bool writeSource)
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
        var keyNamesOk = catalog.KeyNames.All(compiled.KeySet.Contains);
        if (active.Contains("key") && catalog.KeyNames.Count > 0 && !keyNamesOk)
        {
            stderr.WriteLine(Text.Current.KeyNameMissing);
        }

        if (!SourceNamesMatch(catalog, compiled))
        {
            stderr.WriteLine(Text.Current.SourceNameMissing);
        }

        var pairs = new Dictionary<int, (int Span, string Method)>();
        var usedSpans = new HashSet<int>();
        void Pair(int entry, int span, string method)
        {
            pairs[entry] = (span, method);
            usedSpans.Add(span);
        }

        if (active.Contains("key") && catalog.KeyNames.Count > 0 && keyNamesOk)
        {
            PairKey(catalog, compiled, hits, Pair);
        }

        if (active.Contains("sourceByte"))
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
            IReadOnlyList<string> row;
            if (writeSource)
            {
                row = entry.Source;
            }
            else
            {
                if (number < 0 || number >= entry.Replacements.Count)
                {
                    continue;
                }

                row = entry.Replacements[number];
            }

            var hit = hits[pair.Span];
            var count = Math.Min(row.Count, hit.ByteStart.Length);
            for (var slot = 0; slot < count; slot++)
            {
                if (hit.ByteStart[slot] < 0)
                {
                    continue;
                }

                var text = writeSource ? row[slot] : ToNfc(row[slot]);
                replacements.Add(new Replacement(hit.ByteStart[slot], hit.ByteLength[slot], decoded.Encoding.GetBytes(text), slot));
            }
            written[entryIndex] = pair;
        }

        for (var i = 0; i < catalog.Entries.Count; i++)
        {
            var entry = catalog.Entries[i];
            if (!written.TryGetValue(i, out var pair))
            {
                entry.Status = "unmatched";
                entry.MatchedBy = "";
                continue;
            }

            var hit = hits[pair.Span];
            entry.Status = Same(entry.Source, hit.Source) ? "exact" : "revised";
            entry.MatchedBy = pair.Method;
        }

        catalog.Used = writeSource ? UsedValue.Source : number;

        return Splice(decoded.Bytes, replacements);
    }

    private static void PairKey(Catalog catalog, Compiled compiled, IReadOnlyList<Hit> hits, Action<int, int, string> pair)
    {
        var entries = Group(catalog.Entries.Select((entry, index) => (index, Key: entry.Key.ToArray()))
            .Where(item => item.Key.Length == catalog.KeyNames.Count));
        var spans = Group(hits.Select((hit, index) => (index, Key: KeyInCatalogOrder(catalog, compiled, hit).ToArray())));
        foreach (var (key, entryIndexes) in entries)
        {
            if (entryIndexes.Count == 1 && spans.TryGetValue(key, out var spanIndexes) && spanIndexes.Count == 1)
            {
                pair(entryIndexes[0], spanIndexes[0], "key");
            }
        }
    }

    private static List<string> SourceNames(Compiled compiled)
    {
        return compiled.Sources.Count == 0 ? [""] : compiled.Sources.ToList();
    }

    private static bool SourceNamesMatch(Catalog catalog, Compiled compiled)
    {
        var expected = SourceNames(compiled);
        if (catalog.SourceNames.Count == 0)
        {
            return true;
        }

        if (catalog.SourceNames.Count != expected.Count)
        {
            return false;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            if (!string.Equals(catalog.SourceNames[i], expected[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Same(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> KeyInCatalogOrder(Catalog catalog, Compiled compiled, Hit hit)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = compiled.Keys;
        for (var i = 0; i < names.Count && i < hit.Key.Length; i++)
        {
            map[names[i]] = hit.Key[i];
        }

        return catalog.KeyNames.Select(name => map.TryGetValue(name, out var value) ? value : "").ToList();
    }

    private static void PairOffset(
        Catalog catalog,
        DecodedText decoded,
        IReadOnlyList<Hit> hits,
        Dictionary<int, (int Span, string Method)> pairs,
        HashSet<int> usedSpans,
        Action<int, int, string> pair)
    {
        for (var i = 0; i < catalog.Entries.Count; i++)
        {
            if (pairs.ContainsKey(i))
            {
                continue;
            }

            var entry = catalog.Entries[i];
            var matched = -1;
            var count = 0;
            for (var span = 0; span < hits.Count; span++)
            {
                if (usedSpans.Contains(span) || !SlotsMatch(decoded, entry, hits[span]))
                {
                    continue;
                }

                matched = span;
                count++;
            }

            if (count == 1)
            {
                pair(i, matched, "sourceByte");
            }
        }
    }

    private static bool SlotsMatch(DecodedText decoded, Entry entry, Hit hit)
    {
        if (entry.ByteStart.Count != hit.ByteStart.Length || entry.ByteLength.Count != hit.ByteLength.Length || entry.Source.Count != hit.Source.Length)
        {
            return false;
        }

        for (var i = 0; i < hit.ByteStart.Length; i++)
        {
            if (entry.ByteStart[i] < 0 || hit.ByteStart[i] < 0)
            {
                if (entry.ByteStart[i] >= 0 || hit.ByteStart[i] >= 0)
                {
                    return false;
                }

                continue;
            }

            if (entry.ByteStart[i] != hit.ByteStart[i] || entry.ByteLength[i] != hit.ByteLength[i] || !string.Equals(entry.Source[i], hit.Source[i], StringComparison.Ordinal))
            {
                return false;
            }

            if (!BytesMatch(decoded, entry.ByteStart[i], entry.ByteLength[i], entry.Source[i]))
            {
                return false;
            }
        }

        return true;
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
        var entries = GroupFree(catalog.Entries.Select((entry, index) => (index, entry.Source.ToArray())), pairs.ContainsKey);
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
            static context => HasContext(context));
        var spans = GroupFree(
            hits.Select((hit, index) => (index, ContextOf(hit.Before, hit.Source, hit.After))),
            usedSpans.Contains,
            static context => HasContext(context));
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
                    olds.Select(index => catalog.Entries[index].Source.ToArray()).ToList(),
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

    private static List<(int Old, int New)> Lcs(IReadOnlyList<string[]> oldSources, IReadOnlyList<string[]> newSources)
    {
        var rows = oldSources.Count;
        var columns = newSources.Count;
        var scores = new int[rows + 1, columns + 1];
        for (var row = rows - 1; row >= 0; row--)
        {
            for (var column = columns - 1; column >= 0; column--)
            {
                scores[row, column] = Same(oldSources[row], newSources[column])
                    ? scores[row + 1, column + 1] + 1
                    : Math.Max(scores[row + 1, column], scores[row, column + 1]);
            }
        }

        var pairs = new List<(int, int)>();
        var x = 0;
        var y = 0;
        while (x < rows && y < columns)
        {
            if (Same(oldSources[x], newSources[y]))
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

    private static Dictionary<string[], List<int>> GroupFree(IEnumerable<(int Index, string[] Value)> items, Func<int, bool> used)
    {
        var groups = new Dictionary<string[], List<int>>(StringArrayComparer.Instance);
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
        var groups = new Dictionary<Context, List<int>>(ContextComparer.Instance);
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

    private static Context ContextOf(IReadOnlyList<string> before, IReadOnlyList<string> source, IReadOnlyList<string> after)
    {
        return new Context(before.ToArray(), source.ToArray(), after.ToArray());
    }

    private static bool HasContext(Context context)
    {
        return context.Before.Any(text => text.Length > 0) || context.After.Any(text => text.Length > 0);
    }

    private static void EnsureNoOverlap(IReadOnlyList<Hit> hits)
    {
        var spans = new List<(int Index, int Length)>();
        foreach (var hit in hits)
        {
            for (var i = 0; i < hit.CharIndex.Length; i++)
            {
                if (hit.CharIndex[i] >= 0)
                {
                    spans.Add((hit.CharIndex[i], hit.CharLength[i]));
                }
            }
        }

        spans.Sort(static (left, right) =>
        {
            var byIndex = left.Index.CompareTo(right.Index);
            return byIndex != 0 ? byIndex : left.Length.CompareTo(right.Length);
        });
        for (var i = 1; i < spans.Count; i++)
        {
            var previous = spans[i - 1];
            if (spans[i].Index < previous.Index + previous.Length && previous.Index < spans[i].Index + spans[i].Length)
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

        replacements.Sort((left, right) =>
        {
            var byStart = left.Start.CompareTo(right.Start);
            if (byStart != 0)
            {
                return byStart;
            }

            var byLength = left.Length.CompareTo(right.Length);
            return byLength != 0 ? byLength : left.Order.CompareTo(right.Order);
        });
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

    private readonly record struct Context(string[] Before, string[] Source, string[] After);

    private sealed class ContextComparer : IEqualityComparer<Context>
    {
        public static readonly ContextComparer Instance = new();

        public bool Equals(Context x, Context y)
        {
            return Same(x.Before, y.Before) && Same(x.Source, y.Source) && Same(x.After, y.After);
        }

        public int GetHashCode(Context obj)
        {
            var hash = new HashCode();
            hash.Add(StringArrayComparer.Instance.GetHashCode(obj.Before));
            hash.Add(StringArrayComparer.Instance.GetHashCode(obj.Source));
            hash.Add(StringArrayComparer.Instance.GetHashCode(obj.After));
            return hash.ToHashCode();
        }
    }

    private readonly record struct Replacement(int Start, int Length, byte[] Data, int Order);

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
