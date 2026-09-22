using System;
using System.Collections.Generic;
using System.Drawing;

namespace RobloxKeeper
{
    // A client window as it is now.
    struct SizedWindow
    {
        public IntPtr Hwnd;
        public Rectangle Bounds;
        public bool Minimized, Maximized;
    }

    // Every client the size of the first, so a box drawn on one - or a click
    // recorded on one - lands exactly on all of them with no projection at
    // all. Done from outside, the way dragging a window's edge would; nothing
    // in Roblox's own files is touched, and each window keeps its place.
    static class WindowSizer
    {
        public static List<KeyValuePair<IntPtr, Size>> Plan(IList<SizedWindow> windows, out string said)
        {
            List<KeyValuePair<IntPtr, Size>> plan = new List<KeyValuePair<IntPtr, Size>>();
            if (windows.Count == 0) { said = "No client window is open - a client in the tray has none."; return plan; }
            if (windows.Count == 1) { said = "Only one client is open, so there is nothing to match."; return plan; }

            SizedWindow first = windows[0];
            if (first.Minimized || first.Maximized)
            {
                said = "Client 1 is minimized or full screen. Make it an ordinary window of the size you want, then try again.";
                return plan;
            }

            Size want = first.Bounds.Size;
            int skipped = 0;
            for (int i = 1; i < windows.Count; i++)
            {
                SizedWindow w = windows[i];
                if (w.Minimized || w.Maximized) { skipped++; continue; }
                if (w.Bounds.Size != want) plan.Add(new KeyValuePair<IntPtr, Size>(w.Hwnd, want));
            }

            string left = skipped == 0 ? "" : " (" + skipped + " left as they are - minimized or full screen)";
            said = plan.Count == 0
                ? "They're already the same size as Client 1" + left + "."
                : "Made " + plan.Count + (plan.Count == 1 ? " client" : " clients") + " the size of Client 1, "
                  + want.Width + " x " + want.Height + left + ".";
            return plan;
        }

        // The windows of these clients, as they are now. Clients without a
        // window - in the tray - are left out.
        public static List<SizedWindow> Measure(IList<WatchedClient> clients)
        {
            List<SizedWindow> list = new List<SizedWindow>();
            foreach (WatchedClient c in clients)
            {
                IntPtr hwnd = WindowCapture.GameWindow(c.Pid);
                if (hwnd == IntPtr.Zero) continue;
                Native.RECT r;
                if (!Native.GetWindowRect(hwnd, out r)) continue;
                SizedWindow s = new SizedWindow();
                s.Hwnd = hwnd;
                s.Bounds = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                s.Minimized = Native.IsIconic(hwnd);
                s.Maximized = Native.IsZoomed(hwnd);
                list.Add(s);
            }
            return list;
        }

        public static void Apply(IList<KeyValuePair<IntPtr, Size>> plan)
        {
            foreach (KeyValuePair<IntPtr, Size> p in plan)
                Native.SetWindowPos(p.Key, IntPtr.Zero, 0, 0, p.Value.Width, p.Value.Height,
                                    Native.SWP_NOMOVE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
    }
}
