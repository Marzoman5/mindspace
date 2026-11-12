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

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int val, int size);
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

    static readonly HashSet<string> Terminals = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal", "wt", "cmd", "powershell", "pwsh", "conhost", "OpenConsole", "bash", "wsl", "mintty", "alacritty",
        "wezterm-gui", "putty", "kitty", "Hyper", "Tabby", "ConEmu64", "ConEmu", "git-bash", "ubuntu", "debian",
        // remote sessions and VM consoles: keystrokes there land on another machine
        "mstsc", "vmconnect", "vmware", "VirtualBoxVM", "MobaXterm", "SecureCRT", "Xshell", "ttermpro", "WinSCP",
    };

    /// <summary>
    /// Terminals are special: pasted text can run as commands and Ctrl+C interrupts whatever is running.
    /// Mindspace therefore never sends keystrokes to them; the user pastes there by hand.
    /// </summary>
    public static bool IsTerminal(nint hWnd) => hWnd != 0 && Terminals.Contains(ProcessName(hWnd));

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

    /// <summary>Raw counters (idle, kernel+user; kernel time includes idle) for callers that keep their own baseline.</summary>
    public static (long idle, long total) CpuTimes() => GetSystemTimes(out var idle, out var kernel, out var user) ? (idle, kernel + user) : (0, 0);

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

    /// <summary>True while Ctrl, Alt, Shift or Win is physically held.</summary>
    public static bool ModifiersDown() => new[] { 0x11, 0x12, 0x10, 0x5B, 0x5C }.Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0);

    /// <summary>Sends Ctrl+C to the focused window. Only called from the user's own "use my selection" hotkeys.</summary>
    public static void SendCopy() =>
        SendInput(4, [Key(0x11, false), Key(0x43, false), Key(0x43, true), Key(0x11, true)], Marshal.SizeOf<INPUT>());

    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();

    public static void TapWinKey() =>
        SendInput(2, [Key(0x5B, false), Key(0x5B, true)], Marshal.SizeOf<INPUT>());

    // ---------- frosted glass + window shape ----------

    [StructLayout(LayoutKind.Sequential)] struct ACCENT_POLICY { public int AccentState, AccentFlags; public uint GradientColor; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)] struct WCA_DATA { public int Attribute; public nint Data; public int SizeOfData; }
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(nint hwnd, ref WCA_DATA data);

    /// <summary>
    /// Asks the desktop compositor to blur whatever is behind this window (the compositor does the work, on the GPU it
    /// already uses for the desktop; nothing is captured or read by Mindspace). Returns false where unsupported.
    /// </summary>
    public static bool SetFrost(Window w, bool on, uint tintAbgr = 0x30101018)
    {
        try
        {
            var policy = new ACCENT_POLICY { AccentState = on ? 4 /*acrylic blur behind*/ : 0, AccentFlags = 0, GradientColor = tintAbgr };
            int size = Marshal.SizeOf<ACCENT_POLICY>();
            nint mem = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, mem, false);
                var data = new WCA_DATA { Attribute = 19 /*WCA_ACCENT_POLICY*/, Data = mem, SizeOfData = size };
                return SetWindowCompositionAttribute(Handle(w), ref data) != 0;
            }
            finally { Marshal.FreeHGlobal(mem); }
        }
        catch { return false; }
    }

    /// <summary>
    /// Frosts a borderless window and lets Windows 11 round its corners (its fixed 8 px radius), so the blur has
    /// exactly the shape of the glass. The window must be the size of the glass itself.
    /// </summary>
    public static bool Frost(Window w)
    {
        if (!SetFrost(w, true)) return false;
        int round = 2 /*DWMWCP_ROUND*/, none = unchecked((int)0xFFFFFFFE) /*DWMWA_COLOR_NONE*/;
        DwmSetWindowAttribute(Handle(w), 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref round, 4);
        DwmSetWindowAttribute(Handle(w), 34 /*DWMWA_BORDER_COLOR*/, ref none, 4);
        return true;
    }

    // ---------- window geometry + taskbar auto-hide (used only by the optional Dock) ----------

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hWnd, out RECT r);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(nint hWnd, StringBuilder sb, int max);
    [StructLayout(LayoutKind.Sequential)] struct APPBARDATA { public int cbSize; public nint hWnd; public uint uCallbackMessage, uEdge; public RECT rc; public nint lParam; }
    [DllImport("shell32.dll")] static extern nuint SHAppBarMessage(uint msg, ref APPBARDATA data);

    public static string ClassName(nint hWnd) { var sb = new StringBuilder(80); GetClassNameW(hWnd, sb, sb.Capacity); return sb.ToString(); }
    public static (int w, int h) ScreenPixels() => (GetSystemMetrics(0), GetSystemMetrics(1));

    /// <summary>Whether the Windows taskbar is set to hide itself (the same switch as in Taskbar settings).</summary>
    public static bool TaskbarAutoHide
    {
        get { var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() }; return (SHAppBarMessage(4 /*ABM_GETSTATE*/, ref abd) & 1) != 0; }
        set
        {
            if (TaskbarAutoHide == value) return;
            var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = FindWindowW("Shell_TrayWnd", null), lParam = value ? 1 /*ABS_AUTOHIDE*/ : 2 /*ABS_ALWAYSONTOP*/ };
            SHAppBarMessage(10 /*ABM_SETSTATE*/, ref abd);
        }
    }

    // ---------- taskbar (used only by the optional Dock; never persisted) ----------

    public static void SetTaskbarVisible(bool visible)
    {
        int cmd = visible ? SW_SHOWNOACTIVATE : SW_HIDE;
        var tray = FindWindowW("Shell_TrayWnd", null);
        if (tray != 0) ShowWindow(tray, cmd);
        nint sec = 0;
        while ((sec = FindWindowExW(0, sec, "Shell_SecondaryTrayWnd", null)) != 0) ShowWindow(sec, cmd);
    }

    // ---------- microphone (winmm wave-in; used only by Services/Speech.cs while the user is dictating) ----------

    [StructLayout(LayoutKind.Sequential)] public struct WAVEFORMATEX { public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }
    [StructLayout(LayoutKind.Sequential)] public struct WAVEHDR { public nint lpData; public uint dwBufferLength, dwBytesRecorded; public nint dwUser; public uint dwFlags, dwLoops; public nint lpNext, reserved; }
    [DllImport("winmm.dll")] public static extern uint waveInGetNumDevs();
    [DllImport("winmm.dll")] public static extern int waveInOpen(out nint h, uint device, ref WAVEFORMATEX fmt, nint callback, nint instance, uint flags);
    [DllImport("winmm.dll")] public static extern int waveInPrepareHeader(nint h, nint hdr, int size);
    [DllImport("winmm.dll")] public static extern int waveInUnprepareHeader(nint h, nint hdr, int size);
    [DllImport("winmm.dll")] public static extern int waveInAddBuffer(nint h, nint hdr, int size);
    [DllImport("winmm.dll")] public static extern int waveInStart(nint h);
    [DllImport("winmm.dll")] public static extern int waveInReset(nint h);
    [DllImport("winmm.dll")] public static extern int waveInClose(nint h);

    // ---------- screen skills (used only by Services/ScreenSkills.cs, only after the user presses Start) ----------

    [DllImport("user32.dll")] static extern bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern nint ChildWindowFromPointEx(nint parent, POINT pt, uint flags);
    [DllImport("user32.dll")] static extern bool ScreenToClient(nint hWnd, ref POINT pt);
    [DllImport("user32.dll")] static extern nint GetDC(nint hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint hWnd, nint dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleBitmap(nint dc, int w, int h);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
    [DllImport("gdi32.dll")] static extern int GetDIBits(nint dc, nint bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint hWnd);
    public static double DpiScale(nint hwnd) { uint d = GetDpiForWindow(hwnd); return d == 0 ? 1 : d / 96.0; }

    public static bool KeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// Scrolls a window by posting it a mouse-wheel message, the way a wheel turn over its centre would arrive.
    /// Does not move the pointer, take focus or type. notches &gt; 0 scrolls up.
    /// </summary>
    public static void WheelScroll(nint hwnd, int notches)
    {
        if (!GetWindowRect(hwnd, out var r)) return;
        var screen = new POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };
        var client = screen;
        ScreenToClient(hwnd, ref client);
        nint target = ChildWindowFromPointEx(hwnd, client, 0x0001 | 0x0002 /*skip invisible, disabled*/);
        if (target == 0) target = hwnd;
        nint wParam = (nint)((notches * 120) << 16);
        nint lParam = (nint)(((screen.Y & 0xFFFF) << 16) | (screen.X & 0xFFFF));
        PostMessageW(target, 0x020A /*WM_MOUSEWHEEL*/, wParam, lParam);
    }

    /// <summary>Taps one key in the focused window. Only the autoplay skill calls this, for the single key the user chose.</summary>
    public static void TapKey(ushort vk) => SendInput(2, [Key(vk, false), Key(vk, true)], Marshal.SizeOf<INPUT>());

    /// <summary>Copies a rectangle of the screen as 32-bit BGRA rows (top-down). In memory only; never saved.</summary>
    public static byte[]? Grab(int x, int y, int w, int h)
    {
        nint screen = GetDC(0), mem = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, w, h), old = SelectObject(mem, bmp);
        try
        {
            if (!BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020 /*SRCCOPY*/)) return null;
            var info = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var bits = new byte[w * h * 4];
            SelectObject(mem, old);
            return GetDIBits(screen, bmp, 0, (uint)h, bits, ref info, 0) == 0 ? null : bits;
        }
        finally { SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(0, screen); }
    }

    public static bool IsTaskbarVisible()
    {
        var tray = FindWindowW("Shell_TrayWnd", null);
        return tray != 0 && IsWindowVisible(tray);
    }
}
