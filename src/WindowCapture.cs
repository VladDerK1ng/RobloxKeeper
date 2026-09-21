using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace RobloxKeeper
{
    // Whether a client can be watched at all.
    enum WatchState
    {
        Watchable,

        // In the tray, or still launching. There is no game window.
        NotInAGame,

        // There is a window, but Windows has stopped drawing it.
        Minimized
    }

    // Taking a picture of a Roblox window without touching it.
    //
    // Roblox draws through Direct3D, and the ordinary PrintWindow call hands
    // back a rectangle that looks full but contains no game. Measured on a
    // live client: with the plain flag the recogniser read 0 characters, with
    // PW_RENDERFULLCONTENT it read 276. The difference is that the second
    // asks the desktop compositor for the frame it is already holding rather
    // than asking the window to paint itself.
    //
    // Because the frame comes from the compositor, this works on a window
    // that is behind everything else, and on one parked on another virtual
    // desktop - measured over 26 seconds of being cloaked by the shell, every
    // frame still arrived. It does NOT work on a minimized window, because
    // the compositor stops updating one, and that is why StateOf exists.
    static class WindowCapture
    {
        // Ask the compositor for what it already has. Without this flag a
        // Direct3D window comes back empty.
        const uint PW_RENDERFULLCONTENT = 2;

        // Roblox's game window. Its other windows - the tray icon host, the
        // input method stubs - share the process and must not be mistaken for
        // it.
        const string GAME_WINDOW_CLASS = "WINDOWSCLIENT";

        // Below this a window is still launching rather than showing a game.
        const int MIN_USEFUL_SIDE = 200;

        [DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumProc callback, IntPtr param);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);

        delegate bool EnumProc(IntPtr hwnd, IntPtr param);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        // Can this client be read, and if not, why not?
        //
        // Separated out so it can be tested, and because the answer has to
        // reach the user. A client that silently reports nothing forever
        // looks exactly like a watcher that does not work.
        public static WatchState StateOf(bool hasWindow, bool minimized, int width, int height)
        {
            if (!hasWindow) return WatchState.NotInAGame;
            if (width < MIN_USEFUL_SIDE || height < MIN_USEFUL_SIDE) return WatchState.NotInAGame;
            if (minimized) return WatchState.Minimized;
            return WatchState.Watchable;
        }

        public static string Explain(WatchState state)
        {
            switch (state)
            {
                case WatchState.Minimized:
                    return "Minimized - Windows stops drawing a minimized window, "
                         + "so there is nothing to read. Restore it and it can stay "
                         + "behind your other windows.";
                case WatchState.NotInAGame:
                    return "Not in a game yet - nothing to watch until it joins one.";
                default:
                    return "Being watched.";
            }
        }

        public static WatchState StateOf(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return WatchState.NotInAGame;
            RECT r;
            if (!GetWindowRect(hwnd, out r)) return WatchState.NotInAGame;
            return StateOf(true, Native.IsIconic(hwnd), r.Right - r.Left, r.Bottom - r.Top);
        }

        // The game window belonging to a process, or zero when it has none.
        //
        // Process.MainWindowHandle is not enough: a client running in the tray
        // reports zero even though it owns several windows, and one of those
        // is a 16x16 tray host that would be captured instead.
        public static IntPtr GameWindow(int pid)
        {
            if (pid <= 0) return IntPtr.Zero;

            IntPtr found = IntPtr.Zero;
            try
            {
                EnumWindows(delegate(IntPtr hwnd, IntPtr param)
                {
                    uint owner;
                    Native.GetWindowThreadProcessId(hwnd, out owner);
                    if (owner != (uint)pid) return true;
                    if (ClassOf(hwnd) != GAME_WINDOW_CLASS) return true;

                    RECT r;
                    if (!GetWindowRect(hwnd, out r)) return true;
                    if (r.Right - r.Left < MIN_USEFUL_SIDE) return true;

                    found = hwnd;
                    return false;       // stop looking
                }, IntPtr.Zero);
            }
            catch { return IntPtr.Zero; }

            return found;
        }

        // The window's pixels, or null when there is nothing to take a
        // picture of. Never throws - a client can close between being listed
        // and being captured, and that is an ordinary thing to happen rather
        // than a fault.
        public static Pixels Grab(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;

            try
            {
                RECT r;
                if (!GetWindowRect(hwnd, out r)) return null;

                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                if (w <= 0 || h <= 0) return null;

                using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        try { PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT); }
                        finally { g.ReleaseHdc(hdc); }
                    }
                    return Pixels.FromBitmap(bmp);
                }
            }
            catch { return null; }
        }

        static string ClassOf(IntPtr hwnd)
        {
            StringBuilder name = new StringBuilder(64);
            GetClassName(hwnd, name, name.Capacity);
            return name.ToString();
        }
    }
}
