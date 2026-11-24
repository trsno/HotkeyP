// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
// Port of source/volume.cpp (Windows Vista and newer part: endpoint and device topology API).
using System.Runtime.InteropServices;
using static HotkeyP.Commands.CoreAudio;

namespace HotkeyP.Commands;

/// <summary>0 = volume +/-, 1 = toggle mute, 2 = set volume (the action numbers of volume.cpp).</summary>
enum VolumeAction { Change = 0, ToggleMute = 1, Set = 2 }

/// <summary>
/// What a volume command changes. Parameter format: <c>[number] [R:] [1-9:] [line name]</c>.
/// The number is the step (commands 13, 14) or the level in percent (78). <c>R:</c> selects recording devices,
/// <c>N:</c> the N-th active audio device of that direction. The name is the device description (e.g. "Speakers"),
/// its full name, "Mixer" (the default device, also used when the name is empty), or Wave / Bass / Treble
/// (controls inside the default playback device, if the driver has them).
/// </summary>
sealed record VolumeRequest(string Name, bool Record, int Device, VolumeAction Action, int Value)
{
    public bool IsMixer => Name.Equals("Mixer", StringComparison.OrdinalIgnoreCase);

    /// <summary>Text shown in the on-screen display, like the volume popup of HotkeyP.</summary>
    public string Label => (Device > 0 ? $"{Device}: " : "") + (Record ? "R:" : "") + (IsMixer ? "Volume" : Name);

    /// <summary>Builds the request of command 13, 14, 15, 16, 17, 58 or 78 like command() in Commands.cpp.</summary>
    public static VolumeRequest ForCommand(int cmd, string param)
    {
        int iparam = StrToIntStr(param ?? "", out string param2, out bool hasNumber);
        int vol = iparam != 0 ? iparam : 1;
        return cmd switch
        {
            13 => Parse(param2, VolumeAction.Change, vol),
            14 => Parse(param2, VolumeAction.Change, -vol),
            15 => Parse("Wave", VolumeAction.Change, vol),
            16 => Parse("Wave", VolumeAction.Change, -vol),
            17 => Parse(param2, VolumeAction.ToggleMute, 1),
            58 => Parse("Wave", VolumeAction.ToggleMute, 1),
            78 => Parse(param2, VolumeAction.Set, hasNumber ? iparam : 50),
            _ => throw new ArgumentOutOfRangeException(nameof(cmd)),
        };
    }

    /// <summary>
    /// strToIntStr: a leading decimal number and the rest of the text after spaces. A number followed by ':' is a
    /// device prefix, not a value; then the result is 0 and the rest is the whole text.
    /// </summary>
    public static int StrToIntStr(string s, out string rest, out bool hasNumber)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int sign = 1;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) { if (s[i] == '-') sign = -1; i++; }
        int digitsStart = i;
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9')
        {
            if (v < int.MaxValue) v = v * 10 + (s[i] - '0');
            i++;
        }
        hasNumber = i > digitsStart;
        int end = hasNumber ? i : 0; // strtol returns the start of the text when there is no number
        if (end < s.Length && s[end] == ':')
        {
            rest = s;
            hasNumber = false;
            return 0;
        }
        while (end < s.Length && s[end] == ' ') end++;
        rest = s[end..];
        return hasNumber ? (int)Math.Clamp(sign * v, int.MinValue, int.MaxValue) : 0;
    }

    /// <summary>Prefixes R: and 1: to 9: (in any order), then the line name; an empty name means "Mixer".</summary>
    public static VolumeRequest Parse(string which, VolumeAction action, int value)
    {
        bool record = false;
        int device = 0;
        int i = 0;
        for (; ; )
        {
            while (i < which.Length && which[i] == ' ') i++;
            if (i + 1 < which.Length && (which[i] == 'r' || which[i] == 'R') && which[i + 1] == ':') { record = true; i += 2; continue; }
            if (i + 1 < which.Length && which[i] > '0' && which[i] <= '9' && which[i + 1] == ':') { device = which[i] - '0'; i += 2; continue; }
            break;
        }
        string name = which[i..].TrimEnd();
        if (name.Length == 0) name = "Mixer";
        return new VolumeRequest(name, record, device, action, value);
    }

    /// <summary>New level in percent of an endpoint (volumeVista2). Set is clamped to 0..100 (the C++ did not clamp).</summary>
    public static int NewPercent(int current, VolumeAction action, int value) => action switch
    {
        VolumeAction.Change => Math.Clamp(current + value, 0, 100),
        VolumeAction.Set => Math.Clamp(value, 0, 100),
        _ => current,
    };

    /// <summary>
    /// New dB level of a topology control (controls() in volume.cpp): a step is at least one hardware step.
    /// Returns the level and the percent that the display shows.
    /// </summary>
    public static (float Level, int Percent, bool Changed) NewDbLevel(float level, float min, float max, float step, VolumeAction action, int value)
    {
        float rng = max - min;
        if (rng <= 0) return (level, 0, false);
        int i = (int)((level - min) * 100 / rng + 0.5f);
        int i0 = i;
        if (action == VolumeAction.Change)
        {
            i = Math.Clamp(i + value, 0, 100);
            float incr = value * rng / 100;
            if (value < 0) { if (-incr < step) incr = -step; }
            else if (incr < step) incr = step;
            level += incr;
        }
        else if (action == VolumeAction.Set)
        {
            i = Math.Clamp(value, 0, 100);
            level = i * rng / 100 + min;
            level += level < 0 ? -step / 2 : step / 2;
        }
        return (Math.Clamp(level, min, max), i, i != i0);
    }
}

