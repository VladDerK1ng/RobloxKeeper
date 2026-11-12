using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RobloxKeeper
{
    // What the on-screen extras ask of Windows. Kept apart from Native so
    // that these windows - which sit on top of the game and must never take
    // a click, a key or the foreground from it - can be read in one place.
    //
    // Nothing here opens the Roblox process. A window's place is asked of
    // Windows by its handle, as any window manager does; events about the
    // window in front arrive out-of-context, which means in this process,
    // with nothing loaded into anybody else's.
    static class Xn
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int W, H; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte Op, Flags, Alpha, Format; }

        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                         WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
        public const int WM_MOUSEACTIVATE = 0x21, WM_NCHITTEST = 0x84, WM_HOTKEY = 0x312;
        public const int MA_NOACTIVATE = 3, HTTRANSPARENT = -1;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017;
        public const uint WINEVENT_OUTOFCONTEXT = 0;
        public const int ULW_ALPHA = 2;
        public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;
        public const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int cy, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint evMin, uint evMax, IntPtr mod, WinEventProc cb, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dstDc, ref POINT dst, ref SIZE size, IntPtr srcDc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);

        // ----- keys -----

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }
        public delegate IntPtr KeyHookProc(int code, IntPtr wParam, IntPtr lParam);
        public const int WH_KEYBOARD_LL = 13;
        public const uint LLKHF_INJECTED = 0x10, LLKHF_UP = 0x80;

        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookExW(int id, KeyHookProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandleW(IntPtr name);

        // ----- zoom: the magnifier that is part of Windows -----

        [DllImport("Magnification.dll")] public static extern bool MagInitialize();
        [DllImport("Magnification.dll")] public static extern bool MagUninitialize();
        [DllImport("Magnification.dll")] public static extern bool MagSetFullscreenTransform(float level, int xOffset, int yOffset);

        // ----- what Task Manager shows about a process -----
        //
        // Asked with the "limited information" right, which lets a program
        // read another's times and memory totals and nothing else: not its
        // memory, not its threads.

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_MEMORY_COUNTERS
        {
            public uint cb, PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                           QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
        }
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool GetProcessTimes(IntPtr h, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll")] public static extern bool K32GetProcessMemoryInfo(IntPtr h, out PROCESS_MEMORY_COUNTERS c, uint size);

        // ----- drawing text in a font that is only a file -----
        //
        // Through GDI rather than GDI+: GDI reads OpenType fonts with
        // PostScript outlines (most .otf files), which GDI+ cannot.

        public const uint FR_PRIVATE = 0x10;
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern int AddFontResourceExW(string file, uint flags, IntPtr reserved);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern bool RemoveFontResourceExW(string file, uint flags, IntPtr reserved);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline,
            uint strike, uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
        [DllImport("gdi32.dll")] public static extern int SetBkMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] public static extern uint SetTextColor(IntPtr dc, uint colour);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern int GetTextFaceW(IntPtr dc, int max, StringBuilder name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int DrawTextW(IntPtr dc, string text, int count, ref RECT r, uint format);

        // ----- shared small things -----

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public static string ClassOf(IntPtr hwnd)
        {
            StringBuilder sb = new StringBuilder(64);
            return GetClassNameW(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        }

        // This thread's windows are measured in real pixels from here on.
        //
        // The app itself is laid out for a display at 100% and leaves Windows
        // to stretch it; a window that has to sit on the exact middle of the
        // game cannot be stretched, or the middle it is told about is not the
        // one on the glass. False on a Windows too old to offer it - there
        // the places it is given are still right at 100%.
        public static bool RealPixelsOnThisThread()
        {
            try
            {
                if (SetThreadDpiAwarenessContext(new IntPtr(-4)) != IntPtr.Zero) return true;   // per monitor, v2
                return SetThreadDpiAwarenessContext(new IntPtr(-3)) != IntPtr.Zero;              // per monitor
            }
            catch { return false; }
        }
    }
}
