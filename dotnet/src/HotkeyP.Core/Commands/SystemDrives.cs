// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
// Safe removal of drives is based on code (C) Uwe Sieber - www.uwe-sieber.de
using System.Runtime.InteropServices;
using System.Text;
using HotkeyP.Engine;
using static HotkeyP.Commands.SystemNative;

namespace HotkeyP.Commands;

/// <summary>CD/DVD door, audio CD, CD speed and safe removal of drives.</summary>
internal static class SystemDrives
{
    static readonly object cdLock = new();
    static int aliasCounter;

    // ---------------------------------------------------------------- CD

    /// <summary>
    /// C++ audioCD: <paramref name="op"/> is the command id (0 eject, 1 close, 62 eject/close, 39..42 audio, 100 speed).
    /// The work runs on its own thread, because the MCI and SCSI calls can take long.
    /// </summary>
    public static void AudioCD(IAppHost host, char? drive, int op, int speed = 0)
    {
        char letter;
        if (drive == null)
        {
            letter = 'A';
            for (; ; letter++)
            {
                if (GetDriveType(letter + ":\\") == DRIVE_CDROM) break;
                if (letter == 'Z') return;
            }
        }
        else
        {
            letter = drive.Value;
        }
        if (GetDriveType(letter + ":\\") != DRIVE_CDROM)
        {
            host.ShowMessage($"{letter}: is not a CD drive");
            return;
        }
        new Thread(() =>
        {
            try { CdProc(host, letter, op, speed); }
            catch (Exception ex) { Log.Error(ex); }
        }) { IsBackground = true, Name = "HotkeyP CD" }.Start();
    }

    static int Mci(string command, StringBuilder? ret = null) => mciSendString(command, ret, ret?.Capacity ?? 0, IntPtr.Zero);

    static void CdProc(IAppHost host, char letter, int op, int speed)
    {
        lock (cdLock)
        {
            string alias = "hotkeypcd" + Interlocked.Increment(ref aliasCounter);
            if (Mci($"open {letter}: type cdaudio alias {alias}") != 0)
            {
                host.ShowMessage("Drive is already used by another process");
                return;
            }
            try
            {
                if (op == 62)
                {
                    switch (CdDoorState(letter))
                    {
                        case CdDoorAction.Eject:
                            CdCmd(letter, 2);
                            op = -1;
                            break;
                        case CdDoorAction.Close:
                            CdCmd(letter, 3);
                            op = -1;
                            break;
                        default:
                            // the drive does not report the tray state: if opening the door returns at once, it was open
                            long t = Environment.TickCount64;
                            Mci($"set {alias} door open");
                            if (Environment.TickCount64 - t < 200) op = 1;
                            break;
                    }
                }
                switch (op)
                {
                    case 0:
                        Mci($"set {alias} door open");
                        break;
                    case 1:
                        Mci($"set {alias} door closed");
                        break;
                    case 39:
                        Mci($"play {alias}");
                        break;
                    case 41:
                        Mci($"stop {alias}");
                        break;
                    case 40:
                    case 42:
                    {
                        var sb = new StringBuilder(128);
                        if (Mci($"status {alias} current track", sb) == 0 && int.TryParse(sb.ToString().Trim(), out int track))
                        {
                            track += 41 - op;
                            sb.Clear();
                            if (Mci($"status {alias} position track {track}", sb) == 0)
                                Mci($"play {alias} from {sb.ToString().Trim()}");
                        }
                        break;
                    }
                    case 100:
                        CdCmd(letter, 4, speed);
                        break;
                }
            }
            finally
            {
                Mci($"close {alias}");
            }
        }
    }

