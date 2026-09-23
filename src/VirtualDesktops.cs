using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace RobloxKeeper
{
    // Getting back to the virtual desktop you were on.
    //
    // Bringing a client in front makes Windows switch to whichever desktop the
    // client is on. Giving focus back to the window you had only switches back
    // if that window lives on one desktop - the wallpaper and the taskbar are
    // on every desktop, so focusing them changes nothing. Windows offers no
    // documented way to switch desktops, but it keeps the list of desktops and
    // which one is showing in the registry, and it switches one step at a time
    // with Win+Ctrl+Left and Win+Ctrl+Right - the shortcut anyone can press.
    static class VirtualDesktops
    {
        const string KEY = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

        // Windows keeps the desktops as one blob of 16-byte ids, in the order
        // they sit in Task View.
        public static IList<Guid> ParseIds(byte[] blob)
        {
            List<Guid> ids = new List<Guid>();
            if (blob == null) return ids;
            for (int i = 0; i + 16 <= blob.Length; i += 16)
            {
                byte[] one = new byte[16];
                Array.Copy(blob, i, one, 0, 16);
                ids.Add(new Guid(one));
            }
            return ids;
        }

        // Negative is that many to the left, positive to the right. Zero when
        // already there - or when either end isn't in the list, because a
        // guessed direction could take you anywhere.
        public static int StepsBack(IList<Guid> order, Guid home, Guid now)
        {
            if (home == Guid.Empty || now == Guid.Empty || home == now) return 0;
            int from = order.IndexOf(now), to = order.IndexOf(home);
            if (from < 0 || to < 0) return 0;
            return to - from;
        }

        public static IList<Guid> Order()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KEY))
                    return ParseIds(k == null ? null : k.GetValue("VirtualDesktopIDs") as byte[]);
            }
            catch { return new List<Guid>(); }
        }

        // The desktop showing now. Windows 11 keeps it beside the list; Windows
        // 10 keeps it per sign-in session. Empty when there is only the one
        // desktop Windows starts with, or it can't be read.
        public static Guid Current()
        {
            Guid g = Read(KEY, "CurrentVirtualDesktop");
            if (g != Guid.Empty) return g;
            try
            {
                int session = Process.GetCurrentProcess().SessionId;
                return Read(@"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\" + session + @"\VirtualDesktops",
                            "CurrentVirtualDesktop");
            }
            catch { return Guid.Empty; }
        }

        static Guid Read(string key, string value)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key))
                {
                    byte[] b = k == null ? null : k.GetValue(value) as byte[];
                    return b != null && b.Length == 16 ? new Guid(b) : Guid.Empty;
                }
            }
            catch { return Guid.Empty; }
        }

        // Back to `home` if not there already. True when it ends up there.
        // Waits for Windows to finish a switch before judging where it is.
        public static bool ReturnTo(Guid home)
        {
            // The usual case - every client on your desktop - costs nothing.
            if (home == Guid.Empty || Current() == home) return true;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Thread.Sleep(250);
                int steps = StepsBack(Order(), home, Current());
                if (steps == 0) return Current() == home;
                for (int i = 0; i < Math.Abs(steps); i++)
                {
                    Switch(steps < 0);
                    Thread.Sleep(350);
                }
            }
            Thread.Sleep(250);
            return Current() == home;
        }

        // Win+Ctrl+arrow in one go, so nothing typed in between can split it.
        static void Switch(bool left)
        {
            byte arrow = left ? (byte)0x25 : (byte)0x27;    // VK_LEFT, VK_RIGHT
            Native.INPUT[] keys = new Native.INPUT[6];
            Key(keys, 0, 0x5B, false);      // left Windows key
            Key(keys, 1, 0xA2, false);      // left Ctrl
            Key(keys, 2, arrow, false);
            Key(keys, 3, arrow, true);
            Key(keys, 4, 0xA2, true);
            Key(keys, 5, 0x5B, true);
            Native.SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        static void Key(Native.INPUT[] keys, int i, byte vk, bool up)
        {
            keys[i].type = Native.INPUT_KEYBOARD;
            keys[i].U.ki.wVk = vk;
            uint flags = up ? Native.KEYEVENTF_KEYUP : 0u;
            // The Windows key and the arrows are extended keys.
            if (vk == 0x5B || vk == 0x25 || vk == 0x27) flags |= Native.KEYEVENTF_EXTENDEDKEY;
            keys[i].U.ki.dwFlags = flags;
        }
    }
}
