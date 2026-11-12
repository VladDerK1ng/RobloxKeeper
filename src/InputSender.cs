using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace RobloxKeeper
{
    // Keyboard nudges and window focus. Roblox reads raw input, so everything
    // here goes through SendInput rather than posted messages.
    static class InputSender
    {
        // Carried by every key and mouse event this app sends, so its own
        // listeners can leave out what it sent itself and nothing else. A key
        // sent by mouse software - a side button bound to F6 - is sent by a
        // program too, and is still somebody pressing something.
        public const int OURS = 0x524B6B70;

        public static bool IsOurs(IntPtr extraInfo)
        {
            return extraInfo.ToInt64() == OURS;
        }

        // The one way this app sends input: marked, then sent.
        public static uint Send(Native.INPUT[] inputs)
        {
            IntPtr mark = new IntPtr(OURS);
            for (int i = 0; i < inputs.Length; i++)
            {
                if (inputs[i].type == Native.INPUT_KEYBOARD) inputs[i].U.ki.dwExtraInfo = mark;
                else if (inputs[i].type == Native.INPUT_MOUSE) inputs[i].U.mi.dwExtraInfo = mark;
            }
            return Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        public static void FocusWindow(IntPtr hwnd)
        {
            // A quick Alt tap makes Windows allow the foreground switch.
            SendVk(Native.VK_LMENU, true);
            SendVk(Native.VK_LMENU, false);
            Native.SetForegroundWindow(hwnd);
            Thread.Sleep(120);
            if (Native.GetForegroundWindow() != hwnd)
            {
                uint pid;
                uint target = Native.GetWindowThreadProcessId(hwnd, out pid);
                uint mine = Native.GetCurrentThreadId();
                Native.AttachThreadInput(mine, target, true);
                Native.SetForegroundWindow(hwnd);
                Native.AttachThreadInput(mine, target, false);
                Thread.Sleep(120);
            }
        }

        public static void ShowClient(IntPtr hwnd)
        {
            if (Native.IsIconic(hwnd)) { Native.ShowWindow(hwnd, Native.SW_RESTORE); Thread.Sleep(150); }
            FocusWindow(hwnd);
        }

        public static void TapKey(byte vk, int holdMs)
        {
            SendScan(vk, true);
            Thread.Sleep(holdMs);
            SendScan(vk, false);
        }

        // Relative mouse movement, the same way a real mouse reports it.
        public static void MoveMouse(int dx, int dy)
        {
            Native.INPUT[] inp = new Native.INPUT[1];
            inp[0].type = Native.INPUT_MOUSE;
            inp[0].U.mi.dx = dx;
            inp[0].U.mi.dy = dy;
            inp[0].U.mi.dwFlags = Native.MOUSEEVENTF_MOVE;
            InputSender.Send(inp);
        }

        // Plays one nudge method against whatever window currently has focus.
        public static void Perform(NudgeStep[] steps)
        {
            foreach (NudgeStep step in steps)
            {
                if (step.IsMouse) MoveMouse(step.Dx, step.Dy);
                else TapKey(step.Vk, step.HoldMs);
                if (step.AfterMs > 0) Thread.Sleep(step.AfterMs);
            }
        }

        // Scan-code input: what games reading raw/hardware input actually listen for.
        // Arrow keys are extended keys and need the E0 flag, or they read as numpad.
        public static void SendScan(byte vk, bool down)
        {
            Native.INPUT[] inp = new Native.INPUT[1];
            inp[0].type = Native.INPUT_KEYBOARD;
            inp[0].U.ki.wVk = 0;
            inp[0].U.ki.wScan = (ushort)Native.MapVirtualKey(vk, 0);
            uint flags = Native.KEYEVENTF_SCANCODE;
            if (vk >= 0x21 && vk <= 0x2E) flags |= Native.KEYEVENTF_EXTENDEDKEY;
            if (!down) flags |= Native.KEYEVENTF_KEYUP;
            inp[0].U.ki.dwFlags = flags;
            InputSender.Send(inp);
        }

        public static void SendVk(byte vk, bool down)
        {
            Native.INPUT[] inp = new Native.INPUT[1];
            inp[0].type = Native.INPUT_KEYBOARD;
            inp[0].U.ki.wVk = vk;
            inp[0].U.ki.dwFlags = down ? 0u : Native.KEYEVENTF_KEYUP;
            InputSender.Send(inp);
        }
    }
}