    /// <summary>Opens \\.\X: and sends SCSI INQUIRY; returns the handle and the peripheral device type, or -1.</summary>
    static unsafe IntPtr GetCDinfo(char letter, out int deviceType)
    {
        deviceType = -1;
        string path = @"\\.\" + letter + ":";
        var h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (h == INVALID_HANDLE_VALUE)
            h = CreateFile(path, GENERIC_READ, FILE_SHARE_READ, IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (h == INVALID_HANDLE_VALUE) return h;

        byte* inqData = stackalloc byte[36];
        new Span<byte>(inqData, 36).Clear();
        var swb = new SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER();
        swb.spt.Length = (ushort)sizeof(SCSI_PASS_THROUGH_DIRECT);
        swb.spt.CdbLength = 6;
        swb.spt.SenseInfoLength = 24;
        swb.spt.DataIn = SCSI_IOCTL_DATA_IN;
        swb.spt.DataTransferLength = 36;
        swb.spt.TimeOutValue = 5;
        swb.spt.DataBuffer = (IntPtr)inqData;
        swb.spt.SenseInfoOffset = (uint)sizeof(SCSI_PASS_THROUGH_DIRECT);
        swb.spt.Cdb[0] = 0x12; // INQUIRY
        swb.spt.Cdb[4] = 0x24;
        int size = sizeof(SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER);
        if (DeviceIoControl(h, IOCTL_SCSI_PASS_THROUGH_DIRECT, &swb, size, &swb, size, out _, IntPtr.Zero))
            deviceType = inqData[0] & 15;
        return h;
    }

    const int DTC_WORM = 4, DTC_CDROM = 5;

    /// <summary>TEST UNIT READY: tells whether the tray is open, closed, or unknown.</summary>
    static CdDoorAction CdDoorState(char letter)
    {
        CdDoorAction result = CdDoorAction.MciFallback;
        CdCmd(letter, 0, 0, r => result = r);
        return result;
    }

    /// <summary>C++ cdCmd. action: 0 = test unit ready, 2 = eject, 3 = load (close), 4 = set speed.</summary>
    static unsafe void CdCmd(char letter, int action, int param = 0, Action<CdDoorAction>? senseResult = null)
    {
        var h = GetCDinfo(letter, out int type);
        try
        {
            if (h == INVALID_HANDLE_VALUE || (type != DTC_CDROM && type != DTC_WORM)) return;
            bool retried = false;
            while (true)
            {
                var swb = new SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER();
                swb.spt.CdbLength = 6;
                if (action is 2 or 3)
                {
                    swb.spt.Cdb[0] = 0x1B; // START STOP UNIT
                    swb.spt.Cdb[4] = (byte)action;
                }
                if (action == 4)
                {
                    var (hi, lo) = SystemParsing.CdSpeedBytes(param);
                    swb.spt.Cdb[0] = 0xBB; // SET CD SPEED
                    swb.spt.Cdb[2] = hi;
                    swb.spt.Cdb[3] = lo;
                    swb.spt.Cdb[4] = swb.spt.Cdb[5] = 0xFF;
                    swb.spt.CdbLength = 12;
                }
                swb.spt.Length = (ushort)sizeof(SCSI_PASS_THROUGH_DIRECT);
                swb.spt.DataIn = SCSI_IOCTL_DATA_IN;
                swb.spt.TimeOutValue = 15;
                swb.spt.SenseInfoLength = 14;
                swb.spt.SenseInfoOffset = (uint)sizeof(SCSI_PASS_THROUGH_DIRECT);
                int size = sizeof(SCSI_PASS_THROUGH_DIRECT_WITH_BUFFER);
                if (DeviceIoControl(h, IOCTL_SCSI_PASS_THROUGH_DIRECT, &swb, size, &swb, size, out _, IntPtr.Zero))
                {
                    senseResult?.Invoke(SystemParsing.DoorAction(true, swb.ucSenseBuf[2], swb.ucSenseBuf[12], swb.ucSenseBuf[13]));
                    return;
                }
                int err = Marshal.GetLastWin32Error();
                if (retried || (err != ERROR_MEDIA_CHANGED && err != ERROR_INVALID_HANDLE)) return;
                retried = true;
                CloseHandle(h);
                h = GetCDinfo(letter, out _);
                if (h == INVALID_HANDLE_VALUE) return;
            }
        }
        finally
        {
            if (h != INVALID_HANDLE_VALUE) CloseHandle(h);
        }
    }

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);

    // ---------------------------------------------------------------- remove drive

    /// <summary>Command 109: safely removes the drive, or every USB drive when the parameter is empty.</summary>
    public static void RemoveDrive(IAppHost host, string param)
    {
        if (param.Length > 0) RemoveDrive(host, param[0]);
        else RemoveUsbDrives(host);
    }

    static void RemoveDrive(IAppHost host, char driveLetter)
    {
        if (SystemParsing.RemoveDriveLetter(driveLetter) is not { } letter) return;
        uint devInst = DevInstOfDrive(letter);
        if (devInst == 0) return;

        // the parent is the USB bridge, SATA port, IDE channel...
        CM_Get_Parent(out uint parent, devInst, 0);
        var vetoName = new char[260];
        for (int tries = 1; tries <= 3; tries++)
        {
            vetoName[0] = '\0';
            int res = CM_Request_Device_Eject(parent, out int vetoType, vetoName, vetoName.Length, 0);
            if (res == CR_SUCCESS && vetoType == PNP_VetoTypeUnknown) return;
            Thread.Sleep(500);
        }
        host.ShowMessage($"Cannot remove drive {letter}:");
    }

