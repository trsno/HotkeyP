// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
namespace HotkeyP.Model;

/// <summary>
/// Hotkey numbers in "Multi command" / "Commands list" parameters, read like the C++ loop:
/// strtol a number, skip exactly one separator character (any), repeat until a number is 0 or missing.
/// </summary>
public static class MultiCommandParam
{
    public readonly record struct Number(int Value, int Start, int End);

    public static List<Number> Parse(string s)
    {
        var result = new List<Number>();
        int pos = 0;
        while (pos <= s.Length)
        {
            int p = pos;
            while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
            int start = p;
            bool neg = false;
            if (p < s.Length && (s[p] == '+' || s[p] == '-')) { neg = s[p] == '-'; p++; }
            int digits = p;
            long v = 0;
            while (p < s.Length && s[p] is >= '0' and <= '9') { v = Math.Min(v * 10 + (s[p] - '0'), int.MaxValue); p++; }
            if (p == digits || v == 0) break;
            result.Add(new Number((int)(neg ? -v : v), start, p));
            pos = p + 1; // skip one separator
        }
        return result;
    }

    /// <summary>Rewrites the numbers with <paramref name="map"/> (0-based index in, null = drop), keeping separators and trailing text.
    /// Returns the parameter unchanged when it contains no numbers.</summary>
    public static string Renumber(string s, Func<int, int?> map)
    {
        var nums = Parse(s);
        if (nums.Count == 0) return s;
        var kept = new List<(string text, string sep)>();
        for (int i = 0; i < nums.Count; i++)
        {
            var n = nums[i];
            string sep = n.End < s.Length ? s[n.End].ToString() : "";
            if (map(n.Value - 1) is int m) kept.Add(((m + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), sep));
        }
        var last = nums[^1];
        string tail = last.End < s.Length ? s[(last.End + 1)..] : "";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < kept.Count; i++)
        {
            sb.Append(kept[i].text);
            if (i < kept.Count - 1) sb.Append(kept[i].sep.Length > 0 ? kept[i].sep : " ");
        }
        if (tail.Length > 0) sb.Append(nums.Count > 0 && last.End < s.Length ? s[last.End] : ' ').Append(tail);
        return sb.ToString();
    }
}
