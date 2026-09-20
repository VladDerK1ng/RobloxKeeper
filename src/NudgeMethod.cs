using System;
using System.Collections.Generic;

namespace RobloxKeeper
{
    // One thing a nudge does: tap a key, or move the mouse.
    struct NudgeStep
    {
        public byte Vk;        // 0 for a mouse step
        public int HoldMs;     // how long the key stays down
        public int AfterMs;    // pause before the next step
        public bool IsMouse;
        public int Dx, Dy;     // relative mouse movement

        public static NudgeStep Key(byte vk, int holdMs, int afterMs)
        {
            NudgeStep s = new NudgeStep();
            s.Vk = vk; s.HoldMs = holdMs; s.AfterMs = afterMs;
            return s;
        }

        public static NudgeStep Mouse(int dx, int dy, int afterMs)
        {
            NudgeStep s = new NudgeStep();
            s.IsMouse = true; s.Dx = dx; s.Dy = dy; s.AfterMs = afterMs;
            return s;
        }
    }

    // Which keys a nudge is allowed to send.
    //
    // This is not decoration. Probing whether Roblox could be nudged without
    // focus showed stray keys landing in the chat box rather than the game, and
    // an anti-AFK that types in your games unattended every fifteen minutes is
    // worse than one that briefly steals focus. Anything that opens chat, opens
    // a menu, moves focus, or sticks down as a modifier is refused - including
    // keys the user picks themselves.
    static class NudgeKeys
    {
        // Name -> virtual-key, in the order the dropdown should offer them.
        static readonly string[] Names =
        {
            "W", "A", "S", "D", "E", "Q", "F", "R", "C", "Space",
            "Left arrow", "Right arrow", "Up arrow", "Down arrow", "O", "I"
        };

        static readonly byte[] Codes =
        {
            0x57, 0x41, 0x53, 0x44, 0x45, 0x51, 0x46, 0x52, 0x43, 0x20,
            0x25, 0x27, 0x26, 0x28, 0x4F, 0x49
        };

        public static string[] SafeKeyNames() { return (string[])Names.Clone(); }

        public static byte VkFor(string name)
        {
            for (int i = 0; i < Names.Length; i++)
                if (string.Equals(Names[i], name, StringComparison.OrdinalIgnoreCase)) return Codes[i];
            return 0;
        }

        public static string NameFor(byte vk)
        {
            for (int i = 0; i < Codes.Length; i++)
                if (Codes[i] == vk) return Names[i];
            return null;
        }

        // A whitelist, not a blacklist. A key nobody has thought about is
        // refused rather than sent unattended into somebody's game.
        public static bool IsSafe(byte vk)
        {
            // Letters and digits, minus the ones Roblox binds to chat or menus.
            if (vk >= 0x41 && vk <= 0x5A) return true;        // A-Z
            if (vk >= 0x30 && vk <= 0x39) return true;        // 0-9
            if (vk == 0x20) return true;                      // Space
            if (vk >= 0x25 && vk <= 0x28) return true;        // arrows
            return false;
        }
    }

    // The available nudge actions.
    //
    // Every two-key method sends a movement and its opposite, so a client parked
    // for eight hours ends where it started rather than slowly rotating or
    // walking away.
    static class NudgeMethod
    {
        public const int ZOOM = 0;
        public const int CAMERA = 1;
        public const int JUMP = 2;
        public const int MOVE = 3;
        public const int MOUSE = 4;
        public const int CUSTOM = 5;
        public const int Count = 6;

        static readonly string[] Names =
        {
            "Zoom out + in  (O, I)",
            "Turn camera  (left, right)",
            "Jump  (Space)",
            "Step forward + back  (W, S)",
            "Mouse jiggle",
            "Custom key"
        };

        public static string Name(int index)
        {
            return index >= 0 && index < Names.Length ? Names[index] : Names[CAMERA];
        }

        public static string[] AllNames() { return (string[])Names.Clone(); }

        // How long a sequence holds the foreground. Every millisecond here is
        // a millisecond stolen from whatever the user was doing, so it is worth
        // being able to assert a budget on it.
        public static int TotalMs(NudgeStep[] steps)
        {
            int total = 0;
            foreach (NudgeStep s in steps) total += s.HoldMs + s.AfterMs;
            return total;
        }

        // Holds are 60-70ms: several frames at 60fps, so the client cannot miss
        // one between frames, but short enough that the whole nudge reads as a
        // flicker. The original 180ms holds with 250-350ms gaps added up to
        // roughly 1.4 seconds of stolen focus per client, which in a
        // competitive game is a lost fight.
        public static NudgeStep[] StepsFor(int method, byte customVk)
        {
            switch (method)
            {
                case ZOOM:
                    return new NudgeStep[] {
                        NudgeStep.Key(0x4F, 60, 120),     // O - zoom out
                        NudgeStep.Key(0x49, 60, 0)        // I - and back in
                    };

                case JUMP:
                    return new NudgeStep[] { NudgeStep.Key(0x20, 70, 0) };

                case MOVE:
                    // A short step and a step back. Registers as character
                    // movement in games that check the player actually moved,
                    // not merely that a key was pressed.
                    return new NudgeStep[] {
                        NudgeStep.Key(0x57, 90, 90),      // W
                        NudgeStep.Key(0x53, 90, 0)        // S
                    };

                case MOUSE:
                    // Out and back along both axes, netting zero.
                    return new NudgeStep[] {
                        NudgeStep.Mouse(18, 0, 50),
                        NudgeStep.Mouse(0, 14, 50),
                        NudgeStep.Mouse(-18, 0, 50),
                        NudgeStep.Mouse(0, -14, 0)
                    };

                case CUSTOM:
                    // An unset or unsafe key falls back rather than sending
                    // nothing - a silent no-op here means an idle kick later.
                    if (customVk == 0 || !NudgeKeys.IsSafe(customVk))
                        return StepsFor(CAMERA, 0);
                    return new NudgeStep[] { NudgeStep.Key(customVk, 80, 0) };

                default:
                    return new NudgeStep[] {
                        NudgeStep.Key(0x25, 70, 120),     // left
                        NudgeStep.Key(0x27, 70, 0)        // right
                    };
            }
        }
    }
}
