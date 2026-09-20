using System;

namespace RobloxKeeper.Tests
{
    // What a nudge actually sends.
    //
    // Two rules govern every method here. It must leave the character and camera
    // where it found them, because a nudge fires unattended every fifteen
    // minutes and drift accumulates. And it must never send a key that opens
    // chat or a menu - the no-focus probe showed stray keys reaching Roblox's
    // chat box, and an anti-AFK that talks in your games is worse than one that
    // steals focus.
    static class NudgeMethodTests
    {
        const byte VK_W = 0x57, VK_S = 0x53, VK_E = 0x45;
        const byte VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_SLASH = 0xBF, VK_TAB = 0x09;

        // ---------- the methods ----------

        public static void TestEveryMethodSendsSomething()
        {
            for (int i = 0; i < NudgeMethod.Count; i++)
            {
                NudgeStep[] steps = NudgeMethod.StepsFor(i, VK_W);
                Assert.True(steps.Length > 0, "method " + i + " (" + NudgeMethod.Name(i) + ") does something");
            }
        }

        public static void TestEveryMethodHasAName()
        {
            for (int i = 0; i < NudgeMethod.Count; i++)
                Assert.True(!string.IsNullOrEmpty(NudgeMethod.Name(i)), "method " + i + " is named");
        }

        public static void TestZoomGoesOutAndBackIn()
        {
            NudgeStep[] steps = NudgeMethod.StepsFor(NudgeMethod.ZOOM, 0);
            Assert.Equal(2, steps.Length, "two keys");
            Assert.Equal((byte)0x4F, steps[0].Vk, "O first");
            Assert.Equal((byte)0x49, steps[1].Vk, "then I, returning the camera");
        }

        public static void TestCameraTurnsBackTheWayItCame()
        {
            NudgeStep[] steps = NudgeMethod.StepsFor(NudgeMethod.CAMERA, 0);
            Assert.Equal((byte)0x25, steps[0].Vk, "left");
            Assert.Equal((byte)0x27, steps[1].Vk, "then right");
        }

        public static void TestMoveStepsForwardThenBack()
        {
            NudgeStep[] steps = NudgeMethod.StepsFor(NudgeMethod.MOVE, 0);
            Assert.Equal(2, steps.Length, "two taps");
            Assert.Equal(VK_W, steps[0].Vk, "forward");
            Assert.Equal(VK_S, steps[1].Vk, "and back, so the character ends where it started");
        }

        public static void TestJumpIsASingleTap()
        {
            NudgeStep[] steps = NudgeMethod.StepsFor(NudgeMethod.JUMP, 0);
            Assert.Equal(1, steps.Length, "one key");
            Assert.Equal((byte)0x20, steps[0].Vk, "space");
        }

        public static void TestMouseJiggleSendsNoKeys()
        {
            NudgeStep[] steps = NudgeMethod.StepsFor(NudgeMethod.MOUSE, 0);
            foreach (NudgeStep s in steps)
                Assert.True(s.IsMouse, "mouse jiggle sends only mouse input");
        }

        public static void TestMouseJiggleReturnsTheCursor()
        {
            NudgeStep[] steps = NudgeMethod.StepsFor(NudgeMethod.MOUSE, 0);
            int dx = 0, dy = 0;
            foreach (NudgeStep s in steps) { dx += s.Dx; dy += s.Dy; }
            Assert.Equal(0, dx, "net horizontal movement is zero");
            Assert.Equal(0, dy, "net vertical movement is zero");
        }

        public static void TestCustomUsesTheKeyItWasGiven()
        {
            NudgeStep[] steps = NudgeMethod.StepsFor(NudgeMethod.CUSTOM, VK_E);
            Assert.Equal(1, steps.Length, "one tap");
            Assert.Equal(VK_E, steps[0].Vk, "the key the user chose");
        }

        public static void TestCustomWithNoKeySetFallsBackRatherThanSendingNothing()
        {
            NudgeStep[] steps = NudgeMethod.StepsFor(NudgeMethod.CUSTOM, 0);
            Assert.True(steps.Length > 0, "an unset custom key still nudges");
            Assert.True(NudgeKeys.IsSafe(steps[0].Vk), "and the fallback is a safe key");
        }

        public static void TestAnUnknownMethodFallsBackToCamera()
        {
            NudgeStep[] steps = NudgeMethod.StepsFor(999, 0);
            Assert.Equal((byte)0x25, steps[0].Vk, "a settings file with a bad index still works");
        }

        // The rule that matters most, checked across every method at once.
        public static void TestNoMethodCanEverSendAChatOrMenuKey()
        {
            for (int i = 0; i < NudgeMethod.Count; i++)
                foreach (NudgeStep s in NudgeMethod.StepsFor(i, VK_RETURN))
                    if (!s.IsMouse)
                        Assert.True(NudgeKeys.IsSafe(s.Vk),
                            "method " + NudgeMethod.Name(i) + " sent unsafe key 0x" + s.Vk.ToString("X"));
        }

        // ---------- key safety ----------

        public static void TestMovementAndActionKeysAreSafe()
        {
            foreach (byte vk in new byte[] { VK_W, VK_S, 0x41, 0x44, 0x20, 0x45, 0x51, 0x46, 0x25, 0x27 })
                Assert.True(NudgeKeys.IsSafe(vk), "key 0x" + vk.ToString("X") + " is safe to send");
        }

        public static void TestChatAndMenuKeysAreRejected()
        {
            Assert.False(NudgeKeys.IsSafe(VK_RETURN), "Enter sends a chat message");
            Assert.False(NudgeKeys.IsSafe(VK_SLASH), "slash opens the chat box");
            Assert.False(NudgeKeys.IsSafe(VK_ESCAPE), "Escape opens the Roblox menu");
            Assert.False(NudgeKeys.IsSafe(VK_TAB), "Tab opens the player list and moves focus");
        }

        public static void TestModifiersAreRejected()
        {
            // A modifier can stick down and silently change every later keypress.
            foreach (byte vk in new byte[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
                Assert.False(NudgeKeys.IsSafe(vk), "modifier 0x" + vk.ToString("X") + " must not be sendable");
        }

        public static void TestSafeKeyListIsUsableAndEntirelySafe()
        {
            string[] names = NudgeKeys.SafeKeyNames();
            Assert.True(names.Length >= 8, "enough choices to be worth a dropdown");
            foreach (string n in names)
            {
                byte vk = NudgeKeys.VkFor(n);
                Assert.NotEqual((byte)0, vk, "\"" + n + "\" maps to a key");
                Assert.True(NudgeKeys.IsSafe(vk), "\"" + n + "\" is safe");
                Assert.Equal(n, NudgeKeys.NameFor(vk), "\"" + n + "\" round-trips");
            }
        }

        public static void TestAnUnknownKeyNameIsNotTreatedAsAKey()
        {
            Assert.Equal((byte)0, NudgeKeys.VkFor("Ctrl+Alt+Del"), "unknown names map to nothing");
        }
    }
}
