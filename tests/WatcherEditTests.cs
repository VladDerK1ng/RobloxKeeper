using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace RobloxKeeper.Tests
{
    // Editing one watcher.
    //
    // The rules - what makes a watcher saveable, how a test result is worded,
    // where pictures are kept, how painting out a background works - are
    // tested on their own. The editor itself is then checked for the two
    // things that would hurt most if wrong: that opening and saving a watcher
    // changes nothing it was not asked to, and that its test button runs the
    // watcher as typed rather than as last saved.
    static class WatcherEditTests
    {
        const int Grey = unchecked((int)0xFF202020);

        class FakeCapture : ICapture
        {
            public Pixels Grab(int pid, out WatchState state)
            {
                state = WatchState.Watchable;
                Pixels p = new Pixels(400, 300);
                p.Fill(Grey);
                return p;
            }
        }

        class FakeReader : IReadText
        {
            readonly string text;
            public FakeReader(string text) { this.text = text; }
            public string Read(Pixels image) { return text; }
        }

        static WatchedClient Client()
        {
            WatchedClient c = new WatchedClient();
            c.Pid = 100;
            c.Label = "Client 1";
            c.AccountName = "VladDerKing";
            c.PlaceId = "142823291";
            c.JobId = "55cf1f30-d19e-4a37-84aa-609ff3c1d3a0";
            return c;
        }

        static WatchKit Kit(string readText)
        {
            WatchKit k = new WatchKit();
            k.Store = new WatchStore(Path.Combine(Path.GetTempPath(), "rk-edit-" + Guid.NewGuid().ToString("N") + ".dat"));
            FakeCapture capture = new FakeCapture();
            FakeReader reader = new FakeReader(readText);
            k.Capture = capture;
            k.Reader = reader;
            k.Engine = new WatchEngine(capture, reader);
            k.Clients = delegate { return new WatchedClient[] { Client() }; };
            k.Accounts = delegate { return (IList<string>)new List<string> { "VladDerKing", "alt1" }; };
            k.Log = delegate(string s) { };
            return k;
        }

        static Watcher Full(WatchKind kind)
        {
            Watcher w = Watcher.Default("Secret Egg", kind);
            w.Enabled = false;
            w.RegionName = "banner";
            w.Accounts = new string[] { "VladDerKing" };
            w.Rule.Words = new string[] { "secret", "rainbow" };
            w.Rule.Mode = MatchMode.All;
            w.Rule.IgnoreCase = false;
            w.Rule.WholeWordsOnly = true;
            w.ChatContains = "restock";
            w.TemplateFile = "egg.png";
            w.MaskFile = "egg-mask.png";
            w.Tolerance = 0.91;
            w.ConfirmScans = 3;
            w.CooldownSeconds = 45;
            w.SendDiscord = false;
            w.ShowTray = true;
            w.PlaySound = true;
            w.WriteLog = false;
            w.WebhookUrl = "https://discord.com/api/webhooks/1/abc";
            return w;
        }

        static Watcher Good()
        {
            Watcher w = Watcher.Default("Secret Egg", WatchKind.TextAppears);
            w.Rule.Words = new string[] { "secret" };
            return w;
        }

        // ---------- copying ----------

        // The watch thread reads watchers while the editor is open, so the
        // editor works on a copy and the list swaps it in on save.
        public static void TestEditingACopyLeavesTheOriginalAlone()
        {
            Watcher w = Full(WatchKind.TextAppears);
            Watcher copy = WatcherForm.Copy(w);
            Assert.False(ReferenceEquals(w, copy), "a different object");
            Assert.Equal(WatchStore.SerializeWatcher(w), WatchStore.SerializeWatcher(copy), "with everything the same");

            copy.Name = "changed";
            copy.Rule.Words = new string[] { "other" };
            Assert.Equal("Secret Egg", w.Name, "the original's name is untouched");
            Assert.Equal("secret", w.Rule.Words[0], "and so are its words");
        }

        // ---------- words ----------

        public static void TestWordsAreOnePerLineWithBlanksDropped()
        {
            string[] words = WatcherForm.WordsFrom("secret\r\n\r\n  rainbow egg  \n");
            Assert.Equal(2, words.Length, "two real lines");
            Assert.Equal("secret", words[0], "the first");
            Assert.Equal("rainbow egg", words[1], "the second, trimmed, spaces inside kept");
            Assert.Equal(0, WatcherForm.WordsFrom(null).Length, "nothing typed");
        }

        // ---------- what stops a save ----------

        public static void TestAGoodWatcherHasNoProblem()
        {
            Assert.Equal(null, WatcherForm.Problem(Good()), "ready to save");
        }

        public static void TestAWatcherNeedsAName()
        {
            Watcher w = Good();
            w.Name = "  ";
            Assert.Contains("name", WatcherForm.Problem(w), "says what is missing");
        }

        public static void TestAWordWatcherNeedsAWord()
        {
            Watcher w = Good();
            w.Rule.Words = new string[] { "", "  " };
            Assert.Contains("word", WatcherForm.Problem(w), "blank lines are not words");
        }

        public static void TestAPictureWatcherNeedsAPicture()
        {
            Watcher w = Watcher.Default("Egg icon", WatchKind.ImageFound);
            Assert.Contains("picture", WatcherForm.Problem(w), "nothing to look for yet");
        }

        public static void TestAWatcherMustTellYouSomehow()
        {
            Watcher w = Good();
            w.SendDiscord = false;
            w.ShowTray = false;
            w.PlaySound = false;
            w.WriteLog = false;
            Assert.Contains("Tick", WatcherForm.Problem(w), "a watcher nobody hears from is no use");
        }

        public static void TestAWebhookOverrideMustBeADiscordWebhook()
        {
            Watcher w = Good();
            w.WebhookUrl = "https://example.com/hook";
            Assert.Contains("Discord", WatcherForm.Problem(w), "not a Discord webhook");
            w.WebhookUrl = "https://discord.com/api/webhooks/1/abc";
            Assert.Equal(null, WatcherForm.Problem(w), "a real one is fine");
            w.WebhookUrl = null;
            Assert.Equal(null, WatcherForm.Problem(w), "and blank uses the shared one");
        }

        // ---------- words on screen ----------

        public static void TestKindsAreNamedInPlainWords()
        {
            Assert.Equal("A word appears", WatcherForm.KindName(WatchKind.TextAppears), "text");
            Assert.Equal("A new chat line", WatcherForm.KindName(WatchKind.ChatLine), "chat");
            Assert.Equal("A picture appears", WatcherForm.KindName(WatchKind.ImageFound), "picture");
        }

        // The one setting that quietly slows everything else down has to say so.
        public static void TestTheCostOfTheWholeWindowIsStated()
        {
            Assert.Contains("once a second", WatcherForm.CostNote(true), "whole window");
            Assert.Equal("", WatcherForm.CostNote(false), "nothing to say about a box");
        }

        // ---------- the test result ----------

        public static void TestAMatchSaysWhatMatchedAndWhatWasRead()
        {
            WatchCheck k = new WatchCheck();
            k.Seen = true;
            k.Matched = "secret";
            k.Text = "a SECRET egg";
            string said = WatcherForm.CheckSummary(Good(), k);
            Assert.Contains("Matched \"secret\"", said, "what hit");
            Assert.Contains("a SECRET egg", said, "and what was read");
        }

        public static void TestANoMatchStillSaysWhatWasRead()
        {
            WatchCheck k = new WatchCheck();
            k.Text = "a common egg";
            string said = WatcherForm.CheckSummary(Good(), k);
            Assert.Contains("No match", said, "no");
            Assert.Contains("a common egg", said, "and why: this is what it saw");
        }

        // The score against the tolerance is what lets tolerance be tuned
        // against reality rather than guessed.
        public static void TestAPictureCheckGivesTheScoreAgainstWhatItNeeds()
        {
            Watcher w = Watcher.Default("Egg icon", WatchKind.ImageFound);
            w.Tolerance = 0.82;
            WatchCheck k = new WatchCheck();
            k.Score = 0.61;
            string said = WatcherForm.CheckSummary(w, k);
            Assert.Contains("Not found", said, "no");
            Assert.Contains("61%", said, "how close it got");
            Assert.Contains("82%", said, "and how close it has to be");
        }

        public static void TestAChatCheckSaysHowManyLinesPassTheFilter()
        {
            Watcher w = Watcher.Default("Restock", WatchKind.ChatLine);
            w.ChatContains = "restock";
            WatchCheck k = new WatchCheck();
            k.Text = "bob: hi there\n[SERVER] Restock: Rainbow Egg x3";
            string said = WatcherForm.CheckSummary(w, k);
            Assert.Contains("Rainbow Egg", said, "the lines it read");
            Assert.Contains("1 of them", said, "and how many would count");
        }

        public static void TestACheckThatCouldNotLookSaysWhy()
        {
            WatchCheck k = new WatchCheck();
            k.Problem = "No box called \"banner\" has been drawn for this game yet.";
            Assert.Equal(k.Problem, WatcherForm.CheckSummary(Good(), k), "the reason, as it is");
        }

        // ---------- pictures and boxes on disk ----------

        public static void TestPictureFilesGetSafeUniqueNames()
        {
            DateTime at = new DateTime(2026, 9, 21, 23, 15, 2);
            Assert.Equal("Secret-Egg-20260921-231502.png", WatcherForm.TemplateName("Secret Egg!", at), "readable and safe");
            Assert.Equal("picture-20260921-231502.png", WatcherForm.TemplateName("***", at), "something even with no letters");
            Assert.Equal("Secret-Egg-20260921-231502-mask-20260921-232000.png",
                WatcherForm.MaskNameFor("Secret-Egg-20260921-231502.png", new DateTime(2026, 9, 21, 23, 20, 0)),
                "a new painted-out copy never overwrites one a saved watcher uses");
        }

        public static void TestAPictureIsSavedWhereTheWatcherWillLookForIt()
        {
            string path = Path.Combine(Path.GetTempPath(), "rk-pic-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                Pixels p = new Pixels(7, 5);
                p.Fill(Grey);
                p.Set(3, 2, unchecked((int)0xFFFF0000));
                WatcherForm.SavePicture(p, path);

                Pixels back = WatchEngine.LoadPicture(path);
                Assert.True(back != null, "the engine can open it");
                Assert.Equal(7, back.Width, "same size");
                Assert.Equal(unchecked((int)0xFFFF0000), back.At(3, 2), "same pixels");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        // Drawing a box again with the same name for the same game is how a
        // box that needs re-checking gets fixed.
        public static void TestARedrawnBoxReplacesTheOldOneForThatGameOnly()
        {
            WatchStore s = new WatchStore(Path.Combine(Path.GetTempPath(), "unused.dat"));
            s.Regions.Add(Region("banner", "111", 100));
            s.Regions.Add(Region("banner", "222", 100));

            WatcherForm.SaveRegion(s, Region("BANNER", "111", 300));
            Assert.Equal(2, s.Regions.Count, "replaced, not added");
            Assert.Equal(300, s.FindRegion("111", "banner").Project(1000, 600).Width, "the new box for that game");
            Assert.Equal(100, s.FindRegion("222", "banner").Project(1000, 600).Width, "the other game's is untouched");

            WatcherForm.SaveRegion(s, Region("chat", "111", 50));
            Assert.Equal(3, s.Regions.Count, "a new name is added");
        }

        static WatchRegion Region(string name, string placeId, int width)
        {
            WatchRegion r = WatchRegion.FromBox(new Rectangle(0, 0, width, 50), 1000, 600,
                RegionAnchor.TopLeft, RegionScaling.Stretch);
            r.Name = name;
            r.PlaceId = placeId;
            return r;
        }

        // ---------- painting out ----------

        public static void TestPaintingOutRemovesPixelsFromTheComparison()
        {
            Pixels mask = WatcherForm.FullMask(10, 10);
            Assert.True(WatchEngine.MaskFrom(mask)[55], "everything is compared to start with");

            WatcherForm.Paint(mask, 5, 5, 2, true);
            bool[] keep = WatchEngine.MaskFrom(mask);
            Assert.False(keep[5 * 10 + 5], "painted out");
            Assert.True(keep[0], "the corner, outside the brush, is still compared");

            WatcherForm.Paint(mask, 5, 5, 2, false);
            Assert.True(WatchEngine.MaskFrom(mask)[5 * 10 + 5], "and painting it back restores it");
        }

        public static void TestTheBrushStopsAtTheEdges()
        {
            Pixels mask = WatcherForm.FullMask(4, 4);
            WatcherForm.Paint(mask, 0, 0, 3, true);
            Assert.False(WatchEngine.MaskFrom(mask)[0], "the corner painted, nothing thrown");
        }

        public static void TestThePainterStartsWithEverythingKeptAndPaints()
        {
            Pixels template = new Pixels(10, 10);
            template.Fill(Grey);
            using (MaskPainterForm f = new MaskPainterForm(template, null))
            {
                Assert.True(WatchEngine.MaskFrom(f.Mask)[0], "nothing painted out yet");
                f.PaintAt(new Point(5, 5), true);
                Assert.False(WatchEngine.MaskFrom(f.Mask)[55], "the middle is painted out");
                Assert.Equal(10, f.Mask.Width, "the mask is the picture's size");
            }
        }

        // ---------- the editor ----------

        // Opening a watcher and saving it without touching anything must give
        // back exactly the watcher that went in - every field, for every kind.
        public static void TestEveryFieldSurvivesTheEditorUnchanged()
        {
            foreach (WatchKind kind in (WatchKind[])Enum.GetValues(typeof(WatchKind)))
            {
                Watcher w = Full(kind);
                using (WatcherEditDialog d = new WatcherEditDialog(w, Kit("")))
                    Assert.Equal(WatchStore.SerializeWatcher(w), WatchStore.SerializeWatcher(d.Collect()),
                        "unchanged through the editor: " + kind);
            }
        }

        public static void TestTheEditorWorksOnACopy()
        {
            Watcher w = Full(WatchKind.TextAppears);
            using (WatcherEditDialog d = new WatcherEditDialog(w, Kit("")))
                Assert.False(ReferenceEquals(w, d.Collect()), "never the watcher the watch thread is reading");
        }

        // Then: the macros to choose from, and the one chosen comes back out.
        public static void TestTheEditorOffersTheMacrosToPlayNext()
        {
            WatchKit k = Kit("");
            Macro buy = new Macro();
            buy.Name = "Buy egg";
            buy.Steps.Add(MacroStep.Key(0x45, 50));
            k.Store.Macros.Add(buy);

            Watcher w = Good();
            using (WatcherEditDialog d = new WatcherEditDialog(w, k))
            {
                Assert.Equal(2, d.ThenChoices, "nothing, or the one macro");
                Assert.Equal(null, d.Collect().ThenMacro, "nothing chosen yet");
                d.ChooseThen("Buy egg");
                Assert.Equal("Buy egg", d.Collect().ThenMacro, "chosen");
            }

            // A macro that has gone is kept rather than silently dropped by
            // opening and saving the watcher.
            w.ThenMacro = "Gone macro";
            using (WatcherEditDialog d = new WatcherEditDialog(w, k))
                Assert.Equal("Gone macro", d.Collect().ThenMacro, "kept as it was");
        }

        // The test button runs what is typed, not what was last saved.
        public static void TestTheTestButtonRunsTheWatcherAsTyped()
        {
            Watcher w = Good();
            using (WatcherEditDialog d = new WatcherEditDialog(w, Kit("a SECRET egg")))
            {
                string said = d.RunTest(Client());
                Assert.Contains("Matched", said, "it found the word");
                Assert.Contains("a SECRET egg", said, "and says what it read");
            }
        }

        // ---------- a long read ----------

        // What a whole-window chat test really reads: the leaderboard and the
        // buttons, then the chat. The owner's report was a result cut off
        // before the egg lines it was run to show.
        const string WholeWindowRead =
            "Roblox\nHere\nGlobal\nFriends\nVladDerKing\n152B\n53.9B\nFly\n86.5B\n308B\nx64 Speed\nONLY 155\n"
            + "Say hi to everyone playing now!\n"
            + "A Secret Pure Jellyfish Egg spawned in Angels!\n"
            + "A Eternal Mosasaurus Egg spawned in Prehistoric!";

        static Watcher SpawnChat()
        {
            Watcher w = Watcher.Default("Spawns", WatchKind.ChatLine);
            w.ChatContains = "spawned";
            return w;
        }

        public static void TestALongChatReadIsReportedInFull()
        {
            WatchCheck k = new WatchCheck();
            k.Text = WholeWindowRead;
            string said = WatcherForm.CheckSummary(SpawnChat(), k);
            Assert.Contains("A Eternal Mosasaurus Egg spawned in Prehistoric!", said, "the last line, not cut off");
            Assert.Contains("2 of them", said, "and how many would count");
        }

        // The lines the filter picks out come first: in a whole-window read
        // they are the last few of many, and the box shows six at a time.
        public static void TestTheLinesThatCountComeFirst()
        {
            WatchCheck k = new WatchCheck();
            k.Text = WholeWindowRead;
            string said = WatcherForm.CheckSummary(SpawnChat(), k);
            int egg = said.IndexOf("A Eternal Mosasaurus Egg spawned in Prehistoric!", StringComparison.Ordinal);
            int button = said.IndexOf("x64 Speed", StringComparison.Ordinal);
            Assert.True(egg >= 0 && button >= 0, "both are in there");
            Assert.True(egg < button, "the matching line before the rest of the screen");
        }

        // All of it has to be reachable on screen too: a box that scrolls, with
        // each line on its own line, not a label that stops at its bottom edge.
        public static void TestTheTestResultScrollsRatherThanCutsOff()
        {
            using (WatcherEditDialog d = new WatcherEditDialog(SpawnChat(), Kit(WholeWindowRead)))
            {
                d.RunTest(Client());
                Assert.True(d.ResultScrolls, "the result can be scrolled");
                Assert.Contains("\r\nA Eternal Mosasaurus Egg spawned in Prehistoric!", d.ResultShown,
                    "and the last line is in it, on a line of its own");
            }
        }
    }
}