    static unsafe uint DevInstOfDrive(char letter)
    {
        string volumePath = @"\\.\" + letter + ":";
        var hVolume = CreateFile(volumePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (hVolume == INVALID_HANDLE_VALUE) return 0;
        long deviceNumber = -1;
        STORAGE_DEVICE_NUMBER sdn;
        if (DeviceIoControl(hVolume, IOCTL_STORAGE_GET_DEVICE_NUMBER, null, 0, &sdn, sizeof(STORAGE_DEVICE_NUMBER), out _, IntPtr.Zero))
            deviceNumber = sdn.DeviceNumber;
        CloseHandle(hVolume);
        if (deviceNumber == -1) return 0;

        uint driveType = GetDriveType(letter + ":\\");
        var dosName = new char[260];
        uint n = QueryDosDevice(letter + ":", dosName, dosName.Length);
        if (n == 0) return 0;
        bool isFloppy = new string(dosName, 0, (int)n).Split('\0')[0].Contains(@"\Floppy", StringComparison.Ordinal);

        Guid guid;
        switch (driveType)
        {
            case DRIVE_REMOVABLE: guid = isFloppy ? GUID_DEVINTERFACE_FLOPPY : GUID_DEVINTERFACE_DISK; break;
            case DRIVE_FIXED: guid = GUID_DEVINTERFACE_DISK; break;
            case DRIVE_CDROM: guid = GUID_DEVINTERFACE_CDROM; break;
            default: return 0;
        }
        return DevInstByDeviceNumber(deviceNumber, guid);
    }

    static unsafe uint DevInstByDeviceNumber(long deviceNumber, Guid guid)
    {
        var hDevInfo = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (hDevInfo == INVALID_HANDLE_VALUE || hDevInfo == IntPtr.Zero) return 0;
        const int bufSize = 1024;
        var buf = Marshal.AllocHGlobal(bufSize);
        try
        {
            var spdid = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)sizeof(SP_DEVICE_INTERFACE_DATA) };
            for (uint index = 0; SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref guid, index, ref spdid); index++)
            {
                SetupDiGetDeviceInterfaceDetail(hDevInfo, ref spdid, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                if (size == 0 || size > bufSize) continue;
                Marshal.WriteInt32(buf, DeviceInterfaceDetailCbSize);
                var spdd = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };
                if (!SetupDiGetDeviceInterfaceDetail(hDevInfo, ref spdid, buf, size, out _, (IntPtr)(&spdd))) continue;
                string devicePath = Marshal.PtrToStringUni(buf + 4) ?? "";
                var hDrive = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (hDrive == INVALID_HANDLE_VALUE) continue;
                STORAGE_DEVICE_NUMBER sdn;
                bool ok = DeviceIoControl(hDrive, IOCTL_STORAGE_GET_DEVICE_NUMBER, null, 0, &sdn, sizeof(STORAGE_DEVICE_NUMBER), out _, IntPtr.Zero);
                CloseHandle(hDrive);
                if (ok && deviceNumber == sdn.DeviceNumber) return spdd.DevInst;
            }
            return 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }
    }

    static unsafe void RemoveUsbDrives(IAppHost host)
    {
        byte* outBuf = stackalloc byte[512];
        for (char drive = 'A'; drive <= 'Z'; drive++)
        {
            var hVolume = CreateFile(@"\\.\" + drive + ":", 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (hVolume == INVALID_HANDLE_VALUE) continue;
            var query = new STORAGE_PROPERTY_QUERY { PropertyId = StorageAdapterProperty, QueryType = PropertyStandardQuery };
            new Span<byte>(outBuf, 512).Clear();
            DeviceIoControl(hVolume, IOCTL_STORAGE_QUERY_PROPERTY, &query, sizeof(STORAGE_PROPERTY_QUERY), outBuf, 512, out _, IntPtr.Zero);
            CloseHandle(hVolume);
            if (outBuf[StorageAdapterDescriptorBusTypeOffset] == BusTypeUsb) RemoveDrive(host, drive);
        }
    }
}
