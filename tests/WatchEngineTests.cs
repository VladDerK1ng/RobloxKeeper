using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace RobloxKeeper.Tests
{
    // The pass: capture each client once, run its watchers off that one
    // picture, and raise what turns up.
    //
    // Run against a scripted capture and a scripted reader rather than a game,
    // so the things the design rests on - one picture per client, watchers only
    // where they are assigned, every client counting its own sightings, chat
    // already on screen not being news - are proven rather than hoped for.
    static class WatchEngineTests
    {
        const string Place = "142823291";
        const string Job = "55cf1f30-d19e-4a37-84aa-609ff3c1d3a0";
        const int Grey = unchecked((int)0xFF202020);
        const int White = unchecked((int)0xFFFFFFFF);
        const int Red = unchecked((int)0xFFFF0000);

        class FakeCapture : ICapture
        {
            public readonly Dictionary<int, Pixels> Shots = new Dictionary<int, Pixels>();
            public readonly Dictionary<int, WatchState> States = new Dictionary<int, WatchState>();
            public int Grabs;

            public Pixels Grab(int pid, out WatchState state)
            {
                Grabs++;
                if (!States.TryGetValue(pid, out state)) state = WatchState.Watchable;
                if (state != WatchState.Watchable) return null;
                Pixels p;
                return Shots.TryGetValue(pid, out p) ? p : null;
            }
        }

        class FakeReader : IReadText
        {
            public string Text = "";
            public int Reads;
            public Size LastSize;
            public bool FailNextRead;

            public string Read(Pixels image)
            {
                Reads++;
                LastSize = new Size(image.Width, image.Height);
                if (FailNextRead) { FailNextRead = false; throw new InvalidOperationException("boom"); }
                return Text;
            }
        }

        // One engine wired to fakes, and everything it reports.
        class Rig
        {
            public readonly FakeCapture Capture = new FakeCapture();
            public readonly FakeReader Reader = new FakeReader();
            public readonly List<DetectionEvent> Found = new List<DetectionEvent>();
            public readonly List<string> Problems = new List<string>();
            public readonly List<WatchRegion> Regions = new List<WatchRegion>();
            public readonly WatchEngine Engine;
            public DateTime Now = new DateTime(2026, 9, 21, 12, 0, 0);

            public Rig()
            {
                Engine = new WatchEngine(Capture, Reader);
                Engine.Found = delegate(DetectionEvent d) { Found.Add(d); };
                Engine.Problem = delegate(Watcher w, WatchedClient c, string why) { Problems.Add(why); };
                Capture.Shots[100] = Screen();
            }

            // One pass, then a quarter of a second - the target pace.
            public void Pass(WatchedClient[] clients, params Watcher[] watchers)
            {
                Engine.Pass(clients, watchers, Regions, Now);
                Now = Now.AddMilliseconds(250);
            }

            public void Pass(WatchedClient client, params Watcher[] watchers)
            {
                Pass(new WatchedClient[] { client }, watchers);
            }
        }

        static Pixels Screen()
        {
            Pixels p = new Pixels(400, 300);
            p.Fill(Grey);
            return p;
        }

        static Pixels Square(int size, int colour)
        {
            Pixels p = new Pixels(size, size);
            p.Fill(colour);
            return p;
        }

        static WatchedClient Client(int pid, string account)
        {
            WatchedClient c = new WatchedClient();
            c.Pid = pid;
            c.Label = "Client 1";
            c.AccountName = account;
            c.PlaceId = Place;
            c.JobId = Job;
            return c;
        }

        static Watcher Words(string name, string word)
        {
            Watcher w = Watcher.Default(name, WatchKind.TextAppears);
            w.Rule.Words = new string[] { word };
            w.ConfirmScans = 1;
            return w;
        }

        static Watcher Chat(string name, string contains)
        {
            Watcher w = Watcher.Default(name, WatchKind.ChatLine);
            w.ChatContains = contains;
            return w;
        }

        static Watcher Picture(string name, string file)
        {
            Watcher w = Watcher.Default(name, WatchKind.ImageFound);
            w.TemplateFile = file;
            w.ConfirmScans = 1;
            return w;
        }

        static WatchRegion Box(string name, string placeId, Rectangle box, int winW, int winH)
        {
            WatchRegion r = WatchRegion.FromBox(box, winW, winH, RegionAnchor.TopLeft, RegionScaling.Stretch);
            r.Name = name;
            r.PlaceId = placeId;
            return r;
        }

        // ---------- words ----------

        public static void TestAWordOnScreenIsReported()
        {
            Rig r = new Rig();
            r.Reader.Text = "a SECRET egg appeared";
            r.Pass(Client(100, "VladDerKing"), Words("Secret Egg", "secret"));

            Assert.Equal(1, r.Found.Count, "one detection");
            DetectionEvent d = r.Found[0];
            Assert.Equal("Secret Egg", d.WatcherName, "which watcher");
            Assert.Equal("VladDerKing", d.AccountName, "which account");
            Assert.Equal("Client 1", d.ClientLabel, "which window");
            Assert.Equal(Place, d.PlaceId, "which game");
            Assert.Equal(Job, d.JobId, "which server");
            Assert.Equal("secret", d.Matched, "the word that hit");
            Assert.True(d.Crop != null && !d.Crop.IsEmpty, "and the picture it was seen in");
        }

        public static void TestNothingIsReportedWhenNothingMatches()
        {
            Rig r = new Rig();
            r.Reader.Text = "nothing of interest";
            r.Pass(Client(100, "a"), Words("Secret Egg", "secret"));
            Assert.Equal(0, r.Found.Count, "quiet");
        }

        // Still on screen is not news - FireControl's rule, checked end to end.
        public static void TestSomethingStillOnScreenIsReportedOnce()
        {
            Rig r = new Rig();
            r.Reader.Text = "a SECRET egg";
            WatchedClient c = Client(100, "a");
            Watcher w = Words("Secret Egg", "secret");
            for (int i = 0; i < 6; i++) r.Pass(c, w);
            Assert.Equal(1, r.Found.Count, "reported on arrival, then left alone");
        }

        // Two clients seeing the same thing are two pieces of news, and
        // neither may confirm the other's sighting.
        public static void TestEachClientCountsItsOwnSightings()
        {
            Rig r = new Rig();
            r.Capture.Shots[200] = Screen();
            r.Reader.Text = "a SECRET egg";
            Watcher w = Words("Secret Egg", "secret");
            w.ConfirmScans = 2;
            WatchedClient[] both = new WatchedClient[] { Client(100, "a"), Client(200, "b") };

            r.Pass(both, w);
            Assert.Equal(0, r.Found.Count, "neither has been seen twice yet");
            r.Pass(both, w);
            Assert.Equal(2, r.Found.Count, "now both, each on its own count");
        }

        // ---------- what gets captured ----------

        // The decision the design rests on: three watchers on one client is
        // one picture, not three.
        public static void TestAClientIsCapturedOncePerPassNotOncePerWatcher()
        {
            Rig r = new Rig();
            r.Pass(Client(100, "a"), Words("one", "x"), Words("two", "y"), Words("three", "z"));
            Assert.Equal(1, r.Capture.Grabs, "one picture shared by all three");
        }

        // Watchers on the same box share what was read from it, too.
        public static void TestTwoWatchersOnTheSameBoxReadItOnce()
        {
            Rig r = new Rig();
            r.Pass(Client(100, "a"), Words("one", "x"), Words("two", "y"));
            Assert.Equal(1, r.Reader.Reads, "one read, two answers");
        }

        // A client nothing is watching costs nothing at all.
        public static void TestAClientNothingIsWatchingIsNeverCaptured()
        {
            Rig r = new Rig();
            Watcher someoneElses = Words("Secret Egg", "secret");
            someoneElses.Accounts = new string[] { "different_account" };
            r.Pass(Client(100, "VladDerKing"), someoneElses);
            r.Pass(Client(100, "VladDerKing"));
            Assert.Equal(0, r.Capture.Grabs, "nothing to do, so no picture taken");
        }

        public static void TestADisabledWatcherDoesNothing()
        {
            Rig r = new Rig();
            r.Reader.Text = "a SECRET egg";
            Watcher off = Words("Secret Egg", "secret");
            off.Enabled = false;
            r.Pass(Client(100, "a"), off);
            Assert.Equal(0, r.Found.Count, "switched off means switched off");
            Assert.Equal(0, r.Capture.Grabs, "and not even captured for it");
        }

        // A minimized client cannot be read, and that must not look like
        // nothing having been found.
        public static void TestAMinimizedClientIsNotReadAndSaysSo()
        {
            Rig r = new Rig();
            r.Capture.States[100] = WatchState.Minimized;
            r.Reader.Text = "a SECRET egg";
            r.Pass(Client(100, "a"), Words("Secret Egg", "secret"));

            Assert.Equal(0, r.Found.Count, "nothing claimed");
            Assert.Equal(0, r.Reader.Reads, "nothing read from a window that is not being drawn");
            WatchState s;
            Assert.True(r.Engine.TryStateOf(100, out s), "the state is kept for the list");
            Assert.Equal(WatchState.Minimized, s, "and it says minimized");
        }

        // A client closing between being listed and being captured is an
        // ordinary thing, not a fault.
        public static void TestACaptureThatFailsIsNotADetection()
        {
            Rig r = new Rig();
            r.Capture.Shots.Remove(100);
            r.Reader.Text = "a SECRET egg";
            r.Pass(Client(100, "a"), Words("Secret Egg", "secret"));
            Assert.Equal(0, r.Found.Count, "no picture, no claim");
            Assert.Equal(0, r.Reader.Reads, "and nothing read from nothing");
        }

        // PIDs are reused by Windows, so a client that goes away must not leave
        // its counts behind for whatever gets its number next.
        public static void TestAClientThatGoesAwayStartsOverIfItComesBack()
        {
            Rig r = new Rig();
            r.Reader.Text = "a SECRET egg";
            WatchedClient c = Client(100, "a");
            Watcher w = Words("Secret Egg", "secret");

            r.Pass(c, w);
            Assert.Equal(1, r.Found.Count, "reported once");
            r.Pass(new WatchedClient[0], w);
            r.Pass(c, w);
            Assert.Equal(2, r.Found.Count, "a fresh client starts a fresh count");
        }

        // ---------- chat ----------

        // Chat that was on screen before anyone was looking is not news.
        // Without this, starting the app sends a burst of old messages.
        public static void TestChatAlreadyOnScreenWhenWatchingStartsIsNotReported()
        {
            Rig r = new Rig();
            r.Reader.Text = "someone: hello\n[SERVER] Restock: Rainbow Egg x3";
            r.Pass(Client(100, "a"), Chat("Restock", null));
            Assert.Equal(0, r.Found.Count, "it was said before we looked");
        }

        // The whole line, which is the point of chat being its own kind.
        public static void TestANewChatLineIsReportedWhole()
        {
            Rig r = new Rig();
            WatchedClient c = Client(100, "a");
            Watcher w = Chat("Restock", null);

            r.Reader.Text = "someone: hello";
            r.Pass(c, w);
            r.Reader.Text = "someone: hello\n[SERVER] Restock: Rainbow Egg x3";
            r.Pass(c, w);

            Assert.Equal(1, r.Found.Count, "one new line");
            Assert.Equal("[SERVER] Restock: Rainbow Egg x3", r.Found[0].Line, "the whole message");
        }

        public static void TestAChatLineIsNotReportedTwice()
        {
            Rig r = new Rig();
            WatchedClient c = Client(100, "a");
            Watcher w = Chat("Everything", null);

            r.Pass(c, w);
            r.Reader.Text = "[SERVER] Restock: Rainbow Egg x3";
            for (int i = 0; i < 5; i++) r.Pass(c, w);
            Assert.Equal(1, r.Found.Count, "still on screen is not said again");
        }

        public static void TestEveryNewChatLineIsItsOwnDetection()
        {
            Rig r = new Rig();
            WatchedClient c = Client(100, "a");
            Watcher w = Chat("Everything", null);

            r.Pass(c, w);
            r.Reader.Text = "first new message here\nsecond new message here";
            r.Pass(c, w);
            Assert.Equal(2, r.Found.Count, "two lines, two detections");
        }

        public static void TestAChatFilterPassesOnlyLinesContainingIt()
        {
            Rig r = new Rig();
            WatchedClient c = Client(100, "a");
            Watcher w = Chat("Restock", "restock");

            r.Pass(c, w);
            r.Reader.Text = "bob: hi there everyone\n[SERVER] Restock: Rainbow Egg x3";
            r.Pass(c, w);

            Assert.Equal(1, r.Found.Count, "only the line with the word in it");
            Assert.Contains("Rainbow Egg", r.Found[0].Line, "and it is that line");
            Assert.Equal("restock", r.Found[0].Matched, "reported as what was asked for");
        }

        // A different server is a different chat. What was said in the last
        // one must not make the same words in this one look old.
        public static void TestChatStartsOverWhenTheClientChangesServer()
        {
            Rig r = new Rig();
            WatchedClient c = Client(100, "a");
            Watcher w = Chat("Everything", null);

            r.Reader.Text = "hello there everyone";
            r.Pass(c, w);
            c.JobId = "another-server";
            r.Reader.Text = "";
            r.Pass(c, w);
            r.Reader.Text = "hello there everyone";
            r.Pass(c, w);

            Assert.Equal(1, r.Found.Count, "the old server's history did not carry over");
        }

        // ---------- pictures ----------

        public static void TestAPictureOnScreenIsReportedWithItsScore()
        {
            Rig r = new Rig();
            Pixels screen = Screen();
            for (int y = 40; y < 50; y++)
                for (int x = 50; x < 60; x++) screen.Set(x, y, Red);
            r.Capture.Shots[100] = screen;
            r.Engine.LoadImage = delegate(string file) { return Square(10, Red); };

            r.Pass(Client(100, "a"), Picture("Egg icon", "egg.png"));

            Assert.Equal(1, r.Found.Count, "found it");
            Assert.Equal("egg", r.Found[0].Matched, "named after its picture");
            Assert.True(r.Found[0].Score >= 0.82, "and says how sure it is");
        }

        public static void TestAPictureThatIsNotOnScreenIsNotReported()
        {
            Rig r = new Rig();
            r.Engine.LoadImage = delegate(string file) { return Square(10, Red); };
            r.Pass(Client(100, "a"), Picture("Egg icon", "egg.png"));
            Assert.Equal(0, r.Found.Count, "all grey, no egg");
        }

        // Pictures are our own code, so they keep working on a machine that
        // cannot read text.
        public static void TestPictureWatchersDoNotReadText()
        {
            Rig r = new Rig();
            r.Engine.LoadImage = delegate(string file) { return Square(10, Red); };
            r.Pass(Client(100, "a"), Picture("Egg icon", "egg.png"));
            Assert.Equal(0, r.Reader.Reads, "never asked the text reader");
        }

        // Four scans a second must not mean four reads of the same file.
        public static void TestAPictureIsLoadedOnceNotEveryPass()
        {
            Rig r = new Rig();
            int loads = 0;
            r.Engine.LoadImage = delegate(string file) { loads++; return Square(10, Red); };
            Watcher w = Picture("Egg icon", "egg.png");
            for (int i = 0; i < 3; i++) r.Pass(Client(100, "a"), w);
            Assert.Equal(1, loads, "loaded once");

            r.Engine.ForgetImages();
            r.Pass(Client(100, "a"), w);
            Assert.Equal(2, loads, "and again after the pictures changed");
        }

        // The background behind an icon changes as the character moves. With
        // the background painted out, only the icon is compared.
        public static void TestAMaskedPictureIgnoresTheBackgroundBehindIt()
        {
            Rig r = new Rig();

            // Cropped on a white background: a red 4x4 in the middle of 10x10.
            Pixels template = Square(10, White);
            Pixels mask = new Pixels(10, 10);           // all painted out...
            for (int y = 3; y < 7; y++)
                for (int x = 3; x < 7; x++)
                {
                    template.Set(x, y, Red);
                    mask.Set(x, y, unchecked((int)0xFF000000));   // ...except the icon
                }

            // Now on screen over grey scenery instead of white.
            Pixels screen = Screen();
            for (int y = 43; y < 47; y++)
                for (int x = 53; x < 57; x++) screen.Set(x, y, Red);
            r.Capture.Shots[100] = screen;

            r.Engine.LoadImage = delegate(string file) { return file == "egg-mask.png" ? mask : template; };
            Watcher w = Picture("Egg icon", "egg.png");
            w.MaskFile = "egg-mask.png";
            r.Pass(Client(100, "a"), w);

            Assert.Equal(1, r.Found.Count, "found despite the different background");
        }

        public static void TestAMaskKeepsOnlyThePixelsLeftVisible()
        {
            Pixels m = new Pixels(2, 1);
            m.Set(0, 0, unchecked((int)0xFF000000));
            m.Set(1, 0, 0x00000000);
            bool[] keep = WatchEngine.MaskFrom(m);
            Assert.True(keep[0], "visible pixels are compared");
            Assert.False(keep[1], "painted-out pixels are not");
        }

        public static void TestAPictureWatcherWithNoPictureSaysSo()
        {
            Rig r = new Rig();
            r.Pass(Client(100, "a"), Picture("Egg icon", null));
            Assert.Equal(0, r.Found.Count, "nothing to look for");
            Assert.Equal(1, r.Problems.Count, "and it says so");
        }

        public static void TestAPictureThatCannotBeOpenedSaysSo()
        {
            Rig r = new Rig();
            r.Engine.LoadImage = delegate(string file) { return null; };
            r.Pass(Client(100, "a"), Picture("Egg icon", "gone.png"));
            Assert.Equal(1, r.Problems.Count, "a missing file is said, not swallowed");
        }

        // A box that shrank below the picture's size can never match, and
        // that must not look like the picture being absent.
        public static void TestABoxSmallerThanItsPictureSaysSo()
        {
            Rig r = new Rig();
            r.Engine.LoadImage = delegate(string file) { return Square(10, Red); };
            r.Regions.Add(Box("icon", Place, new Rectangle(0, 0, 4, 4), 400, 300));
            Watcher w = Picture("Egg icon", "egg.png");
            w.RegionName = "icon";
            r.Pass(Client(100, "a"), w);
            Assert.Equal(1, r.Problems.Count, "too small to hold it");
        }

        // ---------- regions ----------

        // Regions are what make four passes a second affordable, so the engine
        // must read the box - grown by a quarter - and not the whole window.
        public static void TestARegionIsCroppedAndWidenedBeforeReading()
        {
            Rig r = new Rig();
            r.Regions.Add(Box("banner", Place, new Rectangle(0, 0, 100, 60), 400, 300));
            Watcher w = Words("Secret Egg", "secret");
            w.RegionName = "banner";
            r.Pass(Client(100, "a"), w);
            Assert.Equal(new Size(125, 75), r.Reader.LastSize, "the box plus a quarter");
        }

        // A watcher naming a box this game has none of must say so rather than
        // quietly reading the whole window instead.
        public static void TestAMissingRegionIsReportedNotSwappedForTheWholeWindow()
        {
            Rig r = new Rig();
            r.Reader.Text = "a SECRET egg";
            Watcher w = Words("Secret Egg", "secret");
            w.RegionName = "banner";
            r.Pass(Client(100, "a"), w);

            Assert.Equal(0, r.Found.Count, "did not read the whole window instead");
            Assert.Equal(0, r.Reader.Reads, "did not read anything");
            Assert.Equal(1, r.Problems.Count, "and said why");
        }

        // Regions belong to a game. The same name drawn for another game is a
        // different box.
        public static void TestAnotherGamesRegionIsNotUsed()
        {
            Rig r = new Rig();
            r.Regions.Add(Box("banner", "999", new Rectangle(0, 0, 100, 60), 400, 300));
            Watcher w = Words("Secret Egg", "secret");
            w.RegionName = "banner";
            r.Pass(Client(100, "a"), w);
            Assert.Equal(1, r.Problems.Count, "not this game's banner");
        }

        public static void TestAProblemIsReportedOnceNotEveryPass()
        {
            Rig r = new Rig();
            Watcher w = Words("Secret Egg", "secret");
            w.RegionName = "banner";
            for (int i = 0; i < 3; i++) r.Pass(Client(100, "a"), w);
            Assert.Equal(1, r.Problems.Count, "one line in the log, not four a second");
        }

        // The list shows a problem next to the watcher, and stops showing it
        // once it is fixed.
        public static void TestAProblemIsKeptForTheListUntilItIsFixed()
        {
            Rig r = new Rig();
            Watcher w = Words("Secret Egg", "secret");
            w.RegionName = "banner";
            r.Pass(Client(100, "a"), w);
            Assert.True(r.Engine.ProblemFor(w) != null, "shown while it lasts");

            r.Regions.Add(Box("banner", Place, new Rectangle(0, 0, 100, 60), 400, 300));
            r.Pass(Client(100, "a"), w);
            Assert.Equal(null, r.Engine.ProblemFor(w), "gone once the box exists");
        }

        // ---------- faults ----------

        public static void TestAFaultInOneWatcherDoesNotStopTheOthers()
        {
            Rig r = new Rig();
            Pixels screen = Screen();
            for (int y = 40; y < 50; y++)
                for (int x = 50; x < 60; x++) screen.Set(x, y, Red);
            r.Capture.Shots[100] = screen;
            r.Engine.LoadImage = delegate(string file) { return Square(10, Red); };
            r.Reader.FailNextRead = true;

            r.Pass(Client(100, "a"), Words("Secret Egg", "secret"), Picture("Egg icon", "egg.png"));

            Assert.Equal(1, r.Found.Count, "the picture watcher still ran");
            Assert.Equal(1, r.Problems.Count, "and the failure was said");
        }

        // ---------- the test button ----------

        public static void TestCheckingAWatcherNowSaysWhatItSawWithoutFiring()
        {
            Rig r = new Rig();
            r.Reader.Text = "a SECRET egg";
            Watcher w = Words("Secret Egg", "secret");

            WatchCheck k = r.Engine.Check(w, Client(100, "a"), r.Regions);
            Assert.True(k.Seen, "it would match");
            Assert.Equal("a SECRET egg", k.Text, "here is what it read");
            Assert.Equal(0, r.Found.Count, "but nothing was sent");

            r.Pass(Client(100, "a"), w);
            Assert.Equal(1, r.Found.Count, "and real watching still fires on its first sighting");
        }

        public static void TestCheckingAMinimizedClientSaysWhy()
        {
            Rig r = new Rig();
            r.Capture.States[100] = WatchState.Minimized;
            WatchCheck k = r.Engine.Check(Words("Secret Egg", "secret"), Client(100, "a"), r.Regions);
            Assert.False(k.Seen, "nothing seen");
            Assert.Equal(WatchState.Minimized, k.State, "because it is minimized");
            Assert.Contains("minimi", k.Problem, "and it says so in words");
        }

        // ---------- pace ----------

        public static void TestThePaceStartsAtFourPassesASecond()
        {
            Assert.Equal(250, new PassPacer().IntervalMs, "a quarter of a second");
        }

        public static void TestCheapPassesKeepTheFastestPace()
        {
            PassPacer p = new PassPacer();
            for (int i = 0; i < 10; i++) p.After(20);
            Assert.Equal(250, p.IntervalMs, "never faster than four a second");
        }

        public static void TestTheWaitIsWhatIsLeftOfTheInterval()
        {
            Assert.Equal(230, new PassPacer().After(20), "250 minus the 20 already spent");
        }

        // A pass may use half its interval. Beyond that the pace slows so
        // passes stop queueing up behind each other.
        public static void TestASlowPassSlowsThePaceDown()
        {
            PassPacer p = new PassPacer();
            p.After(200);
            Assert.True(p.IntervalMs >= 400, "slowed until the pass fits in half");
        }

        public static void TestThePaceSettlesWherePassesFit()
        {
            PassPacer p = new PassPacer();
            for (int i = 0; i < 10; i++) p.After(300);
            Assert.Equal(600, p.IntervalMs, "300ms of work in every 600ms");
        }

        public static void TestThePaceNeverGetsSlowerThanTheSlowest()
        {
            PassPacer p = new PassPacer();
            p.After(10000);
            Assert.Equal(PassPacer.SlowestMs, p.IntervalMs, "capped");
        }

        public static void TestThePaceRecoversWhenPassesGetCheapAgain()
        {
            PassPacer p = new PassPacer();
            p.After(10000);
            for (int i = 0; i < 50; i++) p.After(10);
            Assert.Equal(250, p.IntervalMs, "back to four a second");
        }

        public static void TestThePaceIsDescribedInPlainWords()
        {
            Assert.Equal("4 scans a second", PassPacer.Describe(250), "the target");
            Assert.Equal("1 scan a second", PassPacer.Describe(1000), "singular");
            Assert.Equal("1 scan every 2 seconds", PassPacer.Describe(2000), "slower than one a second");
        }

        // ---------- running ----------

        public static void TestTheEngineRunsOnItsOwnThreadAndStops()
        {
            WatchEngine e = new WatchEngine(new FakeCapture(), new FakeReader());
            int asked = 0;
            e.Start(delegate { Interlocked.Increment(ref asked); return new WatchWork(); });
            for (int i = 0; i < 200 && Thread.VolatileRead(ref asked) == 0; i++) Thread.Sleep(10);
            e.Stop();

            Assert.True(asked > 0, "it ran a pass");
            Assert.False(e.Running, "and it stopped when asked");
        }
    }
}
