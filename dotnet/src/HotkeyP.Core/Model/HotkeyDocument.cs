// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace HotkeyP.Model;

/// <summary>A set of hotkeys and categories, stored in a .htk file.</summary>
public sealed class HotkeyDocument
{
    public const string Magic = "hotKeys";
    /// <summary>Current file version: 2 opacity, 3 lirc, 4 category, 5 sound, 6 delay, 7 UTF-8 + admin.</summary>
    public const int CurrentVersion = 7;

    public List<Hotkey> Hotkeys { get; } = new();
    /// <summary>Category names; index 0 is the default category and is not stored in the file.</summary>
    public List<string> Categories { get; } = new() { "" };

    /// <summary>Inserts a hotkey; multi commands keep pointing at the same hotkeys.</summary>
    public void Insert(int index, Hotkey hk)
    {
        Renumber(i => i >= index ? i + 1 : i);
        Hotkeys.Insert(index, hk);
    }

    /// <summary>Removes a hotkey; references to it are dropped from multi commands, later ones renumbered.</summary>
    public void RemoveAt(int index)
    {
        Hotkeys.RemoveAt(index);
        Renumber(i => i == index ? null : i > index ? i - 1 : i);
    }

    /// <summary>Moves a hotkey to another position; multi commands keep pointing at the same hotkeys.</summary>
    public void Move(int from, int to)
    {
        if (from == to) return;
        var hk = Hotkeys[from];
        Hotkeys.RemoveAt(from);
        Hotkeys.Insert(to, hk);
        Renumber(i => i == from ? to : from < to ? (i > from && i <= to ? i - 1 : i) : (i >= to && i < from ? i + 1 : i));
    }

    /// <summary>Rewrites the 1-based hotkey numbers in "Multi command" and "Commands list" parameters.</summary>
    void Renumber(Func<int, int?> map)
    {
        foreach (var hk in Hotkeys)
        {
            if (hk.Command is not (61 or 70)) continue;
            hk.Args = MultiCommandParam.Renumber(hk.Args, map);
        }
    }

    public static HotkeyDocument Load(string path) => Parse(File.ReadAllBytes(path));

    public void Save(string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, Serialize());
        File.Move(tmp, path, overwrite: true);
    }

    public static HotkeyDocument Parse(byte[] data)
    {
        var r = new Reader(data);
        if (!r.TryReadMagic(Magic)) throw new InvalidDataException("Not a HotkeyP hotkeys file.");
        int version = r.ReadInt();
        if (version < 1 || version > CurrentVersion)
            throw new InvalidDataException($"Unsupported hotkeys file version {version}.");
        Encoding enc = version < 7 ? AnsiEncoding() : new UTF8Encoding(false);

        int count = Math.Clamp(r.ReadInt(), 0, 999);
        int categories = 1;
        if (version > 3) categories = Math.Clamp(r.ReadInt(), 1, 999);
        r.SkipLine();

        var doc = new HotkeyDocument();
        for (int i = 0; i < count; i++)
        {
            var hk = new Hotkey
            {
                Note = r.ReadLine(enc),
                Exe = r.ReadLine(enc),
                Args = r.ReadLine(enc),
                Dir = r.ReadLine(enc),
                Sound = version > 4 ? r.ReadLine(enc) : "",
            };
            hk.ScanCode = r.ReadInt();
            hk.VKey = r.ReadInt();
            hk.Modifiers = (Modifiers)r.ReadInt();
            hk.Show = (ShowMode)Math.Clamp(r.ReadInt(), 0, 2);
            hk.Priority = (Priority)Math.Clamp(r.ReadInt(), 0, 5);
            hk.Flags = r.ReadInt();
            hk.Command = r.ReadInt();
            if (version > 1)
            {
                hk.Opacity = r.ReadInt();
                if (version > 3) hk.Category = Math.Clamp(r.ReadInt(), 0, categories - 1);
                if (version > 2 && hk.VKey == Hotkey.VkLirc) hk.Lirc = r.ReadLine(Encoding.ASCII).TrimStart();
                else r.SkipLine();
            }
            else r.SkipLine();
            if (hk.VKey == 255) FixOldWinKey(hk);
            doc.Hotkeys.Add(hk);
        }
        for (int i = 1; i < categories; i++) doc.Categories.Add(r.ReadLine(enc));
        return doc;
    }

    public byte[] Serialize()
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.Append(Magic).Append(CurrentVersion.ToString(inv)).Append(' ')
          .Append(Hotkeys.Count.ToString(inv)).Append(' ').Append(Categories.Count.ToString(inv)).Append("\r\n");
        foreach (var hk in Hotkeys)
        {
            foreach (var s in new[] { hk.Note, hk.Exe, hk.Args, hk.Dir, hk.Sound }) sb.Append(s).Append("\r\n");
            sb.AppendJoin(' ', new[] { hk.ScanCode, hk.VKey, (int)hk.Modifiers, (int)hk.Show, (int)hk.Priority,
                hk.Flags, hk.Command, hk.Opacity, hk.Category }.Select(v => v.ToString(inv)));
            if (hk.VKey == Hotkey.VkLirc) sb.Append(' ').Append(hk.Lirc);
            sb.Append("\r\n");
        }
        for (int i = 1; i < Categories.Count; i++) sb.Append(Categories[i]).Append("\r\n");
        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    /// <summary>Old HotkeyP versions used virtual key 255 for Win+letter hotkeys.</summary>
    static void FixOldWinKey(Hotkey hk)
    {
        int scan = (hk.ScanCode >> 16) & 0x1ff;
        if (scan > 1 && scan < 12 || scan > 15 && scan < 26 || scan > 29 && scan < 39 || scan > 43 && scan < 51)
        {
            int vk = (int)MapVirtualKey((uint)scan, 1 /*MAPVK_VSC_TO_VK*/);
            if (vk != 0) hk.VKey = vk;
        }
    }

    static Encoding AnsiEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding((int)GetACP());
    }

    [DllImport("kernel32.dll")] static extern uint GetACP();
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);

    /// <summary>Minimal reader that mimics the fscanf/fgets sequence of the original program.</summary>
    sealed class Reader(byte[] data)
    {
        int pos;

        public bool TryReadMagic(string magic)
        {
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) pos = 3;
            for (int i = 0; i < magic.Length; i++)
                if (pos + i >= data.Length || data[pos + i] != magic[i]) return false;
            pos += magic.Length;
            return true;
        }

        public int ReadInt()
        {
            while (pos < data.Length && (data[pos] == ' ' || data[pos] == '\t' || data[pos] == '\r' || data[pos] == '\n')) pos++;
            bool neg = false;
            if (pos < data.Length && (data[pos] == '-' || data[pos] == '+')) neg = data[pos++] == '-';
            long v = 0;
            int start = pos;
            while (pos < data.Length && data[pos] >= '0' && data[pos] <= '9') v = v * 10 + (data[pos++] - '0');
            if (pos == start) throw new InvalidDataException("Number expected in hotkeys file.");
            return unchecked((int)(neg ? -v : v));
        }

        /// <summary>Reads to the end of the current line, without the line break.</summary>
        public string ReadLine(Encoding enc)
        {
            int start = pos;
            while (pos < data.Length && data[pos] != '\n') pos++;
            int end = pos;
            if (pos < data.Length) pos++;
            if (end > start && data[end - 1] == '\r') end--;
            return enc.GetString(data, start, end - start);
        }

        public void SkipLine() => ReadLine(Encoding.ASCII);
    }
}
