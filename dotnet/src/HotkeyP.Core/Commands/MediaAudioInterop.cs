// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
// Hand-written Core Audio interop (mmdeviceapi.h, endpointvolume.h, devicetopology.h), as in source/vistavol.h.
// Every vtable slot up to the last one used is declared, in header order.
using System.Runtime.InteropServices;

namespace HotkeyP.Commands;

#pragma warning disable CA1401, SYSLIB1054, SYSLIB1096 // classic COM interop keeps the declarations close to the C++ headers

static class CoreAudio
{
    public const int eRender = 0, eCapture = 1;
    public const int eMultimedia = 1;
    public const uint DEVICE_STATE_ACTIVE = 1;
    public const uint CLSCTX_INPROC_SERVER = 1, CLSCTX_ALL = 0x17;
    public const int STGM_READ = 0;
    public const ushort VT_LPWSTR = 31;

    // DataFlow, PartType, ConnectorType (devicetopology.h)
    public const int In = 0, Out = 1;
    public const int PartConnector = 0;
    public const int Software_IO = 3;

    public static readonly PROPERTYKEY PKEY_Device_DeviceDesc = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
    public static readonly PROPERTYKEY PKEY_Device_FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    public static readonly Guid KSNODETYPE_VOLUME = new("3A5ACC00-C557-11D0-8A2B-00A0C9255AC1");
    public static readonly Guid KSNODETYPE_TONE = new("7607E580-C557-11D0-8A2B-00A0C9255AC1");
    public static readonly Guid KSNODETYPE_MUTE = new("02B223C0-C557-11D0-8A2B-00A0C9255AC1");

    public static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    public static readonly Guid IID_IDeviceTopology = new("2A07407E-6497-4A18-9787-32F79BD0D98F");
    public static readonly Guid IID_IAudioVolumeLevel = new("7FB7B48F-531D-44A2-BCB3-5AD5A134B3DC");
    public static readonly Guid IID_IAudioBass = new("A2B1A1D9-4DB3-425D-A2B2-BD335CB3E2E5");
    public static readonly Guid IID_IAudioTreble = new("0A717812-694E-4907-B74B-BAFA5CFDCA7B");
    public static readonly Guid IID_IAudioMute = new("DF45AEEA-B74A-4B6B-AFAD-2366B6AA012E");

    [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PROPVARIANT pv);

    public static void Release(object? o)
    {
        if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
    }
}

[StructLayout(LayoutKind.Sequential)]
struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
    public PROPERTYKEY(Guid f, uint p) { fmtid = f; pid = p; }
}

/// <summary>PROPVARIANT: 16 bytes on x86, 24 on x64. Only string values are read.</summary>
[StructLayout(LayoutKind.Sequential)]
struct PROPVARIANT
{
    public ushort vt;
    public ushort r1, r2, r3;
    public IntPtr p;
    public IntPtr p2;
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumeratorCom { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
    [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float levelDB, IntPtr eventContext);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);
    [PreserveSig] int GetMasterVolumeLevel(out float levelDB);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDB, IntPtr eventContext);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr eventContext);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDB);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute(int mute, IntPtr eventContext);
    [PreserveSig] int GetMute(out int mute);
}

[ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDeviceTopology
{
    [PreserveSig] int GetConnectorCount(out uint count);
    [PreserveSig] int GetConnector(uint index, out IConnector connector);
}

[ComImport, Guid("9c2c4058-23f5-41de-877a-df3af236a09e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IConnector
{
    [PreserveSig] int GetConnectorType(out int type); // GetType in C++
    [PreserveSig] int GetDataFlow(out int flow);
    [PreserveSig] int ConnectTo(IConnector connectTo);
    [PreserveSig] int Disconnect();
    [PreserveSig] int IsConnected(out int connected);
    [PreserveSig] int GetConnectedTo(out IConnector connectedTo);
}

[ComImport, Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPart
{
    [PreserveSig] int GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    [PreserveSig] int GetLocalId(out uint id);
    [PreserveSig] int GetGlobalId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetPartType(out int partType);
    [PreserveSig] int GetSubType(out Guid subType);
    [PreserveSig] int GetControlInterfaceCount(out uint count);
    [PreserveSig] int GetControlInterface(uint index, out IControlInterface control);
    [PreserveSig] int EnumPartsIncoming(out IPartsList parts);
    [PreserveSig] int EnumPartsOutgoing(out IPartsList parts);
    [PreserveSig] int GetTopologyObject(out IDeviceTopology topology);
    [PreserveSig] int Activate(uint clsContext, ref Guid iid, out IntPtr iface);
}

[ComImport, Guid("6DAA848C-5EB0-45CC-AEA5-998A2CDA1FFB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPartsList
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetPart(uint index, out IPart part);
}

[ComImport, Guid("45d37c3f-5140-444a-ae24-400789f3cbf3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IControlInterface
{
    [PreserveSig] int GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    [PreserveSig] int GetIID(out Guid iid);
}

/// <summary>
/// IPerChannelDbLevel (base of IAudioVolumeLevel, IAudioBass, IAudioTreble) and IAudioMute, called through the vtable
/// of the pointer IPart.Activate returned. A runtime-callable wrapper would QueryInterface for the base IID, which
/// drivers do not have to answer.
/// </summary>
static unsafe class PartControl
{
    static void* Slot(IntPtr p, int i) => (*(void***)p)[i];

    // IPerChannelDbLevel: 3 GetChannelCount, 4 GetLevelRange, 5 GetLevel, 6 SetLevel
    public static int GetChannelCount(IntPtr p, out uint n)
    {
        uint v; int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Slot(p, 3))(p, &v); n = v; return hr;
    }
    public static int GetLevelRange(IntPtr p, uint ch, out float min, out float max, out float step)
    {
        float a, b, c; int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, float*, float*, float*, int>)Slot(p, 4))(p, ch, &a, &b, &c);
        min = a; max = b; step = c; return hr;
    }
    public static int GetLevel(IntPtr p, uint ch, out float level)
    {
        float v; int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, float*, int>)Slot(p, 5))(p, ch, &v); level = v; return hr;
    }
    public static int SetLevel(IntPtr p, uint ch, float level) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, float, IntPtr, int>)Slot(p, 6))(p, ch, level, IntPtr.Zero);

    // IAudioMute: 3 SetMute, 4 GetMute
    public static int SetMute(IntPtr p, bool mute) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr, int>)Slot(p, 3))(p, mute ? 1 : 0, IntPtr.Zero);
    public static int GetMute(IntPtr p, out bool mute)
    {
        int v; int hr = ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(p, 4))(p, &v); mute = v != 0; return hr;
    }
}
