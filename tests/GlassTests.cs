using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace RobloxKeeper.Tests
{
    // The other extras: where panels go, what the keys do, what the
    // read-out says.
    static class GlassTests
    {
        static readonly Rectangle Game = new Rectangle(100, 50, 1000, 800);

        public static void TestPanelsGoInsideTheCornerAskedFor()
        {
            Size p = new Size(120, 160);
            Assert.Equal(new Point(100 + 16, 50 + 64), GlassMath.Corner(Game, p, 0), "top left, clear of Roblox's own buttons");
            Assert.Equal(new Point(1100 - 16 - 120, 50 + 64), GlassMath.Corner(Game, p, 1), "top right");
            Assert.Equal(new Point(100 + 16, 850 - 16 - 160), GlassMath.Corner(Game, p, 2), "bottom left");
            Assert.Equal(new Point(1100 - 16 - 120, 850 - 16 - 160), GlassMath.Corner(Game, p, 3), "bottom right");
        }

        public static void TestPanelsCanGoInTheMiddleOfTheTopOrBottom()
        {
            Size p = new Size(120, 160);
            Assert.Equal(new Point(100 + (1000 - 120) / 2, 50 + 64), GlassMath.Corner(Game, p, 4), "top middle, clear of Roblox's own buttons");
            Assert.Equal(new Point(100 + (1000 - 120) / 2, 850 - 16 - 160), GlassMath.Corner(Game, p, 5), "bottom middle");
            // A wider game moves the middle with it, and the panel stays inside.
            Rectangle wide = new Rectangle(0, 0, 1920, 1080);
            Point at = GlassMath.Corner(wide, new Size(200, 40), 5);
            Assert.Equal(860, at.X, "centred across the wide game");
            Assert.True(at.Y + 40 <= wide.Bottom, "inside the game");
        }

        public static void TestTheFirstFourPlacesKeepTheirNumbersAndTheMiddleOnesComeAfter()
        {
            Assert.Equal(6, ExtrasSettings.CORNERS, "six places to choose from");
            Assert.Equal(ExtrasSettings.CORNERS, ExtrasSettings.CornerNames.Length, "each has a name");
            Assert.Equal("Top left", ExtrasSettings.CornerNames[0], "0 as before");
            Assert.Equal("Top right", ExtrasSettings.CornerNames[1], "1 as before");
            Assert.Equal("Bottom left", ExtrasSettings.CornerNames[2], "2 as before");
            Assert.Equal("Bottom right", ExtrasSettings.CornerNames[3], "3 as before");
            Assert.Equal("Top middle", ExtrasSettings.CornerNames[4], "4 is new");
            Assert.Equal("Bottom middle", ExtrasSettings.CornerNames[5], "5 is new");
        }

        public static void TestSavedPlacesAreKeptAndOnesPastTheLastAreBroughtBack()
        {
            ExtrasSettings old = ExtrasSettings.Parse(new string[] { "keys.corner=3", "stats.corner=0" });
            Assert.Equal(3, old.KeysCorner, "an old bottom right is still bottom right");
            Assert.Equal(0, old.StatsCorner, "an old top left is still top left");
            ExtrasSettings mid = ExtrasSettings.Parse(new string[] { "keys.corner=5", "stats.corner=4" });
            Assert.Equal(5, mid.KeysCorner, "bottom middle is kept");
            Assert.Equal(4, mid.StatsCorner, "top middle is kept");
            ExtrasSettings far = ExtrasSettings.Parse(new string[] { "keys.corner=9", "stats.corner=-2" });
            Assert.Equal(5, far.KeysCorner, "too high is the last");
            Assert.Equal(0, far.StatsCorner, "too low is the first");
            ExtrasSettings round = ExtrasSettings.Parse(mid.ToLines());
            Assert.True(round.KeysCorner == 5 && round.StatsCorner == 4, "and they survive being saved and read back");
        }

        public static void TestZoomKeepsTheMiddleWhereItIs()
        {
            Point mid = new Point(960, 584);
            foreach (int level in ExtrasSettings.ZoomLevels)
            {
                Point off = GlassMath.ZoomOffset(mid, level);
                // The magnifier shows desktop point p at (p - off) * level.
                Assert.True(Math.Abs((mid.X - off.X) * level - mid.X) < level, level + "x: the middle stays put across");
                Assert.True(Math.Abs((mid.Y - off.Y) * level - mid.Y) < level, level + "x: and down");
            }
            Assert.Equal(Point.Empty, GlassMath.ZoomOffset(mid, 1), "no zoom is no move");
        }

        public static void TestClicksASecondForgetsClicksOlderThanASecond()
        {
            List<long> presses = new List<long> { 100, 400, 900, 1050 };
            Assert.Equal(4, GlassMath.PerSecond(presses, 1090), "all four within the second");
            Assert.Equal(2, GlassMath.PerSecond(presses, 1450), "the two oldest have dropped out");
            Assert.Equal(0, GlassMath.PerSecond(presses, 9000), "none, long after");
            Assert.Equal(0, presses.Count, "and nothing is kept");
        }

        public static void TestLeftAndRightShiftAreTheSameKey()
        {
            Assert.Equal((byte)0x10, GlassMath.Plain(0xA0), "left Shift");
            Assert.Equal((byte)0x10, GlassMath.Plain(0xA1), "right Shift");
            Assert.Equal((byte)0x11, GlassMath.Plain(0xA3), "right Ctrl");
            Assert.Equal((byte)0x12, GlassMath.Plain(0xA4), "left Alt");
            Assert.Equal((byte)0x43, GlassMath.Plain(0x43), "an ordinary key is itself");
            Assert.Equal((byte)0, GlassMath.Plain(4000), "a number that is not a key");
        }

        public static void TestProcessorUseIsAShareOfTheWholePc()
        {
            // One core flat out for a second on an 8-core PC is 12.5%, as Task Manager shows it.
            Assert.Equal(13, GlassMath.CpuPercent(0, 10000000, 0, 10000000, 8), "one core of eight");
            Assert.Equal(100, GlassMath.CpuPercent(0, 90000000, 0, 10000000, 8), "never more than all of it");
            Assert.Equal(0, GlassMath.CpuPercent(0, 5, 100, 100, 8), "no time passed");
            Assert.Equal(0, GlassMath.CpuPercent(50, 10, 0, 100, 8), "a reading that went backwards");
        }

        public static void TestTheServerIsReadFromTheLastGameJoined()
        {
            string[] log =
            {
                "2026-11-11T11:09:18.024Z,21.024019,0b0c,6 [FLog::Output] ! Joining game '9b62358a' place 8075399143 at 3.69.168.126",
                "2026-11-11T11:09:18.316Z,21.316570,82b8,6,Info [DFLog::NetworkClient] Connection accepted from 3.69.168.126|54517",
                "2026-11-11T11:20:00.000Z,700.0,0b0c,6 [FLog::Output] ! Joining game 'aaaa' place 123 at 10.20.30.40",
                "something about Joining game without an address"
            };
            Assert.True(GlassMath.ServerIn(log) == null, "the newest join names only an address inside Roblox's own network");
            log[2] = log[2].Replace("10.20.30.40", "45.20.30.40");
            Assert.Equal("45.20.30.40", GlassMath.ServerIn(log), "the newest join wins");
            Assert.True(GlassMath.ServerIn(new string[] { "nothing here", null }) == null, "no join, no server");
            Assert.True(GlassMath.ServerIn(new string[] { "! Joining game 'x' place 1 at 999.1.1.1" }) == null, "not an address");
        }

        public static void TestARobloxDataCentreServerIsFoundByItsPublicAddress()
        {
            // As a live log has it: the join line holds an address inside Roblox's network, the next line the one a PC talks to.
            string[] log =
            {
                "2026-11-11T16:21:56.463Z,21.463699,1b60,6 [FLog::Output] ! Joining game '87fafee7' place 292439477 at 10.30.2.102",
                "2026-11-11T16:21:56.467Z,21.467703,1b60,7 [FLog::Network] UDMUX Address = 128.116.13.34, Port = 60421 | RCC Server Address = 10.30.2.102, Port = 60421",
                "2026-11-11T16:22:06.963Z,31.963572,1b60,6 [FLog::Output] ! Joining game '9aaa6702' place 292439477 at 10.32.1.16",
                "2026-11-11T16:22:07.803Z,32.803608,1b60,7 [FLog::Network] UDMUX Address = 128.116.54.33, Port = 50844 | RCC Server Address = 10.32.1.16, Port = 50844"
            };
            Assert.Equal("128.116.54.33", GlassMath.ServerIn(log), "the public address of the server joined last");
            Assert.True(GlassMath.ServerIn(new string[] { log[0], log[1], log[2] }) == null, "an older server's address is not used for a newer join");
            Assert.True(GlassMath.IsPrivate("10.32.1.16") && GlassMath.IsPrivate("192.168.1.5") && GlassMath.IsPrivate("172.20.0.1"), "addresses inside a network");
            Assert.False(GlassMath.IsPrivate("128.116.54.33") || GlassMath.IsPrivate("172.40.0.1"), "addresses on the internet");
        }

        public static void TestTheReadOutSaysOnlyWhatWasMeasured()
        {
            Assert.Equal("Ping 92 ms", GlassMath.StatsLine(true, 92, true, false, 0, 0, false), "ping alone");
            Assert.Equal("Ping ...", GlassMath.StatsLine(true, -1, false, false, 0, 0, false), "not measured yet: no number");
            Assert.Equal("Ping: no answer", GlassMath.StatsLine(true, -1, true, false, 0, 0, false), "a server that does not answer");
            Assert.Equal("Ping: this server doesn't answer", GlassMath.StatsLine(true, -1, true, true, false, 0, 0, false), "one that has stopped being asked");
            Assert.Equal("Ping ...", GlassMath.StatsLine(true, -1, false, true, false, 0, 0, false), "given up is only said of a server that was asked");
            string both = GlassMath.StatsLine(false, 0, false, true, 1500L * 1024 * 1024, 12, true);
            Assert.Contains("Memory 1.5 GB", both, "memory");
            Assert.Contains("CPU 12%", both, "processor");
            Assert.False(GlassMath.StatsLine(false, 0, false, true, 300L * 1024 * 1024, 0, false).Contains("CPU"), "no processor figure before two readings");
            Assert.False(both.Contains("FPS"), "frames a second are never claimed");
        }

        public static void TestTheKeysPanelGrowsWithItsSize()
        {
            Size small = KeysPiece.PanelSize(0), big = KeysPiece.PanelSize(2);
            Assert.True(big.Width > small.Width && big.Height > small.Height, "large is larger than small");
            Assert.Equal(KeysPiece.PanelSize(2), KeysPiece.PanelSize(99), "a size past the last is the last");
        }

        public static void TestTheDimIsClearInTheMiddleAndDarkAtTheEdge()
        {
            using (Bitmap b = DimPiece.Draw(new Size(400, 300), 50, 30))
            {
                Assert.Equal(0, (int)b.GetPixel(200, 150).A, "the middle is clear");
                Assert.Equal(127, (int)b.GetPixel(2, 2).A, "a corner is half dark");
                Assert.Equal(127, (int)b.GetPixel(200, 4).A, "above the circle is dark");
                Assert.True(b.GetPixel(200 + 85, 150).A > 0 && b.GetPixel(200 + 85, 150).A < 127, "the circle's rim fades");
            }
        }

        // ---------- what the keys do ----------

        class Heard
        {
            public int Toggles;
            public readonly List<string> Zooms = new List<string>(), Sent = new List<string>();
            public KeyRules Rules = new KeyRules();
            public Heard()
            {
                Rules.ToggleCross = delegate { Toggles++; };
                Rules.Zoom = delegate(bool closer) { Zooms.Add(closer ? "in" : "out"); };
                Rules.Send = delegate(byte vk, bool down) { Sent.Add(vk.ToString("X2") + (down ? " down" : " up")); };
            }
        }

        public static void TestTheCrosshairKeySwitchesOnceHoweverLongItIsHeld()
        {
            Heard h = new Heard();
            h.Rules.CrossKey = 0x78;
            Assert.False(h.Rules.Key(0x78, true), "the game sees it going down too");
            Assert.False(h.Rules.Key(0x78, true), "and repeating");
            Assert.False(h.Rules.Key(0x78, true), "and repeating again");
            Assert.False(h.Rules.Key(0x78, false), "and coming up");
            Assert.Equal(1, h.Toggles, "one press, one switch");
            h.Rules.Key(0x78, true);
            h.Rules.Key(0x78, false);
            Assert.Equal(2, h.Toggles, "a second press switches again");
        }

        public static void TestOtherKeysGoStraightToTheGame()
        {
            Heard h = new Heard();
            h.Rules.CrossKey = 0x78; h.Rules.ZoomOn = true; h.Rules.ZoomKey = 0x43; h.Rules.HoldOn = true; h.Rules.HoldKey = 0x10;
            Assert.False(h.Rules.Key(0x57, true), "W is not ours");
            Assert.False(h.Rules.Key(0x57, false), "going up either");
            Assert.False(h.Rules.Key(0, true), "no key at all");
            Assert.Equal(0, h.Toggles + h.Zooms.Count + h.Sent.Count, "and nothing happened");
        }

        public static void TestZoomIsInWhileHeldAndOutOnLettingGo()
        {
            Heard h = new Heard();
            h.Rules.ZoomOn = true; h.Rules.ZoomKey = 0x43;
            Assert.False(h.Rules.Key(0x43, true), "not kept from the game: C can still be typed");
            h.Rules.Key(0x43, true);       // the key repeating
            Assert.True(h.Rules.Zoomed, "zoomed while held");
            Assert.False(h.Rules.Key(0x43, false), "nor coming up");
            Assert.Equal("in,out", string.Join(",", h.Zooms.ToArray()), "in once, out once");
            Assert.False(h.Rules.Zoomed, "back out");
        }

        public static void TestOnlyTheKeyThatSwitchesTheHeldKeyIsKeptFromTheGame()
        {
            Heard h = new Heard();
            h.Rules.CrossKey = 0x78; h.Rules.ZoomOn = true; h.Rules.ZoomKey = 0x43;
            h.Rules.HoldOn = true; h.Rules.HoldKey = 0x10; h.Rules.HoldWith = 0x14;
            bool cross = h.Rules.Key(0x78, true), zoom = h.Rules.Key(0x43, true), shift = h.Rules.Key(0x10, true), caps = h.Rules.Key(0x14, true);
            Assert.False(cross, "the crosshair's key");
            Assert.False(zoom, "the zoom key");
            Assert.False(shift, "the held key itself, when another key switches it");
            Assert.True(caps, "the switching key");
            Assert.Equal(1, h.Toggles, "the crosshair's key still acted");
            Assert.Equal("in", string.Join(",", h.Zooms.ToArray()), "the zoom key still acted");
            Assert.Equal("10 down", string.Join(",", h.Sent.ToArray()), "and the switching key held Shift");
        }

        // The listener's zoom, with a magnifier that does nothing, so a test never magnifies anyone's screen.
        static KeysListener ZoomListener(List<string> told)
        {
            KeysListener k = new KeysListener();
            k.Magnify = delegate(int level, Point from) { told.Add("screen " + level); return true; };
            k.ZoomChanged = delegate(int level) { told.Add("told " + level); };
            k.Set(0, true, 0x43, 4, false, 0x10, 0);
            return k;
        }

        public static void TestTheCrosshairIsToldTheZoomLevelAndThenOne()
        {
            List<string> told = new List<string>();
            using (KeysListener k = ZoomListener(told))
            {
                k.Rules.Key(0x43, true);
                k.Rules.Key(0x43, true);        // repeating
                k.Rules.Key(0x43, false);
                Assert.Equal("screen 4,told 4,screen 1,told 1", string.Join(",", told.ToArray()), "in at the chosen level, out at one, each once");
            }
        }

        public static void TestLetGoZoomsOutAndTellsTheCrosshair()
        {
            List<string> told = new List<string>();
            using (KeysListener k = ZoomListener(told))
            {
                k.Rules.Key(0x43, true);
                k.Rules.LetGo();
                Assert.Equal("screen 4,told 4,screen 1,told 1", string.Join(",", told.ToArray()), "the game left while zoomed");
                k.Rules.LetGo();
                Assert.Equal(4, told.Count, "nothing more when not zoomed");
            }
        }

        public static void TestTheCrosshairIsNotToldOfAZoomThatDidNotHappen()
        {
            List<string> told = new List<string>();
            using (KeysListener k = new KeysListener())
            {
                k.Magnify = delegate(int level, Point from) { return false; };      // the magnifier is not there
                k.ZoomChanged = delegate(int level) { told.Add("told " + level); };
                k.Set(0, true, 0x43, 3, false, 0x10, 0);
                k.Rules.Key(0x43, true);
                k.Rules.Key(0x43, false);
                Assert.Equal(0, told.Count, "no zoom, no news of one");
            }
        }

        public static void TestZoomSwitchedOffLeavesItsKeyAlone()
        {
            Heard h = new Heard();
            h.Rules.ZoomOn = false; h.Rules.ZoomKey = 0x43;
            Assert.False(h.Rules.Key(0x43, true), "the game gets its key back");
            Assert.False(h.Rules.Any, "and nothing needs listening for");
        }

        public static void TestTappingTheHeldKeyHoldsItUntilTappedAgain()
        {
            Heard h = new Heard();
            h.Rules.HoldOn = true; h.Rules.HoldKey = 0x10;
            Assert.True(h.Rules.Key(0x10, true), "the tap itself is kept from the game");
            Assert.True(h.Rules.Key(0x10, false), "going up too - or the game would see it let go");
            Assert.True(h.Rules.Holding, "held");
            h.Rules.Key(0x10, true);
            h.Rules.Key(0x10, false);
            Assert.False(h.Rules.Holding, "let go");
            Assert.Equal("10 down,10 up", string.Join(",", h.Sent.ToArray()), "pressed for the user, then let go");
        }

        public static void TestAnotherKeyCanSwitchTheHeldKey()
        {
            Heard h = new Heard();
            h.Rules.HoldOn = true; h.Rules.HoldKey = 0x10; h.Rules.HoldWith = 0x14;
            Assert.False(h.Rules.Key(0x10, true), "Shift itself still works as Shift");
            h.Rules.Key(0x10, false);
            Assert.True(h.Rules.Key(0x14, true), "the switching key is ours");
            h.Rules.Key(0x14, false);
            Assert.Equal("10 down", string.Join(",", h.Sent.ToArray()), "Shift is what gets held");
        }

        public static void TestLeavingTheGameLetsEverythingGo()
        {
            Heard h = new Heard();
            h.Rules.ZoomOn = true; h.Rules.ZoomKey = 0x43; h.Rules.HoldOn = true; h.Rules.HoldKey = 0x10;
            h.Rules.Key(0x43, true);
            h.Rules.Key(0x10, true);
            h.Rules.LetGo();
            Assert.False(h.Rules.Zoomed || h.Rules.Holding, "nothing zoomed, nothing held");
            Assert.Equal("in,out", string.Join(",", h.Zooms.ToArray()), "zoomed back out");
            Assert.Equal("10 down,10 up", string.Join(",", h.Sent.ToArray()), "the held key was let go");
            h.Rules.LetGo();
            Assert.Equal(2, h.Sent.Count, "letting go twice sends nothing more");
        }

        public static void TestKeysThatWouldBreakTheGameOrTheAppAreRefused()
        {
            ExtrasSettings s = new ExtrasSettings();
            Assert.Contains("moves you", Extras.KeyProblem(0x57, s, "cross", null, 0), "W can't switch the crosshair");
            Assert.Contains("moves you", Extras.KeyProblem(0x20, s, "zoom", null, 0), "Space can't be the zoom key");
            Assert.Contains("do both", Extras.KeyProblem(0x57, s, "zoom", null, 0), "the game sees the zoom key too, so it would do both things");
            Assert.Contains("stop seeing", Extras.KeyProblem(0x57, s, "holdwith", null, 0), "the key that switches the held key is kept from the game");
            Assert.True(Extras.KeyProblem(0x57, s, "hold", null, 0) == null, "but W can be the key that is held - that is auto-walk");
            Assert.Contains("recording", Extras.KeyProblem(MacroRecording.STOP_KEY, s, "cross", null, 0), "F8 ends a recording");
            Assert.Contains("screenshots", Extras.KeyProblem(0x7A, s, "zoom", null, 0x7A), "the screenshot key");
            Assert.True(Extras.KeyProblem(0x78, s, "cross", null, 0) == null, "F9 is free");
        }

        public static void TestTheKeyThatStopsMacrosIsNeverKeptFromTheApp()
        {
            // F10 was free when it was chosen to switch the held key; the stop key was moved onto it afterwards, in another window.
            Heard h = new Heard();
            h.Rules.HoldOn = true; h.Rules.HoldKey = 0x10; h.Rules.HoldWith = 0x79;
            h.Rules.Spared = delegate(byte vk) { return vk == 0x79; };
            Assert.False(h.Rules.Key(0x79, true), "it goes through to the app");
            Assert.False(h.Rules.Key(0x79, false), "coming up too");
            Assert.Equal(0, h.Sent.Count, "and holds nothing");
            Assert.False(h.Rules.Holding, "so nothing is held");
            h.Rules.Spared = delegate { return false; };
            Assert.True(h.Rules.Key(0x79, true), "once the app no longer uses it, it is ours again");
        }

        public static void TestZoomAndCrosshairKeysAreNeverKeptEvenWhenTheAppUsesThem()
        {
            Heard h = new Heard();
            h.Rules.ZoomOn = true; h.Rules.ZoomKey = 0x79; h.Rules.CrossKey = 0x78;
            h.Rules.Spared = delegate(byte vk) { return vk == 0x79 || vk == 0x78; };
            Assert.False(h.Rules.Key(0x79, true), "the zoom key is let through");
            Assert.False(h.Rules.Key(0x78, true), "so is the crosshair's");
        }

        // ---------- the note that says a key is held ----------

        public static void TestTheHeldKeyNoteNamesTheKeyAndTheKeyThatLetsGo()
        {
            Assert.Equal("Holding Shift - tap Shift to let go", GlassMath.HoldNoteText(0x10, 0), "the held key switches itself");
            Assert.Equal("Holding Shift - tap F9 to let go", GlassMath.HoldNoteText(0x10, 0x78), "another key lets go");
            Assert.Contains(MacroKeys.Name(0x57), GlassMath.HoldNoteText(0x57, 0), "any key has its name in it");
        }

        public static void TestTheHeldKeyNoteSitsInTheMiddleAboveTheBottomAndInsideTheGame()
        {
            Size note = new Size(260, 30);
            Point at = GlassMath.HoldNoteAt(Game, note);
            Assert.Equal(Game.Left + (Game.Width - 260) / 2, at.X, "centred across the game");
            Assert.True(at.Y + note.Height < Game.Bottom, "above the bottom edge");
            Assert.True(at.Y + note.Height > Game.Bottom - 200, "but near it");
            Point small = GlassMath.HoldNoteAt(new Rectangle(0, 0, 400, 80), note);
            Assert.True(small.Y >= 0, "a very short game does not push it above the top");
            Assert.Equal(new Point(0, 0).X, GlassMath.HoldNoteAt(new Rectangle(0, 0, 260, 400), note).X, "as wide as the game: against its left edge");
        }

        public static void TestTheHeldKeyNoteIsShownWhenTheHoldStartsAndHiddenWhenItEnds()
        {
            // What KeysListener does with Send's report, checked on the rules themselves.
            Heard h = new Heard();
            List<string> notes = new List<string>();
            h.Rules.Send = delegate(byte vk, bool down) { notes.Add(down ? "show" : "hide"); };
            h.Rules.HoldOn = true; h.Rules.HoldKey = 0x10;
            h.Rules.Key(0x10, true); h.Rules.Key(0x10, false);
            h.Rules.Key(0x10, true); h.Rules.Key(0x10, false);
            Assert.Equal("show,hide", string.Join(",", notes.ToArray()), "one tap to hold, one to let go");
            h.Rules.Key(0x10, true); h.Rules.Key(0x10, false);
            h.Rules.LetGo();
            Assert.Equal("show,hide,show,hide", string.Join(",", notes.ToArray()), "leaving the game lets go too");
        }

        // ---------- pings that are never answered ----------

        public static void TestAServerThatDoesNotAnswerIsGivenUpOnAfterThreeMisses()
        {
            PingTally t = new PingTally();
            Assert.False(t.GaveUp("128.116.1.1"), "nothing asked yet");
            Assert.False(t.Record("128.116.1.1", false), "one miss");
            Assert.False(t.Record("128.116.1.1", false), "two misses");
            Assert.False(t.GaveUp("128.116.1.1"), "still asking");
            Assert.True(t.Record("128.116.1.1", false), "three in a row: stop");
            Assert.True(t.GaveUp("128.116.1.1"), "and it stays given up");
        }

        public static void TestAnAnswerStartsTheCountOver()
        {
            PingTally t = new PingTally();
            t.Record("45.1.1.1", false); t.Record("45.1.1.1", false);
            t.Record("45.1.1.1", true);
            t.Record("45.1.1.1", false); t.Record("45.1.1.1", false);
            Assert.False(t.GaveUp("45.1.1.1"), "two and two with an answer between is not three in a row");
            t.Record("45.1.1.1", false);
            Assert.True(t.GaveUp("45.1.1.1"), "three in a row now");
        }

        public static void TestADifferentServerIsAskedAgain()
        {
            PingTally t = new PingTally();
            for (int i = 0; i < 3; i++) t.Record("128.116.1.1", false);
            Assert.True(t.GaveUp("128.116.1.1"), "given up on the first");
            Assert.False(t.GaveUp("128.116.2.2"), "the next server is not the one given up on");
            Assert.False(t.Record("128.116.2.2", false), "and its count begins at one");
            Assert.False(t.GaveUp("128.116.1.1"), "the old one is forgotten once a new one is asked");
        }

        public static void TestAGivenUpCountIsClearedForANewClient()
        {
            PingTally t = new PingTally();
            for (int i = 0; i < 3; i++) t.Record("128.116.1.1", false);
            t.Reset();
            Assert.False(t.GaveUp("128.116.1.1"), "a new client asks again");
            Assert.False(t.GaveUp(null), "no server is never given up on");
        }

        public static void TestShiftCtrlAndAltAreNotAskedAboutAsShortcuts()
        {
            Assert.False(Extras.KeyTaken(0), "no key");
            Assert.False(Extras.KeyTaken(0x10), "Shift");
            Assert.False(Extras.KeyTaken(0x11), "Ctrl");
            Assert.False(Extras.KeyTaken(0x12), "Alt");
        }

        public static void TestOnlyTheSwitchingKeyIsAskedAboutShortcuts()
        {
            // Another program (here, this test) owns F24 as a shortcut that works everywhere.
            const byte F24 = 0x87;
            const int OWN = 0x5EF8;
            if (!Xn.RegisterHotKey(IntPtr.Zero, OWN, 0, F24)) return;       // not ours to borrow on this PC: nothing to check
            try
            {
                ExtrasSettings s = new ExtrasSettings();
                s.HoldOn = true; s.HoldKey = 0x10; s.HoldWith = 0;
                Assert.True(Extras.KeyProblemLive(F24, s, "cross", null, 0) == null, "the crosshair's key is let through to everybody, so it may be one");
                Assert.True(Extras.KeyProblemLive(F24, s, "zoom", null, 0) == null, "and so may the zoom key");
                Assert.Contains("already a shortcut", Extras.KeyProblemLive(F24, s, "holdwith", null, 0), "the key that switches the held key is kept, so it may not");
                s.HoldWith = 0x78;
                Assert.True(Extras.KeyProblemLive(F24, s, "hold", null, 0) == null, "the held key is only sent, when another key switches it");
                s.HoldWith = 0;
                Assert.Contains("already a shortcut", Extras.KeyProblemLive(F24, s, "hold", null, 0), "but not when it is its own switch");
            }
            finally { Xn.UnregisterHotKey(IntPtr.Zero, OWN); }
        }

        public static void TestOneKeyCannotDoTwoOfTheExtras()
        {
            ExtrasSettings s = new ExtrasSettings();
            s.CrossKey = 0x78; s.ZoomOn = true; s.ZoomKey = 0x43; s.HoldOn = true; s.HoldKey = 0x10; s.HoldWith = 0x14;
            Assert.Contains("crosshair", Extras.KeyProblem(0x78, s, "zoom", null, 0), "the crosshair's key");
            Assert.Contains("zoom", Extras.KeyProblem(0x43, s, "cross", null, 0), "the zoom key");
            Assert.Contains("holds a key", Extras.KeyProblem(0x14, s, "zoom", null, 0), "the key that switches the held key");
            Assert.True(Extras.KeyProblem(0x78, s, "cross", null, 0) == null, "choosing the same key again for the same job is fine");
        }

        public static void TestTheExtrasWindowAsksForAKeyBeforeSavingZoom()
        {
            ExtrasSettings s = new ExtrasSettings();
            s.ZoomOn = true;
            Assert.Contains("key that zooms", ExtrasDialog.Problem(s, false), "zoom ticked with no key");
            s.ZoomKey = 0x43;
            Assert.True(ExtrasDialog.Problem(s, false) == null, "with a key it saves");
            s.HoldOn = true; s.HoldKey = 0x43;
            Assert.Contains("same key", ExtrasDialog.Problem(s, false), "zoom and hold on one key");
            s.HoldKey = 0x10;
            Assert.Contains("switches the held key", ExtrasDialog.Problem(s, true), "another key asked for and not chosen");
        }

        public static void TestChoicesAppearOnlyUnderATickedExtra()
        {
            using (ExtrasDialog d = new ExtrasDialog(null))
            {
                Assert.False(d.ShowsOptions(ExtrasDialog.ZOOM), "zoom's choices are away while it is unticked");
                d.Tick(ExtrasDialog.ZOOM, true);
                Assert.True(d.ShowsOptions(ExtrasDialog.ZOOM), "ticked, they show");
                d.Tick(ExtrasDialog.PING, true);
                Assert.False(d.ShowsOptions(ExtrasDialog.PING), "ping has nothing to choose");
                ExtrasSettings s = d.Collect();
                Assert.True(s.ZoomOn && s.PingOn && !s.KeysOn, "the ticks are what is collected");
            }
        }
    }
}
