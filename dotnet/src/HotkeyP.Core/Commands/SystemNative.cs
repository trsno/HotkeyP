// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
using System.Runtime.InteropServices;

namespace HotkeyP.Commands;

/// <summary>P/Invoke used only by the system, power and drive commands.</summary>
internal static class SystemNative
{
    // ---------------------------------------------------------------- power, privileges

    public const uint EWX_LOGOFF = 0, EWX_REBOOT = 2, EWX_FORCE = 4, EWX_POWEROFF = 8;
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x20, TOKEN_QUERY = 8, SE_PRIVILEGE_ENABLED = 2;
    public const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public uint LuidLow;
        public int LuidHigh;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LookupPrivilegeValueW")]
    public static extern bool LookupPrivilegeValue(string? system, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, uint len, IntPtr prev, IntPtr retLen);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool wakeupEventsDisabled);

    // ---------------------------------------------------------------- display

    public const int ENUM_CURRENT_SETTINGS = -1;
    public const uint DM_BITSPERPEL = 0x40000, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
    public const uint CDS_UPDATEREGISTRY = 1;
    public const int DISP_CHANGE_SUCCESSFUL = 0, DISP_CHANGE_RESTART = 1, DISP_CHANGE_BADMODE = -2, DISP_CHANGE_NOTUPDATED = -3;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
    public static extern bool EnumDisplaySettings(string? device, int mode, ref DEVMODE dm);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")]
    public static extern int ChangeDisplaySettingsEx(string? device, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr param);

    // ---------------------------------------------------------------- shell

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHEmptyRecycleBinW")]
    public static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    [DllImport("shell32.dll")]
    public static extern void SHAddToRecentDocs(uint flags, IntPtr pv);

    // ---------------------------------------------------------------- services

    public const uint SC_MANAGER_CONNECT = 1, SERVICE_START = 0x10, SERVICE_STOP = 0x20, SERVICE_CONTROL_STOP = 1;
    public const int ERROR_SERVICE_ALREADY_RUNNING = 1056, ERROR_SERVICE_NOT_ACTIVE = 1062;

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS
    {
        public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode, dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenSCManagerW")]
    public static extern IntPtr OpenSCManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenServiceW")]
    public static extern IntPtr OpenService(IntPtr scm, string name, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "StartServiceW")]
    public static extern bool StartService(IntPtr service, uint argc, IntPtr argv);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ControlService(IntPtr service, uint control, out SERVICE_STATUS status);

    [DllImport("advapi32.dll")]
    public static extern bool CloseServiceHandle(IntPtr h);

    // ---------------------------------------------------------------- files and devices

    public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_ATTRIBUTE_NORMAL = 0x80;
    public const uint DRIVE_REMOVABLE = 2, DRIVE_FIXED = 3, DRIVE_CDROM = 5;
    public const uint IOCTL_SCSI_PASS_THROUGH_DIRECT = 0x4D014;
    public const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x2D1080, IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
    public const int ERROR_INVALID_HANDLE = 6, ERROR_MEDIA_CHANGED = 1110;
    public const byte SCSI_IOCTL_DATA_IN = 1;
    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern unsafe bool DeviceIoControl(IntPtr device, uint code, void* inBuf, int inSize, void* outBuf, int outSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetDriveTypeW")]
    public static extern uint GetDriveType(string root);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryDosDeviceW")]
    public static extern uint QueryDosDevice(string device, char[] target, int max);

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SCSI_PASS_THROUGH_DIRECT
    {
        public ushort Length;
        public byte ScsiStatus;
        public byte PathId;
        public byte TargetId;
        public byte Lun;
        public byte CdbLength;
        public byte SenseInfoLength;
        public byte DataIn;
        public uint DataTransferLength;
        public uint TimeOutValue;
        public IntPtr DataBuffer;
        public uint SenseInfoOffset;
        public fixed byte Cdb[16];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER
    {
        public SCSI_PASS_THROUGH_DIRECT spt;
        public fixed byte ucSenseBuf[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STORAGE_DEVICE_NUMBER
    {
        public uint DeviceType, DeviceNumber, PartitionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STORAGE_PROPERTY_QUERY
    {
        public int PropertyId, QueryType;
        public byte AdditionalParameters;
    }

    /// <summary>Offset of BusType in STORAGE_ADAPTER_DESCRIPTOR.</summary>
    public const int StorageAdapterDescriptorBusTypeOffset = 24;
    public const int StorageAdapterProperty = 1, PropertyStandardQuery = 0, BusTypeUsb = 7;

    // ---------------------------------------------------------------- MCI

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    public static extern int mciSendString(string command, System.Text.StringBuilder? ret, int retLen, IntPtr callback);

    // ---------------------------------------------------------------- SetupAPI, configuration manager

    public const uint DIGCF_PRESENT = 2, DIGCF_DEVICEINTERFACE = 0x10;
    public const int CR_SUCCESS = 0, PNP_VetoTypeUnknown = 0;
    public static readonly Guid GUID_DEVINTERFACE_DISK = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");
    public static readonly Guid GUID_DEVINTERFACE_CDROM = new("53f56308-b6bf-11d0-94f2-00a0c91efb8b");
    public static readonly Guid GUID_DEVINTERFACE_FLOPPY = new("53f56311-b6bf-11d0-94f2-00a0c91efb8b");

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    /// <summary>cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: the size of the fixed part (DWORD + one WCHAR, packed).</summary>
    public static int DeviceInterfaceDetailCbSize => IntPtr.Size == 8 ? 8 : 6;

    [DllImport("setupapi.dll", SetLastError = true, EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid classGuid, uint index, ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", SetLastError = true, EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int detailSize, out int requiredSize, IntPtr devInfo);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Request_Device_EjectW")]
    public static extern int CM_Request_Device_Eject(uint devInst, out int vetoType, char[]? vetoName, int nameLen, uint flags);
}
