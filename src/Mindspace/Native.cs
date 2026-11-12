using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mindspace;

/// <summary>All Win32 interop lives here so the unsafe surface is one reviewable file.</summary>
internal static class Native
{
    public const int GWL_EXSTYLE = -20, GWL_STYLE = -16;
    public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_APPWINDOW = 0x40000, WS_EX_TOPMOST = 0x8;
    public const int WS_VISIBLE = 0x10000000, WS_CAPTION = 0x00C00000;
    public const int WM_HOTKEY = 0x0312, WM_CLIPBOARDUPDATE = 0x031D;
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_NOREPEAT = 0x4000;
    public const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
    public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4, SW_SHOW = 5, SW_RESTORE = 9, SW_MINIMIZE = 6;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint value);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(nint hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hWnd, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowW(string? cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowExW(nint parent, nint after, string? cls, string? title);
    [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(nint hWnd);
    [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(nint hWnd);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(nint hIcon);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, nint lParam);
    delegate bool EnumProc(nint hWnd, nint lParam);

    public delegate void WinEventProc(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] public static extern nint SetWinEventHook(uint min, uint max, nint mod, WinEventProc cb, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(nint hook);

    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint hwnd, int attr, out int val, int size);

    [StructLayout(LayoutKind.Sequential)] struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX s);
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO { public nint hIcon; public int iIcon; public uint dwAttributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern nint SHGetFileInfoW(string path, uint attrs, ref SHFILEINFO info, uint cb, uint flags);

    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public KEYBDINPUT ki; public ulong pad; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nint dwExtraInfo; }
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

    // ---------- helpers ----------

    public static nint Handle(Window w) => new WindowInteropHelper(w).EnsureHandle();

    public static void AddExStyle(Window w, int flags)
    {
        var h = Handle(w);
        SetWindowLongPtr(h, GWL_EXSTYLE, GetWindowLongPtr(h, GWL_EXSTYLE) | flags);
    }

    public static string WindowTitle(nint hWnd)
    {
        var sb = new StringBuilder(260);
        GetWindowTextW(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string ProcessName(nint hWnd)
    {
        try
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; }
    }

    public static string? ProcessPath(nint hWnd)
    {
        try
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.MainModule?.FileName;
        }
        catch { return null; }
    }

    /// <summary>Top-level windows the user would expect on a taskbar (alt-tab rules).</summary>
    public static List<nint> TaskbarWindows()
    {
        var list = new List<nint>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            if (GetWindow(h, 4 /*GW_OWNER*/) != 0) return true;
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return true;
            if (DwmGetWindowAttribute(h, 14 /*DWMWA_CLOAKED*/, out int cloaked, 4) == 0 && cloaked != 0) return true;
            if (WindowTitle(h).Length == 0) return true;
            list.Add(h);
            return true;
        }, 0);
        return list;
    }

    public static ImageSource? IconForPath(string path)
    {
        var info = new SHFILEINFO();
        var r = SHGetFileInfoW(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100 /*SHGFI_ICON*/ | 0x0 /*large*/);
        if (r == 0 || info.hIcon == 0) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally { DestroyIcon(info.hIcon); }
    }

    public static (double usedGb, double totalGb, int percent) Memory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref m);
        double total = m.ullTotalPhys / 1073741824.0, avail = m.ullAvailPhys / 1073741824.0;
        return (total - avail, total, (int)m.dwMemoryLoad);
    }

    static long _pIdle, _pKernel, _pUser;
    /// <summary>System-wide CPU % since the previous call. Costs one syscall.</summary>
    public static double CpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        long dIdle = idle - _pIdle, dTotal = (kernel - _pKernel) + (user - _pUser);
        bool first = _pKernel == 0;
        _pIdle = idle; _pKernel = kernel; _pUser = user;
        if (first || dTotal <= 0) return 0;
        return Math.Clamp(100.0 * (dTotal - dIdle) / dTotal, 0, 100);
    }

    public static TimeSpan Uptime() => TimeSpan.FromMilliseconds(GetTickCount64());

    static INPUT Key(ushort vk, bool up) => new() { type = 1, ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0u } };

    /// <summary>Sends Ctrl+V to whatever window has focus. Only ever called from an explicit user click.</summary>
    public static void SendPaste()
    {
        // Don't fight with modifier keys the user may still be holding from the hotkey.
        var inputs = new List<INPUT>();
        foreach (ushort vk in new ushort[] { 0x12 /*alt*/, 0x10 /*shift*/, 0x5B /*win*/ })
            if ((GetAsyncKeyState(vk) & 0x8000) != 0) inputs.Add(Key(vk, true));
        inputs.AddRange([Key(0x11, false), Key(0x56, false), Key(0x56, true), Key(0x11, true)]);
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    public static void TapWinKey() =>
        SendInput(2, [Key(0x5B, false), Key(0x5B, true)], Marshal.SizeOf<INPUT>());

    // ---------- taskbar (used only by the optional Dock; never persisted) ----------

    public static void SetTaskbarVisible(bool visible)
    {
        int cmd = visible ? SW_SHOWNOACTIVATE : SW_HIDE;
        var tray = FindWindowW("Shell_TrayWnd", null);
        if (tray != 0) ShowWindow(tray, cmd);
        nint sec = 0;
        while ((sec = FindWindowExW(0, sec, "Shell_SecondaryTrayWnd", null)) != 0) ShowWindow(sec, cmd);
    }

    public static bool IsTaskbarVisible()
    {
        var tray = FindWindowW("Shell_TrayWnd", null);
        return tray != 0 && IsWindowVisible(tray);
    }
}
