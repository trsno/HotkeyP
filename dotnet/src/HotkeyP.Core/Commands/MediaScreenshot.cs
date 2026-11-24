// HotkeyP .NET port. Based on HotkeyP (C) Petr Lastovicka, GNU General Public License.
// Port of screenshot() in source/Commands.cpp: capture, then ask for a file name and save a BMP file.
using System.Runtime.InteropServices;
using Microsoft.Win32;
using HotkeyP.Native;

namespace HotkeyP.Commands;

#pragma warning disable CA1401, SYSLIB1054

static class Screenshot
{
    const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;
    const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT rect, int size);

    /// <summary>Command 89 (desktop = primary monitor, like GetDesktopWindow in the C++) and 90 (foreground window).</summary>
    public static void Take(CommandContext c, bool desktop)
    {
        RECT rc;
        if (desktop)
        {
            rc = new RECT { Left = 0, Top = 0, Right = NativeMethods.GetSystemMetrics(SM_CXSCREEN), Bottom = NativeMethods.GetSystemMetrics(SM_CYSCREEN) };
        }
        else
        {
            var w = NativeMethods.GetForegroundWindow();
            if (w == IntPtr.Zero) return;
            // the visible frame, without the invisible resize borders GetWindowRect includes
            if (DwmGetWindowAttribute(w, DWMWA_EXTENDED_FRAME_BOUNDS, out rc, Marshal.SizeOf<RECT>()) != 0 &&
                !NativeMethods.GetWindowRect(w, out rc)) return;
        }
        byte[]? pixels = Capture(rc, out int width, out int height);
        if (pixels == null) { c.Host.ShowMessage("GetDIBits failed"); return; }

        string? file = AskFileName(c.Host.MainWindowHandle);
        if (file == null) return;
        try { File.WriteAllBytes(file, Bmp(width, height, pixels)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            c.Host.ShowMessage($"Cannot create file {file}");
        }
    }

    /// <summary>Copies the screen area into a 32-bit bottom-up DIB.</summary>
    static byte[]? Capture(RECT rc, out int w, out int h)
    {
        w = rc.Width;
        h = rc.Height;
        if (w <= 0 || h <= 0) return null;
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
        try
        {
            IntPtr old = SelectObject(mem, bmp);
            BitBlt(mem, 0, 0, w, h, screen, rc.Left, rc.Top, SRCCOPY | CAPTUREBLT);
            SelectObject(mem, old);
            var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = h, biPlanes = 1, biBitCount = 32 };
            var bits = new byte[w * 4 * h];
            return GetDIBits(mem, bmp, 0, (uint)h, bits, ref bi, 0) == 0 ? null : bits;
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>A .bmp file: file header, BITMAPINFOHEADER, 32-bit bottom-up pixels.</summary>
    public static byte[] Bmp(int width, int height, byte[] pixels)
    {
        int imageSize = width * 4 * height;
        if (pixels.Length < imageSize) throw new ArgumentException("Too few pixels", nameof(pixels));
        const int headers = 14 + 40;
        var ms = new MemoryStream(headers + imageSize);
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write((ushort)0x4D42); // 'BM'
            bw.Write(headers + imageSize);
            bw.Write(0);
            bw.Write(headers);
            bw.Write(40);
            bw.Write(width);
            bw.Write(height);
            bw.Write((short)1);
            bw.Write((short)32);
            bw.Write(0); // BI_RGB
            bw.Write(imageSize);
            bw.Write(0);
            bw.Write(0);
            bw.Write(0);
            bw.Write(0);
            bw.Write(pixels, 0, imageSize);
        }
        return ms.ToArray();
    }

    // ------------------------------------------------------------ save dialog (save1 in Hotkeyp.cpp)

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner, hInstance, lpstrFilter, lpstrCustomFilter;
        public int nMaxCustFilter, nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public IntPtr lpstrInitialDir, lpstrTitle;
        public int Flags;
        public short nFileOffset, nFileExtension;
        public IntPtr lpstrDefExt, lCustData, lpfnHook, lpTemplateName, pvReserved;
        public int dwReserved, FlagsEx;
    }

    const int OFN_HIDEREADONLY = 0x4, OFN_NOCHANGEDIR = 0x8, OFN_PATHMUSTEXIST = 0x800;
    const uint FNERR_INVALIDFILENAME = 0x3002;
    const int MaxPath = 1024;

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)] static extern bool GetSaveFileName(ref OPENFILENAME ofn);
    [DllImport("comdlg32.dll")] static extern uint CommDlgExtendedError();

    /// <summary>Asks for the file name; the last one is remembered in the registry value "snapshotFile" as in the C++.</summary>
    static string? AskFileName(IntPtr mainWindow)
    {
        string initial = MediaSettings.Get("snapshotFile") as string ?? "";
        IntPtr filter = Marshal.StringToHGlobalUni("Bitmap images (*.bmp)\0*.bmp\0\0");
        IntPtr defExt = Marshal.StringToHGlobalUni("BMP");
        IntPtr buf = Marshal.AllocHGlobal(MaxPath * 2);
        try
        {
            for (; ; )
            {
                WriteString(buf, initial);
                var ofn = new OPENFILENAME
                {
                    lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                    hwndOwner = NativeMethods.IsWindowVisible(mainWindow) ? mainWindow : IntPtr.Zero,
                    lpstrFilter = filter,
                    nFilterIndex = 1,
                    lpstrFile = buf,
                    nMaxFile = MaxPath,
                    lpstrDefExt = defExt,
                    Flags = OFN_PATHMUSTEXIST | OFN_HIDEREADONLY | OFN_NOCHANGEDIR,
                };
                if (GetSaveFileName(ref ofn))
                {
                    string file = Marshal.PtrToStringUni(buf) ?? "";
                    MediaSettings.Set("snapshotFile", file, RegistryValueKind.String);
                    return file;
                }
                if (CommDlgExtendedError() != FNERR_INVALIDFILENAME || initial.Length == 0) return null; // cancel
                initial = "";
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
            Marshal.FreeHGlobal(defExt);
            Marshal.FreeHGlobal(filter);
        }
    }

    static void WriteString(IntPtr buf, string s)
    {
        if (s.Length >= MaxPath) s = "";
        var chars = (s + "\0").ToCharArray();
        Marshal.Copy(chars, 0, buf, chars.Length);
    }
}
