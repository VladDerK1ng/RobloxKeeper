using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // The autoclicker's key, the screenshot key and the rules' keys are heard
    // by a hook that lets every key through. Which presses count is worked out
    // by KeyPressTracker, tested here without a hook.
    static class GlobalKeysTests
    {
        const byte F6 = 0x75, F7 = 0x76;
        const byte LSHIFT = 0xA0, LCTRL = 0xA2, RCTRL = 0xA3, LALT = 0xA4, LWIN = 0x5B;

        static KeyPressTracker Tracking(params int[] keys)
        {
            KeyPressTracker t = new KeyPressTracker();
            t.Watch(new List<int>(keys));
            return t;
        }

        static string Said(HeardKey h)
        {
            return h == null ? "nothing" : (h.Down ? "down " : "up ") + h.Key;
        }

        // Windows drops a hook that answers late, without a word, so it is
        // set again every so often: the fresh one first, then the old one
        // off, so no key goes unheard in between.
        public static void TestAFreshHookIsSetBeforeTheOldOneComesOff()
        {
            List<string> done = new List<string>();
            IntPtr fresh = GlobalKeys.Rehook(new IntPtr(11),
                delegate { done.Add("hook"); return new IntPtr(22); },
                delegate(IntPtr h) { done.Add("unhook " + h.ToInt32()); });
            Assert.Equal(22, fresh.ToInt32(), "the fresh one is kept");
            Assert.Equal("hook,unhook 11", string.Join(",", done.ToArray()), "set first, the old one off after");

            done.Clear();
            fresh = GlobalKeys.Rehook(new IntPtr(11), delegate { return IntPtr.Zero; },
                delegate(IntPtr h) { done.Add("unhook"); });
            Assert.Equal(0, fresh.ToInt32(), "refused: said so");
            Assert.Equal(0, done.Count, "and the old one is left alone");

            fresh = GlobalKeys.Rehook(new IntPtr(11), delegate { throw new InvalidOperationException(); },
                delegate(IntPtr h) { done.Add("unhook"); });
            Assert.Equal(0, fresh.ToInt32(), "a hook that throws is refused too");
            Assert.Equal(0, done.Count, "and the old one is still left alone");
            Assert.True(GlobalKeys.REHOOK_MS >= 30000 && GlobalKeys.REHOOK_MS <= 5 * 60 * 1000, "every few minutes");
        }

        // Should the old hook and the fresh one ever both hear a key, it is
        // still one press.
        public static void TestTheSameKeyHeardTwiceIsOnePress()
        {
            KeyPressTracker t = Tracking(F6);
            Assert.Equal("down " + F6, Said(t.Feed(F6, true, false, 1000)), "F6 pressed");
            Assert.Equal("nothing", Said(t.Feed(F6, true, false, 1000)), "the same down again");
            Assert.Equal("up " + F6, Said(t.Feed(F6, false, false, 1080)), "let go");
            Assert.Equal("nothing", Said(t.Feed(F6, false, false, 1080)), "the same up again");
        }

        public static void TestAPressIsHeardAndSoIsItsUp()
        {
            KeyPressTracker t = Tracking(F6);
            Assert.Equal("down " + F6, Said(t.Feed(F6, true, false, 1000)), "F6 pressed");
            Assert.Equal("up " + F6, Said(t.Feed(F6, false, false, 1080)), "and let go");
            Assert.Equal("nothing", Said(t.Feed(F7, true, false, 2000)), "a key nobody listens for");
            Assert.Equal("nothing", Said(t.Feed(F7, false, false, 2050)), "nor its up");
        }

        // The app's own macros and autoclicker press keys too; none of them
        // is anybody pressing anything.
        public static void TestKeysThisAppSentAreLeftOut()
        {
            KeyPressTracker t = Tracking(F6, RuleHotkeys.Key(F7, true, false, false));
            Assert.Equal("nothing", Said(t.Feed(F6, true, true, 1000)), "an F6 this app sent");
            Assert.Equal("nothing", Said(t.Feed(F6, false, true, 1050)), "and its up");
            Assert.Equal("down " + F6, Said(t.Feed(F6, true, false, 1100)), "a real F6 straight after is heard");

            // A Ctrl held down by a macro does not make the person's F7 into Ctrl+F7.
            t.Feed(F6, false, false, 1150);
            t.Feed(LCTRL, true, true, 1200);
            Assert.Equal("nothing", Said(t.Feed(F7, true, false, 1300)), "plain F7 is not listened for");
        }

        // Windows marks every key a program sends as injected - this app's,
        // and mouse software's that turns a side button into F6. Only this
        // app's own mark is left out; the side button still works.
        public static void TestOnlyThisAppsOwnKeysAreLeftOut()
        {
            IntPtr ours = new IntPtr(InputSender.OURS);
            Assert.True(KeyPressTracker.SentByUs(Native.LLKHF_INJECTED, ours), "injected with this app's mark");
            Assert.False(KeyPressTracker.SentByUs(Native.LLKHF_INJECTED, IntPtr.Zero), "injected by another program");
            Assert.False(KeyPressTracker.SentByUs(Native.LLKHF_INJECTED, new IntPtr(1234)), "another program's own mark");
            Assert.False(KeyPressTracker.SentByUs(0, IntPtr.Zero), "a real key");

            KeyPressTracker t = Tracking(F6);
            Assert.Equal("nothing", Said(t.Feed(F6, true, KeyPressTracker.SentByUs(Native.LLKHF_INJECTED, ours), 1000)), "this app's F6");
            t.Feed(F6, false, KeyPressTracker.SentByUs(Native.LLKHF_INJECTED, ours), 1050);
            Assert.Equal("down " + F6, Said(t.Feed(F6, true, KeyPressTracker.SentByUs(Native.LLKHF_INJECTED, IntPtr.Zero), 1100)),
                         "mouse software's F6 is heard");
            Assert.Equal("up " + F6, Said(t.Feed(F6, false, KeyPressTracker.SentByUs(Native.LLKHF_INJECTED, IntPtr.Zero), 1150)),
                         "and so is its up");
        }

        // Held down, a key sends its down over and over. That is one press.
        public static void TestAHeldKeyIsOnePress()
        {
            KeyPressTracker t = Tracking(F6);
            Assert.Equal("down " + F6, Said(t.Feed(F6, true, false, 1000)), "pressed");
            int heard = 0;
            for (uint ms = 1500; ms < 4000; ms += 33)
                if (t.Feed(F6, true, false, ms) != null) heard++;
            Assert.Equal(0, heard, "two and a half seconds of repeats say nothing");
            Assert.Equal("up " + F6, Said(t.Feed(F6, false, false, 4000)), "one up");
            Assert.Equal("down " + F6, Said(t.Feed(F6, true, false, 4100)), "pressed again, heard again");
        }

        // An up can be lost - while a window run as administrator is in
        // front, say. The key must not then be thought held for ever.
        public static void TestAKeyWhoseUpWasLostIsHeardAgain()
        {
            KeyPressTracker t = Tracking(F6);
            t.Feed(F6, true, false, 1000);
            Assert.Equal("nothing", Said(t.Feed(F6, true, false, 1000 + KeyPressTracker.REPEAT_GAP_MS - 1)), "a repeat in time is a repeat");
            Assert.Equal("down " + F6, Said(t.Feed(F6, true, false, 1000 + KeyPressTracker.REPEAT_GAP_MS * 3)), "a down long after is a new press");
        }

        public static void TestTheClockWrapping()
        {
            KeyPressTracker t = Tracking(F6);
            t.Feed(F6, true, false, uint.MaxValue - 10);
            Assert.Equal("nothing", Said(t.Feed(F6, true, false, 20)), "a repeat across the wrap of the clock");
        }

        // Ctrl+F6 is not F6, and F6 is not Ctrl+F6.
        public static void TestCtrlAltAndShiftMatchExactly()
        {
            int ctrlF6 = RuleHotkeys.Key(F6, true, false, false);
            KeyPressTracker plain = Tracking(F6);
            plain.Feed(LCTRL, true, false, 1000);
            Assert.Equal("nothing", Said(plain.Feed(F6, true, false, 1100)), "Ctrl+F6 is not plain F6");
            plain.Feed(F6, false, false, 1150);
            plain.Feed(LCTRL, false, false, 1200);
            Assert.Equal("down " + F6, Said(plain.Feed(F6, true, false, 1300)), "plain F6 once Ctrl is let go");

            KeyPressTracker withCtrl = Tracking(ctrlF6);
            Assert.Equal("nothing", Said(withCtrl.Feed(F6, true, false, 1000)), "plain F6 is not Ctrl+F6");
            withCtrl.Feed(F6, false, false, 1050);
            withCtrl.Feed(RCTRL, true, false, 1100);
            Assert.Equal("down " + ctrlF6, Said(withCtrl.Feed(F6, true, false, 1200)), "the right Ctrl is Ctrl too");
            withCtrl.Feed(LSHIFT, true, false, 1250);
            withCtrl.Feed(F6, false, false, 1260);
            Assert.Equal("nothing", Said(withCtrl.Feed(F6, true, false, 1300)), "Ctrl+Shift+F6 is neither");

            KeyPressTracker withAlt = Tracking(RuleHotkeys.Key(F7, false, true, false));
            withAlt.Feed(LALT, true, false, 1000);
            Assert.Equal("down " + RuleHotkeys.Key(F7, false, true, false), Said(withAlt.Feed(F7, true, false, 1100)), "Alt+F7");

            // The generic Ctrl, as some keyboards send it.
            KeyPressTracker generic = Tracking(ctrlF6);
            generic.Feed(0x11, true, false, 1000);
            Assert.Equal("down " + ctrlF6, Said(generic.Feed(F6, true, false, 1100)), "the plain Ctrl key's own code");
        }

        // The up is the same key as the down, however the fingers come off.
        public static void TestTheUpIsTheKeyThatWentDown()
        {
            int ctrlF6 = RuleHotkeys.Key(F6, true, false, false);
            KeyPressTracker t = Tracking(ctrlF6);
            t.Feed(LCTRL, true, false, 1000);
            Assert.Equal("down " + ctrlF6, Said(t.Feed(F6, true, false, 1100)), "Ctrl+F6");
            t.Feed(LCTRL, false, false, 1200);
            Assert.Equal("up " + ctrlF6, Said(t.Feed(F6, false, false, 1300)), "Ctrl let go first; still Ctrl+F6's up");
        }

        // Win+F6 is a shortcut of Windows' own, not this app's.
        public static void TestNothingWithTheWindowsKeyHeld()
        {
            KeyPressTracker t = Tracking(F6);
            t.Feed(LWIN, true, false, 1000);
            Assert.Equal("nothing", Said(t.Feed(F6, true, false, 1100)), "Win+F6");
        }

        // The autoclicker may be switched with Shift, say. Then Shift is the
        // key, not a Shift held while pressing it - and either side counts.
        public static void TestAModifierCanBeTheKeyItself()
        {
            KeyPressTracker t = Tracking(0x10);
            Assert.Equal("down " + 0x10, Said(t.Feed(LSHIFT, true, false, 1000)), "left Shift is the Shift key");
            Assert.Equal("up " + 0x10, Said(t.Feed(LSHIFT, false, false, 1100)), "and its up");
            Assert.Equal("down " + 0x10, Said(t.Feed(0xA1, true, false, 1200)), "so is the right one");
        }

        // A Ctrl whose up was lost would make every plain key unheard. Before
        // a key counts, Windows' own record of Ctrl, Alt and Shift is asked.
        public static void TestALostModifierUpIsCaughtUp()
        {
            KeyPressTracker t = Tracking(F6);
            bool altReallyDown = true;
            t.StillDown = delegate(byte vk) { return vk == LALT && altReallyDown; };
            t.Feed(LALT, true, false, 1000);
            Assert.Equal("nothing", Said(t.Feed(F6, true, false, 1100)), "Alt+F6 while Alt is really down");
            t.Feed(F6, false, false, 1150);
            altReallyDown = false;      // its up went to a window this can't hear
            Assert.Equal("down " + F6, Said(t.Feed(F6, true, false, 9000)), "Alt is up by Windows' record, so plain F6");
        }

        // Choosing a key in the "press a key" box must only choose it.
        public static void TestNothingWhileAKeyIsBeingChosen()
        {
            KeyPressTracker t = Tracking(F7);
            GlobalKeys.CaptureOpened();
            try
            {
                Assert.True(GlobalKeys.Capturing, "the box is open");
                Assert.Equal("nothing", Said(t.Feed(F7, true, false, 1000)), "F7 pressed in the box");
                // The box closes on the down and F6 becomes the key at once,
                // while F6 is still held and repeating.
                t.Feed(F6, true, false, 2000);
            }
            finally { GlobalKeys.CaptureClosed(); }
            Assert.False(GlobalKeys.Capturing, "the box is closed");

            t.Watch(new List<int>(new int[] { F6, F7 }));
            Assert.Equal("nothing", Said(t.Feed(F6, true, false, 2400)), "the repeats of the press that chose it");
            Assert.Equal("nothing", Said(t.Feed(F6, false, false, 2500)), "nor the up of a down nobody heard");
            Assert.Equal("down " + F6, Said(t.Feed(F6, true, false, 2600)), "the next press is heard");
        }

        // A down heard before the box opened still has its up heard, so
        // whoever heard the down is never left thinking the key is held.
        public static void TestAnUpIsHeardEvenWhileAKeyIsBeingChosen()
        {
            KeyPressTracker t = Tracking(F6);
            Assert.Equal("down " + F6, Said(t.Feed(F6, true, false, 1000)), "pressed");
            GlobalKeys.CaptureOpened();
            try { Assert.Equal("up " + F6, Said(t.Feed(F6, false, false, 1100)), "let go with the box open"); }
            finally { GlobalKeys.CaptureClosed(); }
        }

        public static void TestTheKeysAskedForChangeWhole()
        {
            KeyPressTracker t = Tracking(F6);
            Assert.True(t.Watching(F6), "F6");
            t.Watch(new List<int>(new int[] { F7 }));
            Assert.False(t.Watching(F6), "not F6 any more");
            Assert.True(t.Watching(F7), "F7 instead");
            t.Watch(null);
            Assert.False(t.Watching(F7), "nothing");
            Assert.False(t.Watching(-1), "nonsense is not listened for");
            Assert.False(t.Watching(KeyPressTracker.KEYS), "nor past the end");
        }

        // Listening starts with the key just chosen still held down: its
        // repeats are not a press.
        public static void TestAKeyAlreadyDownAsListeningStartsIsNotAPress()
        {
            KeyPressTracker t = Tracking(F6);
            t.AlreadyDown(F6, 1000);
            Assert.Equal("nothing", Said(t.Feed(F6, true, false, 1400)), "its first repeat");
            Assert.Equal("nothing", Said(t.Feed(F6, false, false, 1500)), "its up, whose down nobody heard");
            Assert.Equal("down " + F6, Said(t.Feed(F6, true, false, 1600)), "the next press");
        }

        // A rule's key can't also be the crosshair's: it would do both. The
        // rules' keys are no longer Windows' shortcut keys, so asking Windows
        // can't find them; they are looked up.
        public static void TestARulesKeyIsNotFreeForTheCrosshair()
        {
            ExtrasSettings s = new ExtrasSettings();
            GlobalKeys.Remember(new List<int>(new int[] { 0x71, RuleHotkeys.Key(0x72, true, false, false) }));
            try
            {
                Assert.Contains("rules", Extras.KeyProblemLive(0x71, s, "cross", null, 0), "F2 plays a rule");
                Assert.True(Extras.KeyProblemLive(0x72, s, "cross", null, 0) == null, "F3 does only with Ctrl, so plain F3 is free");
            }
            finally { GlobalKeys.Remember(null); }
            Assert.True(Extras.KeyProblemLive(0x71, s, "cross", null, 0) == null, "once nothing is listened for, F2 is free");
        }

        // The crosshair window asks whether a key is a rule's. With no app
        // running, nothing is listened for.
        public static void TestNothingIsListenedForUntilTheAppAsks()
        {
            Assert.False(GlobalKeys.ListensFor(F6), "F6");
            Assert.False(GlobalKeys.Active, "no hook in tests");
            ExtrasSettings s = new ExtrasSettings();
            Assert.True(Extras.KeyProblemLive(0x87, s, "cross", null, 0) == null, "F24 is free for the crosshair");
        }
    }
}
