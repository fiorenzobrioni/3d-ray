using System.Globalization;
using System.Text;
using YamlDotNet.RepresentationModel;

namespace SceneMigrate;

/// <summary>
/// Text edits on a YAML file located through its parsed node tree, so that
/// everything not edited (comments, layout, number formatting) is kept as
/// written. Edits never overlap; two insertions at the same place keep the
/// order they were made in.
/// </summary>
public sealed class YamlEdits(string text)
{
    public string Text { get; } = text;

    private readonly List<(int Start, int End, string With, int Order)> _edits = new();
    private readonly HashSet<YamlNode> _removed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<YamlMappingNode, List<(string Key, string Value)>> _adds = new(ReferenceEqualityComparer.Instance);
    private readonly List<YamlMappingNode> _addOrder = new();

    public static int Start(YamlNode n) => (int)n.Start.Index;
    /// <summary>
    /// Where a node's text ends. The representation model leaves the end of a
    /// block collection at its start, so it is found from the last child; a
    /// flow collection ends at its closing bracket.
    /// </summary>
    public int End(YamlNode n)
    {
        switch (n)
        {
            case YamlScalarNode: return (int)n.End.Index;
            case YamlMappingNode m:
            {
                if (m.Children.Count == 0) return Math.Max((int)n.End.Index, FlowClose(Start(n), '{', '}'));
                int last = End(m.Children.Last().Value);
                return m.Style == YamlDotNet.Core.Events.MappingStyle.Flow ? Text.IndexOf('}', last) + 1 : last;
            }
            case YamlSequenceNode q:
            {
                if (q.Children.Count == 0) return Math.Max((int)n.End.Index, FlowClose(Start(n), '[', ']'));
                int last = End(q.Children[^1]);
                return q.Style == YamlDotNet.Core.Events.SequenceStyle.Flow ? Text.IndexOf(']', last) + 1 : last;
            }
            default: return (int)n.End.Index;
        }
    }

    private int FlowClose(int start, char open, char close) =>
        start < Text.Length && Text[start] == open ? Text.IndexOf(close, start) + 1 : start;

    public void Replace(int start, int end, string with)
    {
        foreach (var e in _edits)
            if (start < e.End && e.Start < end && !(start == end || e.Start == e.End))
                throw new InvalidOperationException(
                    $"overlapping edits at {LineOf(start)}: '{Text[start..end]}' and '{Text[e.Start..e.End]}'");
        _edits.Add((start, end, with, _edits.Count));
    }

    public void Insert(int at, string what) => Replace(at, at, what);

    public void Replace(YamlNode node, string with) => Replace(Start(node), End(node), with);

    public int LineOf(int index) => Text.AsSpan(0, Math.Min(index, Text.Length)).Count('\n') + 1;

    public bool IsRemoved(YamlNode key) => _removed.Contains(key);

    /// <summary>
    /// Renames a key in place, keeping the column its value was aligned to
    /// (the spaces after the colon grow or shrink, at least one stays).
    /// </summary>
    public void RenameKey(YamlNode key, string name)
    {
        int s = Start(key), e = End(key);
        int colon = e;
        if (colon < Text.Length && Text[colon] == ':')
        {
            int v = colon + 1;
            while (v < Text.Length && Text[v] == ' ') v++;
            int spaces = v - colon - 1;
            if (spaces > 1 && v < Text.Length && Text[v] is not ('\n' or '\r' or '#'))
            {
                int wanted = Math.Max(1, spaces + (e - s) - name.Length);
                Replace(s, v, name + ":" + new string(' ', wanted));
                return;
            }
        }
        Replace(s, e, name);
    }

    public int LineStart(int index)
    {
        int i = Text.LastIndexOf('\n', Math.Max(0, index - 1));
        return i < 0 ? 0 : i + 1;
    }

    /// <summary>Index of the newline that ends the last line holding text of <paramref name="node"/> (or the end of the file).</summary>
    public int LastLineEnd(YamlNode node)
    {
        int q = End(node) - 1;
        while (q > Start(node) && char.IsWhiteSpace(Text[q])) q--;
        int nl = Text.IndexOf('\n', Math.Max(q, 0));
        return nl < 0 ? Text.Length : nl;
    }

    private bool KeyOpensLine(YamlNode key)
    {
        int ls = LineStart(Start(key));
        return Text.AsSpan(ls, Start(key) - ls).Trim().Length == 0;
    }

    /// <summary>Removes a key and its value from a mapping.</summary>
    public void RemovePair(YamlMappingNode map, YamlNode key)
    {
        var value = map.Children[key];
        _removed.Add(key);
        if (map.Style == YamlDotNet.Core.Events.MappingStyle.Flow)
        {
            var keys = map.Children.Keys.ToList();
            int i = keys.IndexOf(key);
            int s = Start(key), e = End(value);
            if (i + 1 < keys.Count) e = Start(keys[i + 1]);
            else if (i > 0) s = End(map.Children[keys[i - 1]]);
            Replace(s, e, "");
            return;
        }
        if (KeyOpensLine(key))
        {
            int end = LastLineEnd(value);
            Replace(LineStart(Start(key)), Math.Min(end + 1, Text.Length), "");
            return;
        }
        // The key follows a sequence dash ("- key: v"): the next key moves up.
        var list = map.Children.Keys.ToList();
        int k = list.IndexOf(key);
        if (k + 1 < list.Count) Replace(Start(key), Start(list[k + 1]), "");
        else Replace(Start(key), LastLineEnd(value), "{}");
    }

