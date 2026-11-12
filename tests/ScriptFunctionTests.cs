using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace RobloxKeeper.Tests
{
    // Each of a script's functions against a pretend PC: what it sends, where
    // places in % land, what it gives back - and mistakes as "Line N: ...".
    // Nothing here touches the real keyboard, mouse or screen.
    static class ScriptFunctionTests
    {
        static FakeDesk desk;
        static FakeOutlet outlet;

        static ScriptResult Run(string code)
        {
            desk = new FakeDesk();
            outlet = new FakeOutlet();
            return ScriptRig.Run(code, desk, outlet);
        }

        static void Ok(ScriptResult r)
        {
            Assert.Equal(null, r.Problem, "it ran to the end");
        }

        // ---------- keys ----------

        public static void TestPressPressesAndLetsGoOfAKey()
        {
            Ok(Run("press(\"e\")"));
            Assert.Equal("down E | up E", desk.SaidText(), "E down and up");
        }

        public static void TestPressForSecondsHoldsItThatLong()
        {
            Ok(Run("press(\"w\", 2)"));
            Assert.Equal("down W | up W", desk.SaidText(), "W down and up");
            Assert.True(desk.Now >= 2000, "held two seconds");
        }

        public static void TestKeyNamesAreAsPrinted()
        {
            Ok(Run("press(\"space\") press(\"Shift\") press(\"f1\") press(\"left\") press(\"5\") press(\"key 160\")"));
            Assert.Equal("down Space | up Space | down Shift | up Shift | down F1 | up F1 | down Left arrow | up Left arrow | down 5 | up 5 | down Shift | up Shift",
                         desk.SaidText(), "each key by its printed name");
            Assert.Equal((byte)0xA0, desk.Sent[10].Value.Vk, "key 160 is the left Shift itself");
        }

        public static void TestAKeyNobodyHasHeardOfIsAMistakeOnItsLine()
        {
            ScriptResult r = Run("press(\"e\")\npress(\"banana\")");
            Assert.Contains("Line 2:", r.Problem, "said on its line");
            Assert.Contains("no key called", r.Problem, "in plain words");
        }

        public static void TestKeyDownHoldsUntilKeyUp()
        {
            Ok(Run("keydown(\"w\") press(\"space\") keyup(\"w\")"));
            Assert.Equal("down W | down Space | up Space | up W", desk.SaidText(), "jumping while walking");
        }

        public static void TestAKeyHeldAtTheEndIsLetGo()
        {
            Ok(Run("keydown(\"w\")"));
            Assert.Equal("down W | up W", desk.SaidText(), "never left walking forward");
            Assert.False(desk.Keyboard, "and the keyboard is given back");
        }

        public static void TestTypeTypesAndChatSaysItInChat()
        {
            Ok(Run("type(\"hi\")"));
            Assert.Equal("char h | char i", desk.SaidText(), "typed");
            Ok(Run("chat(\"gg\")"));
            Assert.Equal("down / | up / | char g | char g | down Enter | up Enter", desk.SaidText(), "opened, typed, sent");
        }

        // ---------- the mouse, and places in % ----------

        public static void TestClickGoesToItsPlaceInPercent()
        {
            Ok(Run("click(50, 80)"));
            // The window's area is 801 x 601 from 100, 50.
            Assert.Equal("move 500,530 | press left | release left", desk.SaidText(), "the middle, near the bottom");
        }

        public static void TestTheCornersAre0And100()
        {
            Ok(Run("click(0, 0) click(100, 100)"));
            Assert.Equal("move 100,50 | press left | release left | move 900,650 | press left | release left", desk.SaidText(), "top left, bottom right");
        }

        public static void TestAPlaceOffTheWindowIsAMistake()
        {
            ScriptResult r = Run("click(101, 5)");
            Assert.Contains("Line 1:", r.Problem, "on its line");
            Assert.Contains("from 0 to 100", r.Problem, "says why");
            Assert.Equal("", desk.SaidText(), "nothing clicked");
        }

        public static void TestClickWithNoPlaceIsWhereThePointerIs()
        {
            Ok(Run("click() click(\"right\") click(10, 20, \"right\")"));
            Assert.Equal("press left | release left | press right | release right | move 180,170 | press right | release right",
                         desk.SaidText(), "no move for the pointer's own place");
        }

        public static void TestDoubleClickIsTwoClicksInOnePlace()
        {
            Ok(Run("doubleclick(50, 50)"));
            Assert.Equal("move 500,350 | press left | release left | press left | release left", desk.SaidText(), "one move, two clicks");
        }

        public static void TestMoveAndDrag()
        {
            Ok(Run("move(25, 75)"));
            Assert.Equal("move 300,500", desk.SaidText(), "moved");
            Ok(Run("drag(0, 0, 100, 100, 0.2, \"right\")"));
            List<string> s = desk.Said();
            Assert.Equal("move 100,50", s[0], "starts where it starts");
            Assert.Equal("press right", s[1], "with the right button");
            Assert.Equal("drag 900,650", s[s.Count - 2], "ends where it ends");
            Assert.Equal("release right", s[s.Count - 1], "and lets go");
        }

        public static void TestScrollUpAtThePointerAndDownAtAPlace()
        {
            Ok(Run("scroll(2) scroll(-1, 50, 50)"));
            Assert.Equal("wheel 120 | wheel 120 | move 500,350 | wheel -120", desk.SaidText(), "two up here, one down in the middle");
        }

        public static void TestUseScreenPlaysOnTheWholeScreen()
        {
            Ok(Run("use(\"screen\") click(50, 50)"));
            Assert.Equal("screen", desk.Sent[0].Key, "on the screen");
            // The screen is 3840 x 1080 from -1920, 0.
            Assert.Equal("move 0,540", desk.Said()[0], "the middle of every monitor together");
            Assert.False(desk.Log.Contains("front 0"), "nothing is brought to the front for the screen");
        }

        // ---------- time ----------

        public static void TestWaitAndElapsed()
        {
            Ok(Run("wait(1.5) print(elapsed())"));
            Assert.True(desk.Now >= 1500, "waited");
            Assert.Equal("1.5", outlet.All, "and knows how long it has run");
        }

        public static void TestANegativeWaitIsAMistake()
        {
            ScriptResult r = Run("wait(-1)");
            Assert.Contains("less than 0", r.Problem, "says why");
        }

        // ---------- the screen ----------

        public static void TestFindGivesThePlaceInPercentOrNil()
        {
            desk = new FakeDesk();
            outlet = new FakeOutlet();
            desk.Pictures["egg"] = "egg-20261112-120000.png";
            desk.Seen = delegate { return true; };
            desk.SeenAt = new Point(500, 350);
            Ok(ScriptRig.Run("print(find(\"egg\"))", desk, outlet));
            Assert.Equal("50\t50", outlet.All, "the middle");

            desk.Seen = delegate { return false; };
            outlet = new FakeOutlet();
            Ok(ScriptRig.Run("print(find(\"egg\"))", desk, outlet));
            Assert.Equal("nil", outlet.All, "not there");
        }

        public static void TestAMissingPictureIsAMistake()
        {
            ScriptResult r = Run("wait(0)\nfind(\"nothing\")");
            Assert.Contains("Line 2:", r.Problem, "on its line");
            Assert.Contains("no picture called \"nothing\"", r.Problem, "by name");
        }

        public static void TestWaitForWaitsUntilItShowsOrTheTimeIsUp()
        {
            desk = new FakeDesk();
            outlet = new FakeOutlet();
            desk.Pictures["egg"] = "egg.png";
            desk.Seen = delegate(string f, int looks) { return looks >= 3; };
            Ok(ScriptRig.Run("print(waitfor(\"egg\", 5) ~= nil)", desk, outlet));
            Assert.Equal("true", outlet.All, "found on the fourth look");
            Assert.True(desk.Now >= 3 * MacroStep.LOOK_EVERY_MS && desk.Now < 5000, "and stopped looking then");

            desk.Seen = delegate { return false; };
            desk.Now = 0;
            outlet = new FakeOutlet();
            Ok(ScriptRig.Run("print(waitfor(\"egg\", 2))", desk, outlet));
            Assert.Equal("nil", outlet.All, "never showed");
            Assert.True(desk.Now >= 2000, "looked for the whole time");
        }

        public static void TestWaitGoneSaysWhetherItWent()
        {
            desk = new FakeDesk();
            outlet = new FakeOutlet();
            desk.Pictures["bag"] = "bag.png";
            desk.Seen = delegate(string f, int looks) { return looks < 2; };
            Ok(ScriptRig.Run("print(waitgone(\"bag\", 5))", desk, outlet));
            Assert.Equal("true", outlet.All, "went");
            desk.Seen = delegate { return true; };
            outlet = new FakeOutlet();
            Ok(ScriptRig.Run("print(waitgone(\"bag\", 1))", desk, outlet));
            Assert.Equal("false", outlet.All, "still there");
        }

        public static void TestClickPictureClicksWhereItIsNow()
        {
            desk = new FakeDesk();
            outlet = new FakeOutlet();
            desk.Pictures["buy"] = "buy.png";
            desk.Seen = delegate { return true; };
            desk.SeenAt = new Point(333, 222);
            Ok(ScriptRig.Run("print(clickpicture(\"buy\"))", desk, outlet));
            Assert.Equal("true", outlet.All, "found it");
            Assert.Equal("move 333,222 | press left | release left", desk.SaidText(), "on the very pixel it was found");

            desk.Seen = delegate { return false; };
            desk.Sent.Clear();
            outlet = new FakeOutlet();
            Ok(ScriptRig.Run("print(clickpicture(\"buy\", 1, \"right\"))", desk, outlet));
            Assert.Equal("false", outlet.All, "not there");
            Assert.Equal("", desk.SaidText(), "nothing clicked");
        }

        public static void TestColorReadAndSee()
        {
            desk = new FakeDesk();
            outlet = new FakeOutlet();
            desk.Text = "Your bag\nis FULL";
            Ok(ScriptRig.Run("print(color(50, 50)) print(read()) print(see(\"bag is full\")) print(see(\"empty\", 1))", desk, outlet));
            Assert.Equal("10\t20\t30\nYour bag\nis FULL\ntrue\nfalse", outlet.All, "colour, words, and seeing them");
        }

        // ---------- windows ----------

        public static void TestWindowsListsThemAndUsePicksOne()
        {
            desk = new FakeDesk();
            outlet = new FakeOutlet();
            Ok(ScriptRig.Run(
                "for i, w in ipairs(windows()) do print(i, w.name, w.account) end\n" +
                "use(2) press(\"e\") use(\"Vlad\") press(\"e\") use(\"client 2\") press(\"e\")", desk, outlet));
            Assert.Equal("1\tClient 1\tVlad\n2\tClient 2\tnil", outlet.All, "both, with the account where known");
            Assert.Equal("22", desk.Sent[0].Key, "use(2) is the second");
            Assert.Equal("11", desk.Sent[2].Key, "by account");
            Assert.Equal("22", desk.Sent[4].Key, "by its name, any case");
            Assert.True(desk.Log.Contains("front 22") && desk.Log.Contains("front 11"), "each brought to the front for its turn");
        }

        public static void TestUseAWindowThatIsNotThereIsAMistake()
        {
            ScriptResult r = Run("use(\"Client 9\")");
            Assert.Contains("no Roblox window called", r.Problem, "says so");
            r = Run("use(3)");
            Assert.Contains("number 3", r.Problem, "by number too");
        }

        // ---------- telling you ----------

        public static void TestPrintSayNotifyDiscordAndStatus()
        {
            Ok(Run("print(\"a\", 1) say(\"hello\") notify(\"pop\") discord(\"sent\") status(\"Selling\") status()"));
            Assert.Equal("a\t1", outlet.All, "printed");
            Assert.Equal("hello", outlet.Said[0], "said");
            Assert.Equal("pop", outlet.Notified[0], "a pop-up");
            Assert.Equal("sent", outlet.Discorded[0], "to Discord");
            Assert.Equal("Selling", outlet.Statuses[0], "the label shows");
            Assert.Equal(null, outlet.Statuses[1], "and status() hides it");
            Assert.Equal(null, outlet.Statuses[outlet.Statuses.Count - 1], "hidden at the end, whatever the script left");
        }

        public static void TestPopUpsAndDiscordAreKeptFromFlooding()
        {
            Ok(Run("for i = 1, 20 do notify(\"n\" .. i) discord(\"d\" .. i) end wait(5) notify(\"later\") discord(\"later\")"));
            Assert.Equal(2, outlet.Notified.Count, "the first, and the one after the pause");
            Assert.Equal("later", outlet.Notified[1], "later ones still get through");
            Assert.Equal(2, outlet.Discorded.Count, "the same for Discord");
            Assert.Contains("left out", outlet.All, "and it says so once");
        }

        public static void TestTheLineItIsOnIsSaidAsFunctionsAreCalled()
        {
            Ok(Run("press(\"e\")\n\nwait(1)\nlocal x = 1\nprint(x)\nwait(1)"));
            Assert.Equal("1,3,6", string.Join(",", outlet.Lines.ConvertAll(delegate(int n) { return n.ToString(); }).ToArray()), "each line a function was called on");
        }

        // ---------- ending ----------

        public static void TestStopEndsItWithNothingToComplainAbout()
        {
            ScriptResult r = Run("press(\"e\") stop() press(\"f\")");
            Assert.Equal(null, r.Problem, "ended itself");
            Assert.Equal("down E | up E", desk.SaidText(), "nothing after stop()");
        }

        public static void TestAnotherWindowInFrontStopsItLikeAMacro()
        {
            desk = new FakeDesk();
            outlet = new FakeOutlet();
            bool front = true;
            desk.Front = delegate { return front; };
            desk.Napped = delegate { if (desk.Now >= 100) front = false; };
            ScriptResult r = ScriptRig.Run("keydown(\"w\") wait(5) press(\"e\")", desk, outlet);
            Assert.Equal(MacroPlayer.STOPPED, r.Problem, "stopped - another window came to the front");
            Assert.True(desk.Now < 1000, "at once, not at the end of the wait");
            Assert.Equal("down W | up W", desk.SaidText(), "and let go of W");
        }

        public static void TestItsWindowClosingStopsIt()
        {
            desk = new FakeDesk();
            outlet = new FakeOutlet();
            desk.Napped = delegate { desk.WindowGone = true; };
            ScriptResult r = ScriptRig.Run("press(\"e\") wait(1) press(\"e\")", desk, outlet);
            Assert.Contains("closed", r.Problem, "says so");
        }

        public static void TestKeptFromTheKeyboardIsAMistake()
        {
            desk = new FakeDesk();
            outlet = new FakeOutlet();
            desk.Refuse = true;
            ScriptResult r = ScriptRig.Run("wait(0)\npress(\"e\")", desk, outlet);
            Assert.Equal("Line 2: something else was using the keyboard for too long", r.Problem, "on its line");
        }
    }
}