/// <summary>Level of the audio line a volume command changed, for the on-screen display.</summary>
sealed record VolumeState(int Percent, bool Muted);

/// <summary>Applies a <see cref="VolumeRequest"/> to every matching audio line with the Core Audio API.</summary>
sealed class VolumeController
{
    /// <summary>Device names by endpoint id, like endpointNameList (reading the property store is slow).</summary>
    static readonly Dictionary<string, (string? Desc, string? Friendly)> nameCache = new();

    readonly VolumeRequest req;
    int? percent;
    bool? muted;

    VolumeController(VolumeRequest req) => this.req = req;

    /// <summary>Changes the volume; returns the state of the first matching line or null when no line matched.</summary>
    public static VolumeState? Apply(VolumeRequest req)
    {
        var vc = new VolumeController(req);
        vc.Run();
        return vc.percent is int p ? new VolumeState(p, vc.muted ?? false) : vc.muted is bool m ? new VolumeState(0, m) : null;
    }

    void Report(int? p, bool? m)
    {
        // the display shows the first line that matched
        if (percent == null && muted == null) { percent = p; muted = m; }
    }

    void Run()
    {
        IMMDeviceEnumerator? en = null;
        try
        {
            en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        }
        catch (COMException ex) { Engine.Log.Error(ex); return; }
        try
        {
            for (int rec = 0; rec < 2; rec++)
            {
                int flow = rec == 1 ? eCapture : eRender;
                string? defaultId = null;
                if (en.GetDefaultAudioEndpoint(flow, eMultimedia, out var def) == 0)
                {
                    if (def.GetId(out string id) == 0) defaultId = id;
                    Release(def);
                }
                if (en.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, out var coll) != 0) continue;
                try
                {
                    coll.GetCount(out uint count);
                    for (uint i = 0; i < count; i++)
                    {
                        if (coll.Item(i, out var dev) != 0) continue;
                        try { Endpoint(dev, rec == 1, (int)i, defaultId); }
                        finally { Release(dev); }
                    }
                }
                finally { Release(coll); }
            }
        }
        finally { Release(en); }
    }

    /// <summary>volumeVista1: does this endpoint match, and should its topology be searched for Wave, Bass, Treble.</summary>
    void Endpoint(IMMDevice dev, bool rec, int index, string? defaultId)
    {
        if (dev.GetId(out string id) != 0) return;
        if (!nameCache.TryGetValue(id, out var names))
        {
            if (dev.OpenPropertyStore(STGM_READ, out var props) == 0)
            {
                names = (PropString(props, PKEY_Device_DeviceDesc), PropString(props, PKEY_Device_FriendlyName));
                Release(props);
                nameCache[id] = names;
            }
        }
        bool isDefault = defaultId != null ? id == defaultId : string.Equals(names.Desc, "Speakers", StringComparison.OrdinalIgnoreCase);
        var (volume, topology) = Select(req, rec, index, isDefault, names.Desc, names.Friendly);
        if (volume) EndpointVolume(dev);
        if (topology) Topology(dev);
    }

    /// <summary>
    /// Whether an endpoint's own volume matches the request, and whether its topology is searched for Wave, Bass,
    /// Treble. N: selects the N-th device of the direction; without it, "Mixer" means the default device.
    /// </summary>
    public static (bool Volume, bool Topology) Select(VolumeRequest req, bool rec, int index, bool isDefault, string? desc, string? friendly)
    {
        bool selected = rec == req.Record && (req.Device == 0 || req.Device - 1 == index);
        bool Eq(string? name) => name != null && name.Equals(req.Name, StringComparison.OrdinalIgnoreCase);
        bool volume = selected && (Eq(desc) || Eq(friendly) || req.IsMixer && (isDefault || req.Device > 0));
        bool topology = selected && !rec && !req.IsMixer && (req.Device > 0 || isDefault);
        return (volume, topology);
    }

    static string? PropString(IPropertyStore props, PROPERTYKEY key)
    {
        if (props.GetValue(ref key, out var pv) != 0) return null;
        try { return pv.vt == VT_LPWSTR ? Marshal.PtrToStringUni(pv.p) : null; }
        finally { PropVariantClear(ref pv); }
    }

    /// <summary>volumeVista2: master volume and mute of an endpoint.</summary>
    void EndpointVolume(IMMDevice dev)
    {
        var iid = IID_IAudioEndpointVolume;
        if (dev.Activate(ref iid, CLSCTX_INPROC_SERVER, IntPtr.Zero, out object o) != 0) return;
        var ev = (IAudioEndpointVolume)o;
        try
        {
            int? p = null;
            bool? m = null;
            if (ev.GetMasterVolumeLevelScalar(out float f) == 0)
            {
                int i = (int)(f * 100 + 0.5f);
                int n = VolumeRequest.NewPercent(i, req.Action, req.Value);
                if (n != i && ev.SetMasterVolumeLevelScalar(n / 100f, IntPtr.Zero) == 0) i = n;
                p = i;
            }
            if (ev.GetMute(out int b) == 0)
            {
                if (req.Action == VolumeAction.ToggleMute && ev.SetMute(b == 0 ? 1 : 0, IntPtr.Zero) == 0) b = b == 0 ? 1 : 0;
                m = b != 0;
            }
            Report(p, m);
        }
        finally { Release(ev); }
    }

    // ------------------------------------------------------------ device topology (Wave, Bass, Treble)

    int depth;

    void Topology(IMMDevice dev)
    {
        var iid = IID_IDeviceTopology;
        if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object o) != 0) return;
        var topo = (IDeviceTopology)o;
        try
        {
            // the device topology of an endpoint always contains just one connector
            if (topo.GetConnector(0, out var conn) != 0) return;
            try
            {
                if (conn.GetDataFlow(out int flow) == 0) Connector(conn, flow);
            }
            finally { Release(conn); }
        }
        finally { Release(topo); }
    }

    void Connector(IConnector from, int flow)
    {
        if (from.IsConnected(out int connected) != 0 || connected == 0) return; // end of the path (Software_IO)
        if (from.GetDataFlow(out int flowTo) != 0 || flowTo != flow) return;
        if (from.GetConnectedTo(out var to) != 0) return;
        try
        {
            if (to is IPart part) Part(part, flow);
        }
        finally { Release(to); }
    }

    void Part(IPart part, int flow)
    {
        if (++depth > 64) { depth--; return; }
        try
        {
            int hr = flow == Out ? part.EnumPartsOutgoing(out var parts) : part.EnumPartsIncoming(out parts);
            if (hr != 0) return; // includes ERROR_NOT_FOUND at the end of the path
            try
            {
                if (part.GetName(out string name) != 0) name = "";
                if (part.GetSubType(out Guid subType) == 0 &&
                    (subType == KSNODETYPE_VOLUME || subType == KSNODETYPE_MUTE || subType == KSNODETYPE_TONE))
                    Controls(part, name);
                if (parts.GetCount(out uint count) != 0) return;
                for (uint i = 0; i < count; i++)
                {
                    if (parts.GetPart(i, out var next) != 0) return;
                    try
                    {
                        if (next.GetPartType(out int type) != 0) return;
                        if (type == PartConnector)
                        {
                            if (next is IConnector c) Connector(c, flow);
                        }
                        else Part(next, flow);
                    }
                    finally { Release(next); }
                }
            }
            finally { Release(parts); }
        }
        finally { depth--; }
    }

    /// <summary>controls() in volume.cpp: Bass and Treble controls, and volume and mute of parts named Wave...</summary>
    void Controls(IPart part, string partName)
    {
        if (part.GetControlInterfaceCount(out uint n) != 0) return;
        bool wavePart = partName.StartsWith("Wave", StringComparison.Ordinal);
        for (uint j = 0; j < n; j++)
        {
            if (part.GetControlInterface(j, out var ci) != 0) return;
            try
            {
                if (ci.GetIID(out Guid iid) != 0) return;
                string? name = iid == IID_IAudioBass ? "Bass" : iid == IID_IAudioTreble ? "Treble"
                    : wavePart && (iid == IID_IAudioVolumeLevel || iid == IID_IAudioMute) ? "Wave" : null;
                if (name == null || !name.Equals(req.Name, StringComparison.OrdinalIgnoreCase)) continue;
                if (part.Activate(CLSCTX_ALL, ref iid, out IntPtr p) != 0 || p == IntPtr.Zero) continue;
                try
                {
                    if (iid == IID_IAudioMute) Mute(p);
                    else Level(p);
                }
                finally { Marshal.Release(p); }
            }
            finally { Release(ci); }
        }
    }

    void Level(IntPtr p)
    {
        if (req.Action == VolumeAction.ToggleMute) return; // a level control has no mute
        if (PartControl.GetChannelCount(p, out uint nch) != 0) return;
        int vol = 0;
        for (uint c = 0; c < nch; c++)
        {
            if (PartControl.GetLevel(p, c, out float level) != 0 || PartControl.GetLevelRange(p, c, out float l1, out float l2, out float step) != 0) continue;
            var (newLevel, percent, changed) = VolumeRequest.NewDbLevel(level, l1, l2, step, req.Action, req.Value);
            if (changed) PartControl.SetLevel(p, c, newLevel);
            vol = Math.Max(vol, percent);
        }
        Report(vol, null);
    }

    /// <summary>Not in the C++ (its IAudioMute code is commented out): "Mute Wave" toggles a mute control of a Wave part.</summary>
    void Mute(IntPtr p)
    {
        if (req.Action != VolumeAction.ToggleMute) return;
        if (PartControl.GetMute(p, out bool m) != 0) return;
        if (PartControl.SetMute(p, !m) == 0) m = !m;
        Report(null, m);
    }
}