    /// <summary>
    /// Comments a key out, value included (the content is kept for whoever
    /// reviews the scene); on a flow mapping or a dash line it is removed.
    /// Returns the original text of the pair.
    /// </summary>
    public string CommentOutPair(YamlMappingNode map, YamlNode key)
    {
        var value = map.Children[key];
        string original = Text[Start(key)..Math.Max(End(value), End(key))].TrimEnd();
        if (map.Style == YamlDotNet.Core.Events.MappingStyle.Flow || !KeyOpensLine(key))
        {
            RemovePair(map, key);
            return original;
        }
        _removed.Add(key);
        int col = Start(key) - LineStart(Start(key));
        int line = LineStart(Start(key));
        int last = LastLineEnd(value);
        while (line <= last && line < Text.Length)
        {
            int lineEnd = Text.IndexOf('\n', line);
            if (lineEnd < 0) lineEnd = Text.Length;
            int lead = 0;
            while (line + lead < lineEnd && Text[line + lead] == ' ') lead++;
            if (line + lead < lineEnd)
                Insert(line + Math.Min(lead, col), "# ");
            line = lineEnd + 1;
        }
        return original;
    }

    /// <summary>Adds a key at the end of a mapping (applied by <see cref="Apply"/>).</summary>
    public void AddPair(YamlMappingNode map, string key, string value)
    {
        if (!_adds.TryGetValue(map, out var list))
        {
            _adds[map] = list = new();
            _addOrder.Add(map);
        }
        list.Add((key, value));
    }

    private void EmitAdds()
    {
        // Deepest mappings first: two insertions at the end of the same line
        // then come out in nesting order.
        foreach (var map in _addOrder.OrderByDescending(m => Start(m)))
        {
            var adds = _adds[map];
            var live = map.Children.Where(kv => !_removed.Contains(kv.Key)).ToList();
            if (map.Style == YamlDotNet.Core.Events.MappingStyle.Flow || map.Children.Count == 0)
            {
                int close = Text.LastIndexOf('}', Math.Max(End(map) - 1, 0));
                string body = string.Join(", ", adds.Select(a => $"{a.Key}: {a.Value}"));
                if (close < Start(map)) { Replace(map, "{ " + body + " }"); continue; }
                if (live.Count > 0) Insert(End(live[^1].Value), ", " + body);
                else Insert(close, " " + body + " ");
                continue;
            }
            var first = map.Children.Keys.First();
            int col = Start(first) - LineStart(Start(first));
            string indent = new(' ', col);
            var anchor = live.Count > 0 ? live[^1].Value : map.Children.Last().Value;
            int at = LastLineEnd(anchor);
            var sb = new StringBuilder();
            foreach (var (k, v) in adds) sb.Append('\n').Append(indent).Append(k).Append(": ").Append(v);
            Insert(at, sb.ToString());
        }
    }

    public string Apply()
    {
        EmitAdds();
        var sb = new StringBuilder(Text);
        // From the end of the file; at one position the replacements first,
        // then the insertions, the earlier made ending up first.
        foreach (var e in _edits.OrderByDescending(e => e.Start)
                                .ThenBy(e => e.Start == e.End ? 1 : 0)
                                .ThenByDescending(e => e.Order))
        {
            sb.Remove(e.Start, e.End - e.Start);
            sb.Insert(e.Start, e.With);
        }
        return sb.ToString();
    }

    /// <summary>The node in flow style, scalars as written (to copy a value elsewhere).</summary>
    public string Flow(YamlNode node) => node switch
    {
        YamlScalarNode s => Text[Start(s)..End(s)],
        YamlSequenceNode q => "[" + string.Join(", ", q.Children.Select(Flow)) + "]",
        YamlMappingNode m => "{ " + string.Join(", ", m.Children.Select(kv => Flow(kv.Key) + ": " + Flow(kv.Value))) + " }",
        _ => "",
    };

    /// <summary>A number as YAML: the shortest form that reads back as the same float.</summary>
    public static string Num(double v)
    {
        float f = (float)v;
        if (f == MathF.Round(f) && MathF.Abs(f) < 1e7f) return ((int)f).ToString(CultureInfo.InvariantCulture);
        for (int digits = 4; digits <= 9; digits++)
        {
            string s = f.ToString("G" + digits, CultureInfo.InvariantCulture);
            if (float.Parse(s, CultureInfo.InvariantCulture) == f) return s.Contains('E') ? f.ToString("R", CultureInfo.InvariantCulture) : s;
        }
        return f.ToString("R", CultureInfo.InvariantCulture);
    }

    public static string Vec(IEnumerable<double> v) => "[" + string.Join(", ", v.Select(Num)) + "]";
}
