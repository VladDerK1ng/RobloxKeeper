using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace RobloxKeeper.Tests
{
    // What someone can make for themselves: the packs that come with the
    // app, macros written as text, and the guide that explains both - whose
    // every example is run here, so the guide cannot say something that
    // does not work.
    //
    // The Roblox here is a pretend one in the temp folder, never the real one.
    static class ModdingTests
    {
        class Pretend : IDisposable
        {
            public readonly string Root, Versions, Library, Backup, Version;

            public Pretend()
            {
                Root = Path.Combine(Path.GetTempPath(), "rk-modding-" + Guid.NewGuid().ToString("N"));
                Versions = Path.Combine(Root, "Versions");
                Library = Path.Combine(Root, "mods");
                Backup = Path.Combine(Root, "backup");
                Version = Path.Combine(Versions, "version-aaa");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Version, PremadeMods.POINTER)));
                File.WriteAllText(Path.Combine(Version, "RobloxPlayerBeta.exe"), "");
                File.WriteAllText(Path.Combine(Version, PremadeMods.POINTER), "roblox's pointer");
                File.WriteAllText(Path.Combine(Version, PremadeMods.POINTER_FAR), "roblox's far pointer");
                File.WriteAllText(Path.Combine(Version, PremadeMods.POINTER_LOCKED), "roblox's locked pointer");
            }

            public LooksPlaces Places()
            {
                LooksPlaces at = new LooksPlaces();
                at.Versions = Versions; at.Library = Library; at.Backup = Backup;
                return at;
            }

            // Roblox installs a new version: a new folder, its own files in it.
            public string NewVersion(string name)
            {
                string folder = Path.Combine(Versions, name);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, PremadeMods.POINTER)));
                File.WriteAllText(Path.Combine(folder, "RobloxPlayerBeta.exe"), "");
                File.WriteAllText(Path.Combine(folder, PremadeMods.POINTER), "roblox's pointer");
                return folder;
            }

            public void Dispose()
            {
                try { Directory.Delete(Root, true); } catch { }
            }
        }

        // Lets work handed back to the window's thread arrive.
        static void Until(Func<bool> done, string what)
        {
            DateTime give = DateTime.Now.AddSeconds(10);
            while (!done() && DateTime.Now < give)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }
            Assert.True(done(), what);
        }

        // ---------- ready-made packs ----------

        // The page says nothing in Roblox changes until Apply packs is
        // pressed. The once-a-minute check for a new Roblox version used to
        // take a pack that was only ticked - as every pack is when it is
        // added - for one waiting to be put back, and put it in.
        public static void TestAPackOnlyAddedStaysOutOfRobloxUntilApplyIsPressed()
        {
            using (Pretend r = new Pretend())
            {
                LooksPlaces at = r.Places();
                LooksMods mods = new LooksMods(r.Library);
                Assert.Equal(null, PremadeMods.Use(mods, PremadeMods.Find("Dot pointer"), null), "added, and ticked");

                Assert.Equal(null, Looks.Reapply(at, new LooksChoices()), "the minute's check has nothing to do");
                Assert.Equal("roblox's pointer", File.ReadAllText(Path.Combine(r.Version, PremadeMods.POINTER)), "and Roblox has its own pointer still");
                Assert.False(Directory.Exists(r.Backup), "nothing was even saved, because nothing was touched");

                // A new version arriving does not change that.
                string second = r.NewVersion("version-bbb");
                Assert.Equal(null, Looks.Reapply(at, new LooksChoices()), "nor for a new Roblox version");
                Assert.Equal("roblox's pointer", File.ReadAllText(Path.Combine(second, PremadeMods.POINTER)), "which is left alone too");
            }
        }

        // After Apply, a new Roblox version gets what was applied - not what
        // has been ticked since without pressing Apply.
        public static void TestANewRobloxVersionGetsWhatWasAppliedNotWhatIsTicked()
        {
            using (Pretend r = new Pretend())
            {
                LooksPlaces at = r.Places();
                LooksMods mods = new LooksMods(r.Library);
                PremadeMods.Use(mods, PremadeMods.Find("Dot pointer"), null);
                mods.Apply(r.Versions, r.Backup);
                byte[] dot = File.ReadAllBytes(Path.Combine(r.Version, PremadeMods.POINTER));
                Assert.Equal("Dot pointer", string.Join(",", new List<string>(new LooksMods(r.Library).Applied).ToArray()), "what was applied is remembered with the library");

                // Another one picked, Apply not pressed.
                PremadeMods.Use(mods, PremadeMods.Find("Ring pointer"), null);
                string second = r.NewVersion("version-bbb");
                Assert.Contains("mod packs", Looks.Reapply(at, new LooksChoices()), "the new version is given the packs");
                Assert.Equal(Convert.ToBase64String(dot), Convert.ToBase64String(File.ReadAllBytes(Path.Combine(second, PremadeMods.POINTER))),
                             "the one that was applied, not the one only ticked");
                Assert.Equal(null, Looks.Reapply(at, new LooksChoices()), "and once is enough");

                // A pack removed from the library cannot be applied again.
                Assert.Equal(null, mods.Remove("Dot pointer"), "removed");
                r.NewVersion("version-ccc");
                Assert.Equal(null, Looks.Reapply(at, new LooksChoices()), "nothing left that was applied");
            }
        }

        // The whole page, as its button does it: Roblox back, every pack
        // unticked, and nothing left for the minute's check to put in again.
        public static void TestPuttingRobloxBackFromThePageLeavesNothingToComeBack()
        {
            using (Pretend r = new Pretend())
            using (MainForm f = new MainForm(false))
            using (LooksPage page = new LooksPage(f, new System.Drawing.Size(902, 692), r.Places()))
            {
                IntPtr handle = page.Handle;        // work comes back to the page through its window
                LooksMods mods = new LooksMods(r.Library);
                PremadeMods.Use(mods, PremadeMods.Find("Dot pointer"), null);
                mods.Apply(r.Versions, r.Backup);
                Assert.NotEqual("roblox's pointer", File.ReadAllText(Path.Combine(r.Version, PremadeMods.POINTER)), "the pack is in Roblox");

                page.Restore();
                Until(delegate { return page.PacksSays.StartsWith("Roblox is back"); }, "the page says it is back: " + page.PacksSays);

                Assert.Equal("roblox's pointer", File.ReadAllText(Path.Combine(r.Version, PremadeMods.POINTER)), "Roblox's own pointer");
                LooksMods after = new LooksMods(r.Library);
                Assert.Equal(1, after.Packs.Count, "the pack is still in the list");
                Assert.False(after.AnyEnabled, "unticked, so the next Apply does not undo it");
                Assert.False(after.AnyApplied, "and not remembered as applied");
                r.NewVersion("version-bbb");
                Assert.Equal(null, Looks.Reapply(r.Places(), new LooksChoices()), "so a new Roblox version is left as Roblox made it");
            }
        }

        // The tick boxes in the list, not only the Add menu.
        public static void TestTickingAReadyMadePointerInTheListUnticksTheOthers()
        {
            using (Pretend r = new Pretend())
            using (MainForm f = new MainForm(false))
            using (LooksPage page = new LooksPage(f, new System.Drawing.Size(902, 692), r.Places()))
            {
                LooksMods mods = new LooksMods(r.Library);
                PremadeMods.Use(mods, PremadeMods.Find("Dot pointer"), null);
                PremadeMods.Use(mods, PremadeMods.Find("Ring pointer"), null);

                string said = page.Tick("Dot pointer", true);
                Assert.Contains("Ring pointer was turned off", said, "it says which");
                LooksMods now = new LooksMods(r.Library);
                Assert.True(now.Find("Dot pointer").Enabled && !now.Find("Ring pointer").Enabled, "one ticked, saved");
                Assert.Equal("", page.Tick("Dot pointer", false), "unticking one unticks nothing else");
            }
        }

        // Picked again from the menu, it goes to the end of the list - where
        // it wins over any other pack that replaces the pointer.
        public static void TestAPointerPickedAgainGoesLastWhereItWins()
        {
            using (Pretend r = new Pretend())
            {
                LooksMods mods = new LooksMods(r.Library);
                PremadeMods.Use(mods, PremadeMods.Find("Dot pointer"), null);
                string own = Path.Combine(r.Root, "My pointer");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(own, PremadeMods.POINTER)));
                File.WriteAllText(Path.Combine(own, PremadeMods.POINTER), "my own pointer");
                Assert.Equal(null, mods.Import(own), "somebody's own pointer pack, added after");

                List<string> off = new List<string>();
                PremadeMods.Use(mods, PremadeMods.Find("Dot pointer"), off);
                Assert.Equal("Dot pointer", mods.Packs[mods.Packs.Count - 1].Name, "last in the list");
                Assert.True(mods.Find("My pointer").Enabled, "their own pack is not unticked for them");
                mods.Apply(r.Versions, r.Backup);
                Assert.NotEqual("my own pointer", File.ReadAllText(Path.Combine(r.Version, PremadeMods.POINTER)), "and the one picked is the one that shows");
            }
        }

        // What a pack would change, before Apply is pressed.
        public static void TestAPacksFilesCanBeReadBeforeItIsApplied()
        {
            using (Pretend r = new Pretend())
            using (MainForm f = new MainForm(false))
            using (LooksPage page = new LooksPage(f, new System.Drawing.Size(902, 692), r.Places()))
            {
                LooksMods mods = new LooksMods(r.Library);
                PremadeMods.Use(mods, PremadeMods.Find("Dot pointer"), null);
                PremadeMods.Use(mods, PremadeMods.Find("Big bright pointer"), null);

                List<string> ticked = page.FilesOf("Big bright pointer");
                Assert.Equal("Replaces or adds 2 files in Roblox:", ticked[0], "how many");
                Assert.Contains("ArrowCursor.png", string.Join("|", ticked.ToArray()), "and which");

                // Not ticked, and still readable - with what it would lose to.
                List<string> unticked = page.FilesOf("Dot pointer");
                Assert.Equal("Replaces or adds 3 files in Roblox:", unticked[0], "an unticked pack's files too");
                string all = string.Join("|", unticked.ToArray());
                Assert.Contains("ArrowCursor.png   (Big bright pointer is lower in the list, so it wins)", all, "a file another pack would take");
                Assert.Contains("MouseLockedCursor.png|", all + "|", "and one that would be its own");
                Assert.False(mods.Find("Dot pointer").Enabled && new LooksMods(r.Library).Find("Dot pointer").Enabled, "asking does not tick it");
                Assert.Equal(0, page.FilesOf("No such pack").Count, "nothing for a pack that is not there");
            }
        }

        public static void TestEveryReadyMadePackHasItsOwnNameAndSaysWhatItIs()
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PremadePack p in PremadeMods.All)
            {
                Assert.True(names.Add(p.Name), p.Name + " is only there once");
                Assert.True(p.Says.Length > 10, p.Name + " says what it is");
                Assert.True(ReferenceEquals(p, PremadeMods.Find(p.Name.ToUpperInvariant())), p.Name + " is found by name");
            }
            Assert.True(PremadeMods.All.Length >= 5, "a handful to choose from");
        }

        // Roblox clicks with the middle of its pointer picture. A pointer
        // drawn off to one side would click beside where it shows.
        public static void TestAimingPointersAreDrawnAroundTheMiddle()
        {
            foreach (PremadePack p in PremadeMods.All)
            {
                if (p.Name.StartsWith("Big")) continue;        // an arrow: its tip is the middle, checked below
                using (Bitmap b = PremadeMods.Picture(p.Draw, PremadeMods.POINTER_SIZE))
                {
                    double sumX = 0, sumY = 0, weight = 0;
                    for (int y = 0; y < b.Height; y++)
                        for (int x = 0; x < b.Width; x++)
                        {
                            int a = b.GetPixel(x, y).A;
                            sumX += (x + 0.5) * a; sumY += (y + 0.5) * a; weight += a;
                        }
                    Assert.True(weight > 0, p.Name + " draws something");
                    Assert.True(Math.Abs(sumX / weight - 32) < 1 && Math.Abs(sumY / weight - 32) < 1,
                                p.Name + " is centred on the point that clicks: " + sumX / weight + ", " + sumY / weight);
                    Assert.Equal(0, (int)b.GetPixel(0, 0).A, p.Name + " has a see-through background");
                }
            }
        }

        public static void TestTheArrowsTipIsThePointThatClicks()
        {
            PremadePack p = PremadeMods.Find("Big bright pointer");
            using (Bitmap b = PremadeMods.Picture(p.Draw, PremadeMods.POINTER_SIZE))
            {
                Assert.True(b.GetPixel(33, 35).A > 200, "solid just inside the tip");
                for (int y = 0; y < 29; y++)
                    for (int x = 0; x < 64; x++)
                        Assert.Equal(0, (int)b.GetPixel(x, y).A, "nothing above the tip, at " + x + "," + y);
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 29; x++)
                        Assert.Equal(0, (int)b.GetPixel(x, y).A, "nothing left of the tip, at " + x + "," + y);
            }
        }

        public static void TestAPackIsLaidOutAsRobloxIsAtRobloxsSizes()
        {
            using (Pretend r = new Pretend())
            {
                string folder = Path.Combine(r.Root, "built");
                PremadeMods.Build(PremadeMods.Find("Crosshair pointer"), folder);
                using (Bitmap b = new Bitmap(Path.Combine(folder, "content", "textures", "Cursors", "KeyboardMouse", "ArrowCursor.png")))
                    Assert.Equal(new Size(64, 64), b.Size, "the pointer");
                using (Bitmap b = new Bitmap(Path.Combine(folder, "content", "textures", "Cursors", "KeyboardMouse", "ArrowFarCursor.png")))
                    Assert.Equal(new Size(64, 64), b.Size, "the far pointer");
                using (Bitmap b = new Bitmap(Path.Combine(folder, "content", "textures", "MouseLockedCursor.png")))
                    Assert.Equal(new Size(32, 32), b.Size, "the locked pointer");
                foreach (string rel in new string[] { PremadeMods.POINTER, PremadeMods.POINTER_FAR, PremadeMods.POINTER_LOCKED })
                    Assert.Equal(null, LooksMods.WhyNot(rel), rel + " is a file a pack may hold");

                string arrow = Path.Combine(r.Root, "arrow");
                PremadeMods.Build(PremadeMods.Find("Big bright pointer"), arrow);
                Assert.False(File.Exists(Path.Combine(arrow, PremadeMods.POINTER_LOCKED)), "an arrow leaves the locked pointer to Roblox");
            }
        }

        // The whole way: into the library, into Roblox, and out again with
        // Roblox's own files back as they were.
        public static void TestAReadyMadePackGoesIntoRobloxAndComesBackOut()
        {
            using (Pretend r = new Pretend())
            {
                LooksMods mods = new LooksMods(r.Library);
                List<string> off = new List<string>();
                Assert.Equal(null, PremadeMods.Use(mods, PremadeMods.Find("Dot pointer"), off), "added");
                Assert.Equal(0, off.Count, "nothing else was ticked");
                Assert.True(mods.Find("Dot pointer").Enabled, "and ticked");
                Assert.Equal("roblox's pointer", File.ReadAllText(Path.Combine(r.Version, PremadeMods.POINTER)), "Roblox untouched until asked");

                LooksModsResult applied = mods.Apply(r.Versions, r.Backup);
                Assert.Equal(0, applied.Failed.Count, "applied");
                using (Bitmap b = new Bitmap(Path.Combine(r.Version, PremadeMods.POINTER)))
                    Assert.Equal(new Size(64, 64), b.Size, "the pack's picture is in Roblox");

                LooksMods.Restore(r.Versions, r.Backup);
                Assert.Equal("roblox's pointer", File.ReadAllText(Path.Combine(r.Version, PremadeMods.POINTER)), "the pointer is back");
                Assert.Equal("roblox's far pointer", File.ReadAllText(Path.Combine(r.Version, PremadeMods.POINTER_FAR)), "the far one");
                Assert.Equal("roblox's locked pointer", File.ReadAllText(Path.Combine(r.Version, PremadeMods.POINTER_LOCKED)), "and the locked one");
            }
        }

        // They all replace the same pictures. Two ticked would mean the
        // lower one silently wins.
        public static void TestOnlyOneReadyMadePointerIsTickedAtATime()
        {
            using (Pretend r = new Pretend())
            {
                LooksMods mods = new LooksMods(r.Library);
                PremadeMods.Use(mods, PremadeMods.Find("Dot pointer"), null);
                List<string> off = new List<string>();
                Assert.Equal(null, PremadeMods.Use(mods, PremadeMods.Find("Ring pointer"), off), "a second one");
                Assert.Equal("Dot pointer", string.Join(",", off.ToArray()), "the first is named as unticked");
                Assert.False(mods.Find("Dot pointer").Enabled, "and is");
                Assert.True(mods.Find("Ring pointer").Enabled, "the new one is ticked");

                off.Clear();
                Assert.Equal(null, PremadeMods.Use(mods, PremadeMods.Find("Dot pointer"), off), "picked again: not an error");
                Assert.True(mods.Find("Dot pointer").Enabled, "it is the one in use again");
                Assert.False(mods.Find("Ring pointer").Enabled, "and the other is not");
                Assert.Equal(2, mods.Packs.Count, "and it is not in the list twice");

                LooksMods again = new LooksMods(r.Library);
                again.Load();
                Assert.True(again.Find("Dot pointer").Enabled && !again.Find("Ring pointer").Enabled, "and that is what was saved");
            }
        }

        // ---------- the new looks ----------

        public static void TestALookOnlyPaysForWhatItUses()
        {
            ShaderPreset plain = ShaderLooks.Find("Sharper"), glow = ShaderLooks.Find("Soft glow");
            Assert.Equal(5, plain.Taps, "a plain look reads the picture five times");
            Assert.Equal(13, glow.Taps, "a glowing one thirteen");
            Assert.False(ShaderLooks.Source(plain).Contains("blur"), "and a plain look has no glow in it at all");
            Assert.True(ShaderLooks.Source(glow).Contains("blur"), "where a glowing one does");
            foreach (ShaderPreset p in ShaderLooks.Presets)
                Assert.True(p.Taps <= 13, p.Name + " stays cheap");
        }

        public static void TestTheNewLooksScaleWithStrengthToo()
        {
            ShaderPreset comic = ShaderLooks.Find("Comic book");
            Assert.True(ShaderLooks.At(comic, 0).Levels > ShaderLooks.At(comic, 2).Levels, "stronger is fewer steps of colour");
            Assert.True(ShaderLooks.At(comic, 2).Levels >= 3, "but never so few it is a blot");
            ShaderPreset glow = ShaderLooks.Find("Soft glow");
            Assert.True(ShaderLooks.At(glow, 0).Glow < ShaderLooks.At(glow, 1).Glow && ShaderLooks.At(glow, 1).Glow < ShaderLooks.At(glow, 2).Glow, "glow grows");
            ShaderPreset night = ShaderLooks.Find("Night vision");
            Assert.Equal(1.25, ShaderLooks.At(night, 2).TintG, "the colour it goes towards does not change");
        }

        // Left to run for hours - which is what this app is for - the count
        // of frames must not grow until one pixel cannot be told from the
        // next and the grain turns to streaks.
        public static void TestGrainKeepsItsCountOfFramesSmall()
        {
            foreach (string name in new string[] { "Old television", "Old film", "Night vision" })
            {
                string source = ShaderLooks.Source(ShaderLooks.Find(name));
                Assert.Contains("fmod(floor(time * 24.0), 997.0)", source, name + " wraps its count");
                Assert.False(source.Contains("floor(time * 24.0) * 7.31"), name + " has no count left to grow");
            }
        }

        // A look whose file was deleted showed as "Off" while the app went on
        // trying it - and choosing Off, already chosen, did nothing.
        public static void TestALookWhoseFileHasGoneIsSwitchedOff()
        {
            using (MainForm f = new MainForm(false))
            {
                f.SetShader(ShaderLooks.FILE + "rk-test-not-there.hlsl", 1);
                Assert.True(ShaderLooks.IsOn(f.Shader), "a look is on");
                f.ShowPage(MainForm.PAGE_LOOKS);
                Assert.False(ShaderLooks.IsOn(f.Shader), "opening the page, which cannot list it, switches it off");
            }
        }

        // What was saved by an older build still means the same look.
        public static void TestTheLooksThereWereBeforeAreStillThereByName()
        {
            foreach (string name in new string[] { "Sharper", "Vivid", "Sharper and vivid", "Warm evening", "Cinema", "Cool and clean", "Black and white" })
                Assert.True(ShaderLooks.Find(name) != null, name);
            for (int i = 0; i < 7; i++)
                Assert.True(ShaderLooks.Presets[i].Glow == 0 && ShaderLooks.Presets[i].Levels == 0, ShaderLooks.Presets[i].Name + " is as it was");
        }

        // ---------- macros as text ----------

        static PictureShelf Shelf()
        {
            PictureShelf s = new PictureShelf();
            s.Add("Buy button-20261110-213000.png", "Buy button-20261110-213000-mask-20261110-213500.png");
            s.Add("Shop title-20261101-090000.png", null);
            return s;
        }

        static string Lines(params string[] lines)
        {
            return string.Join("\r\n", lines) + "\r\n";
        }

        static List<MacroStep> Read(string text)
        {
            List<MacroStep> steps = new List<MacroStep>();
            string why = MacroText.Read(text, Shelf(), steps);
            Assert.Equal(null, why, "understood: " + text);
            return steps;
        }

        static string Wrong(string text)
        {
            List<MacroStep> steps = new List<MacroStep>();
            string why = MacroText.Read(text, Shelf(), steps);
            Assert.True(why != null, "refused: " + text);
            return why;
        }

        static string Saved(IList<MacroStep> steps)
        {
            Macro m = new Macro();
            m.Name = "m";
            foreach (MacroStep s in steps) m.Steps.Add(s);
            return WatchStore.SerializeMacro(m);
        }

        public static void TestAMacroReadsAsItWouldBeSaid()
        {
            Macro m = new Macro();
            m.Steps.Add(MacroStep.Repeat(RepeatMode.Forever, 2));
            m.Steps.Add(MacroStep.ClickPicture("Buy button-20261110-213000.png", null, 3000));
            m.Steps.Add(MacroStep.Key(0x45, 50));
            m.Steps.Add(MacroStep.Wait(500));
            m.Steps.Add(MacroStep.EndRepeat());
            Assert.Equal(Lines("repeat forever", "    click picture \"Buy button\" wait 3", "    press E", "    wait 0.5", "end"),
                         MacroText.Write(m.Steps, Shelf()), "a step a line, what repeats moved in");
        }

        // Every kind of step the editor can make, out to text and back, is
        // the same step - compared as it would be saved.
        public static void TestEveryKindOfStepComesBackAsItWas()
        {
            List<MacroStep> all = new List<MacroStep>();
            all.Add(MacroStep.Key(0x45, 50));
            all.Add(MacroStep.Key(0x20, 750));
            all.Add(MacroStep.Key(0x25, 50));                   // Left arrow: a name of two words
            all.Add(MacroStep.Key(0xA0, 50));                   // the left Shift, not just any Shift
            all.Add(MacroStep.Key(0x37, 50));                   // the 7 key
            all.Add(MacroStep.Key(0xDB, 50));                   // a key with only Windows' name for it
            all.Add(MacroStep.KeyDown(0x57));
            all.Add(MacroStep.KeyUp(0x57));
            all.Add(MacroStep.Wait(1250));
            all.Add(MacroStep.Click(0.5, 0.25, false));
            all.Add(MacroStep.Click(0.125, 0.9, true));
            all.Add(MacroStep.ClickHere(false));
            all.Add(MacroStep.ClickHere(true));
            all.Add(MacroStep.Type("plain"));
            MacroStep chat = MacroStep.Type("he said \"hi\" \\ and left");
            chat.InChat = true;
            all.Add(chat);
            all.Add(MacroStep.ClickPicture("Buy button-20261110-213000.png", "Buy button-20261110-213000-mask-20261110-213500.png", 0));
            MacroStep fussy = MacroStep.ClickPicture("Shop title-20261101-090000.png", null, 3000);
            fussy.RightButton = true; fussy.Tolerance = 0.9; fussy.Box = "Shop corner"; fussy.OffX = 20; fussy.OffY = -5; fussy.StopIfMissing = true;
            all.Add(fussy);
            all.Add(MacroStep.WaitPicture("Shop title-20261101-090000.png", null, 10000, false));
            MacroStep gone = MacroStep.WaitPicture("Buy button-20261110-213000.png", "Buy button-20261110-213000-mask-20261110-213500.png", 20000, true);
            gone.StopIfMissing = true;
            all.Add(gone);
            all.Add(MacroStep.Move(0.3, 0.4));
            all.Add(MacroStep.Drag(0.4, 0.5, 0.6, 0.5, 500, false));
            all.Add(MacroStep.Drag(0.45, 0.5, 0.75, 0.5, 400, true));
            all.Add(MacroStep.Scroll(0.5, 0.5, 3));
            all.Add(MacroStep.Scroll(0.2, 0.8, -4));
            MacroStep wheelHere = MacroStep.Scroll(0.5, 0.5, -2);
            wheelHere.AtPointer = true;
            all.Add(wheelHere);
            all.Add(MacroStep.Repeat(RepeatMode.Times, 5));
            all.Add(MacroStep.Repeat(RepeatMode.Forever, 2));
            MacroStep until = MacroStep.Repeat(RepeatMode.UntilSeen, 2);
            until.Picture = "Shop title-20261101-090000.png"; until.Tolerance = 0.75;
            all.Add(until);
            all.Add(MacroStep.Wait(100));
            all.Add(MacroStep.EndRepeat());
            all.Add(MacroStep.EndRepeat());
            MacroStep untilGone = MacroStep.Repeat(RepeatMode.UntilGone, 2);
            untilGone.Picture = "Buy button-20261110-213000.png"; untilGone.Mask = "Buy button-20261110-213000-mask-20261110-213500.png";
            all.Add(untilGone);
            all.Add(MacroStep.Wait(100));
            all.Add(MacroStep.EndRepeat());
            all.Add(MacroStep.EndRepeat());

            string text = MacroText.Write(all, Shelf());
            List<MacroStep> back = Read(text);
            Assert.Equal(all.Count, back.Count, "as many steps:\r\n" + text);
            for (int i = 0; i < all.Count; i++)
                Assert.Equal(Saved(new MacroStep[] { all[i] }), Saved(new MacroStep[] { back[i] }), "step " + (i + 1) + ", " + all[i].Describe());
            Assert.Equal(text, MacroText.Write(back, Shelf()), "and it writes out the same again");
        }

        // The examples in the macros window, which is what most people start from.
        public static void TestTheExamplesComeBackAsTheyWere()
        {
            for (int i = 0; i < MacroForm.ExampleNames.Length; i++)
            {
                Macro m = MacroForm.Example(i);
                List<MacroStep> back = Read(MacroText.Write(m.Steps, Shelf()));
                Assert.Equal(m.Steps.Count, back.Count, m.Name);
                for (int s = 0; s < back.Count; s++)
                    Assert.Equal(m.Steps[s].Describe(), back[s].Describe(), m.Name + ", step " + (s + 1));
            }
        }

        public static void TestItIsForgivingAboutHowItIsTyped()
        {
            List<MacroStep> steps = Read(Lines(
                "# jump, then wait", "", "  PRESS space  ", "Wait 2s", "click 50% 50%", "// another note",
                "repeat 3 times:", "tap e", "hold w for 1,5", "scroll down 2", "wait until \"shop title\" shows up to 5", "end repeat"));
            Assert.Equal("Press Space | Wait 2s | Click 50% across, 50% down | Repeat 3 times: | Press E | Hold W for 1.5s | "
                       + "Scroll down 2 where the pointer is | Wait until Shop title appears, up to 5s | End of repeat",
                         string.Join(" | ", steps.ConvertAll<string>(delegate(MacroStep s) { return s.Describe(); }).ToArray()),
                         "capitals, spaces, notes, a comma for a point, a colon after repeat");
        }

        public static void TestAWrongLineSaysWhichAndWhy()
        {
            Assert.Contains("Line 3: there is no key called \"Spcae\"", Wrong(Lines("wait 1", "", "press Spcae")), "the line is counted as the person sees it");
            Assert.Contains("don't know \"jump\"", Wrong("jump"), "a word it has never heard of");
            Assert.Contains("press, hold", Wrong("jump"), "and what a line can start with");
            Assert.Contains("isn't a number of seconds", Wrong("wait soon"), "the editor's own words for a bad time");
            Assert.Contains("\"90\": Keep it under a minute.", Wrong("wait 90"), "and its own limit, said once");
            Assert.Contains("0 to 100", Wrong("click 150 50"), "a place outside the window");
            Assert.Contains("how far down", Wrong("click 50"), "half a place");
            Assert.Contains("in quotes", Wrong("say hello"), "typing without quotes");
            Assert.Contains("never closed", Wrong("say \"hello"), "a quote left open");
            Assert.Contains("didn't expect \"now\"", Wrong("wait 1 now"), "something left over");
            Assert.Contains("no key called \"E now\"", Wrong("press E now"), "or a key's name that runs on");
            Assert.Contains("2 to 9999", Wrong("repeat 1\nwait 1\nend"), "the editor's limit on a repeat");
            Assert.Contains("1 to 50", Wrong("scroll up 500"), "and on the wheel");
            Assert.Contains("same spot", Wrong("drag 10 10 to 10 10"), "a drag that goes nowhere");
            Assert.Contains("before click or drag", Wrong("right press E"), "right with the wrong thing");
            Assert.Contains("what ends it", Wrong("repeat until \"Shop title\"\nwait 1\nend"), "a repeat that never says when");
        }

        public static void TestTheSmallSlipsAreRefused()
        {
            Assert.Contains("0 to 100", Wrong("click NaN 50"), "not-a-number is not a place");
            Assert.Contains("in quotes", Wrong("type say \"hello\""), "type and say are one or the other");
            Assert.Contains("button on the mouse", Wrong("press LButton"), "a mouse button by Windows' name");
            Assert.Contains("button on the mouse", Wrong("press key 1"), "or by its number");
            Assert.Contains("button on the mouse", Wrong("hold RButton"), "held, too");
            Assert.Equal((byte)0x1B, Read("press Esc")[0].Vk, "Esc is a key, though the key window uses it to cancel");
        }

        public static void TestKeysAreWrittenAsTheyArePrinted()
        {
            string[] printed = { "[", "219", "\\", "220", "]", "221", "'", "222", ";", "186", ",", "188" };
            for (int i = 0; i < printed.Length; i += 2)
            {
                byte vk = byte.Parse(printed[i + 1]);
                Assert.Equal(vk, Read("press " + printed[i])[0].Vk, "read: " + printed[i]);
                Assert.Equal(Lines("press " + printed[i]), MacroText.Write(new MacroStep[] { MacroStep.Key(vk, 50) }, null), "written: " + printed[i]);
            }
        }

        public static void TestTheLineAProblemIsAboutIsFound()
        {
            Assert.Equal(3, MacroText.LineOf(Wrong(Lines("wait 1", "", "press Spcae"))), "counted from 1, blank lines and all");
            Assert.Equal(0, MacroText.LineOf(Wrong("repeat forever\npress E")), "a repeat with no end is about the whole macro");
            Assert.Equal(0, MacroText.LineOf(null), "no problem, no line");

            using (MacroTextDialog d = new MacroTextDialog(new List<MacroStep>(), Shelf(), null))
            {
                d.Written = "press E\r\nwait 1\r\npress Spcae\r\nwait 2";
                Assert.False(d.TryUse(), "a line is wrong");
                Assert.Equal("press Spcae", d.Selected, "and it is the one picked out in the box");
            }
        }

        // A picture another macro or a watcher uses keeps what was painted
        // out of it when it is named in text - even though the folder, read
        // last, knows the file without it.
        public static void TestAPictureInUseElsewhereKeepsItsPaintedOutCopy()
        {
            string folder = Path.Combine(Path.GetTempPath(), "rk-shelf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                foreach (string name in new string[] { "Buy-20261110-213000.png", "Sell-20261110-213000.png", "Gem-20261110-213000.png" })
                    File.WriteAllText(Path.Combine(folder, name), "");
                WatchKit k = Kit();
                Macro other = new Macro();
                other.Name = "Other";
                other.Steps.Add(MacroStep.ClickPicture("Buy-20261110-213000.png", "Buy-20261110-213000-mask-20261110-220000.png", 0));
                k.Store.Macros.Add(other);
                Watcher w = new Watcher();
                w.Name = "Sees sell"; w.Kind = WatchKind.ImageFound;
                w.TemplateFile = "Sell-20261110-213000.png"; w.MaskFile = "Sell-20261110-213000-mask-20261110-220000.png";
                k.Store.Watchers.Add(w);

                PictureShelf shelf = PictureShelf.For(k.Store, null, folder);
                string file, mask;
                Assert.True(shelf.Find("Buy", out file, out mask), "another macro's picture");
                Assert.Equal("Buy-20261110-213000-mask-20261110-220000.png", mask, "with its painted-out copy");
                Assert.True(shelf.Find("Sell", out file, out mask), "a watcher's picture");
                Assert.Equal("Sell-20261110-213000-mask-20261110-220000.png", mask, "with its own");
                Assert.True(shelf.Find("Gem", out file, out mask), "one only in the folder");
                Assert.Equal(null, mask, "which has none");

                List<MacroStep> steps = new List<MacroStep>();
                Assert.Equal(null, MacroText.Read("click picture \"Buy\"", shelf, steps), "named in text");
                Assert.Equal("Buy-20261110-213000-mask-20261110-220000.png", steps[0].Mask, "the step has the painted-out copy");
            }
            finally { try { Directory.Delete(folder, true); } catch { } }
        }

        // The editor refuses these with the buttons, so text must too.
        public static void TestRepeatsHaveToMatchUp()
        {
            Assert.Contains("has no end", Wrong("repeat forever\npress E"), "a repeat left open");
            Assert.Contains("no repeat above it", Wrong("press E\nend"), "an end with nothing to end");
            Assert.Contains("too many repeats", Wrong("repeat 2\nrepeat 2\nrepeat 2\nrepeat 2\nwait 1\nend\nend\nend\nend"), "four deep");
        }

        // A macro from someone else names pictures this PC does not have.
        public static void TestAPictureThisPcDoesNotHaveIsNamed()
        {
            string why = Wrong("click picture \"Sell button\"");
            Assert.Contains("no picture called \"Sell button\"", why, "which one");
            Assert.Contains("Buy button", why, "and the ones there are");
            List<MacroStep> none = new List<MacroStep>();
            Assert.Contains("Make one first", MacroText.Read("click picture \"Sell button\"", new PictureShelf(), none), "or how to get one, when there are none");

            // No picture yet is not an error here: the editor asks before it saves.
            List<MacroStep> steps = Read("click picture \"\"");
            Assert.Equal(null, steps[0].Picture, "kept, with no picture");
            Macro m = new Macro();
            m.Name = "m";
            m.Steps.AddRange(steps);
            Assert.Contains("no picture yet", m.Problem(), "and the editor's own check catches it");
        }

        public static void TestAPictureKeepsItsPaintedOutCopy()
        {
            List<MacroStep> steps = Read("click picture \"buy BUTTON\"");
            Assert.Equal("Buy button-20261110-213000.png", steps[0].Picture, "found whatever the capitals");
            Assert.Equal("Buy button-20261110-213000-mask-20261110-213500.png", steps[0].Mask, "with what was painted out of it");
        }

        // Two pictures cut on different days and called the same.
        public static void TestTwoPicturesCalledTheSameAreToldApart()
        {
            PictureShelf shelf = new PictureShelf();
            shelf.Add("Buy-20261110-213000.png", null);
            shelf.Add("Buy-20261111-080000.png", null);
            List<MacroStep> steps = new List<MacroStep>();
            steps.Add(MacroStep.ClickPicture("Buy-20261110-213000.png", null, 0));
            steps.Add(MacroStep.ClickPicture("Buy-20261111-080000.png", null, 0));
            string text = MacroText.Write(steps, shelf);
            Assert.Equal(Lines("click picture \"Buy\"", "click picture \"Buy-20261111-080000\""), text, "the second by its whole name");
            List<MacroStep> back = new List<MacroStep>();
            Assert.Equal(null, MacroText.Read(text, shelf, back), "read back");
            Assert.Equal("Buy-20261110-213000.png", back[0].Picture, "the first");
            Assert.Equal("Buy-20261111-080000.png", back[1].Picture, "and the second, not the first twice");
        }

        class NoCapture : ICapture
        {
            public Pixels Grab(int pid, out WatchState state) { state = WatchState.NotInAGame; return null; }
        }

        class NoReader : IReadText
        {
            public string Read(Pixels image) { return ""; }
        }

        static WatchKit Kit()
        {
            WatchKit k = new WatchKit();
            k.Store = new WatchStore(Path.Combine(Path.GetTempPath(), "rk-text-" + Guid.NewGuid().ToString("N") + ".dat"));
            k.Capture = new NoCapture();
            k.Reader = new NoReader();
            k.Engine = new WatchEngine(k.Capture, k.Reader);
            k.Clients = delegate { return new WatchedClient[0]; };
            k.Accounts = delegate { return (IList<string>)new List<string>(); };
            k.Log = delegate(string s) { };
            return k;
        }

        // In the editor: text in, the list of steps changes; bad text in,
        // nothing changes.
        public static void TestTheEditorTakesTextInPlaceOfItsSteps()
        {
            Macro m = MacroForm.Example(0);
            using (MacroEditDialog d = new MacroEditDialog(m, Kit()))
            {
                Assert.Equal(Lines("repeat forever", "    press Space", "    wait 5", "end"), d.AsText(), "what is there, as text");
                Assert.Equal(null, d.TakeText("press E\nwait 1\nclick 25 75"), "new text");
                Assert.Equal("Press E | Wait 1s | Click 25% across, 75% down", d.StepsSaid, "is the steps now");
                Assert.Contains("Line 2", d.TakeText("press E\nfly away"), "bad text says so");
                Assert.Equal(3, d.StepCount, "and what was there stays");
            }
        }

        public static void TestTheTextWindowKeepsNothingUntilEveryLineIsRight()
        {
            using (MacroTextDialog d = new MacroTextDialog(MacroForm.Example(1).Steps, Shelf(), null))
            {
                Assert.Contains("click 50 50", d.Written, "it opens on the steps there are");
                d.Written = "press E\r\nwait";
                Assert.False(d.TryUse(), "a line is wrong");
                Assert.Contains("Line 2", d.ProblemText, "which");
                Assert.Equal(null, d.Result, "nothing kept");
                d.Written = "press E\r\nwait 1";
                Assert.True(d.TryUse(), "put right");
                Assert.Equal(2, d.Result.Count, "kept");
            }
        }

        // The editor's box for what to type is one line. Text is not a way
        // round that: a line break typed into a game is Enter.
        public static void TestWhatIsTypedIsOneLineAsItIsInTheEditor()
        {
            Assert.Contains("tab inside the quotes", Wrong("say \"a\tb\""), "a tab between the quotes");
            List<MacroStep> steps = Read("type \"C:\\new\"");
            Assert.Equal("C:\\new", steps[0].Text, "a backslash and a letter are a backslash and a letter, not a line break");
            MacroStep odd = MacroStep.Type("one\r\ntwo\tthree");
            string text = MacroText.Write(new MacroStep[] { odd }, null);
            Assert.Equal(Lines("type \"onetwothree\""), text, "and a step that somehow holds one is written without it");
        }

        // A box that isn't there is not an error when a macro plays - the
        // whole window is looked in - so a name spelt wrong would seem to work.
        public static void TestABoxHasToBeOneThereIs()
        {
            PictureShelf shelf = Shelf();
            shelf.Boxes = new List<string>(new string[] { "Shop corner", "Top bar" });
            List<MacroStep> steps = new List<MacroStep>();
            Assert.Equal(null, MacroText.Read("click picture \"Buy button\" in \"shop CORNER\"", shelf, steps), "one there is, whatever the capitals");
            Assert.Equal("shop CORNER", steps[0].Box, "kept");
            string why = MacroText.Read("click picture \"Buy button\" in \"Shpo corner\"", shelf, new List<MacroStep>());
            Assert.Contains("Line 1: there is no box called \"Shpo corner\"", why, "one there isn't");
            Assert.Contains("Shop corner, Top bar", why, "and the ones there are");
            shelf.Boxes.Clear();
            Assert.Contains("Draw one first", MacroText.Read("wait for \"Buy button\" up to 5 in \"Any\"", shelf, new List<MacroStep>()), "or how to get one");
        }

        public static void TestTheEditorKnowsItsBoxesAndKeepsOnesAlreadyUsed()
        {
            WatchKit k = Kit();
            WatchRegion r = new WatchRegion();
            r.Name = "Shop corner"; r.PlaceId = "123";
            k.Store.Regions.Add(r);
            MacroStep uses = MacroStep.ClickPicture("Buy-20261110-213000.png", null, 0);
            uses.Box = "An old box";
            PictureShelf shelf = PictureShelf.For(k.Store, new MacroStep[] { uses }, null);
            Assert.True(shelf.HasBox("shop corner"), "a box drawn for a game");
            Assert.True(shelf.HasBox("An old box"), "and one the macro already names, so opening it as text does not break it");
            Assert.False(shelf.HasBox("Nowhere"), "but not one nobody drew");
        }

        // A picture cut again keeps its name. The newer one is the one meant.
        public static void TestOfTwoPicturesInTheFolderTheNewerIsFound()
        {
            string folder = Path.Combine(Path.GetTempPath(), "rk-shelf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                foreach (string name in new string[] { "Buy-20261110-213000.png", "Buy-20261111-080000.png", "Buy-20261111-080000-mask-20261111-080500.png" })
                    File.WriteAllText(Path.Combine(folder, name), "");
                PictureShelf shelf = PictureShelf.For(null, null, folder);
                string file, mask;
                Assert.True(shelf.Find("Buy", out file, out mask), "found by what it is called");
                Assert.Equal("Buy-20261111-080000.png", file, "the one cut last");
                Assert.Equal(2, shelf.Names().Count, "and a painted-out copy is not a picture of its own");
            }
            finally { try { Directory.Delete(folder, true); } catch { } }
        }

        // ---------- the guide ----------

        // It opens in the browser as a page, not in Notepad as marks and bars.
        public static void TestTheGuideIsWrittenOutAsAPage()
        {
            string page = Guide.Html(Guide.Text());
            Assert.True(Guide.File.EndsWith(".html"), "a page");
            Assert.Contains("<h1>Making mods for RobloxKeeper</h1>", page, "its title");
            Assert.Contains("<h2>3. Macros as text</h2>", page, "its parts");
            Assert.Contains("<td><code>press E</code></td><td>Press a key and let go</td>", page, "tables are tables");
            Assert.Contains("<pre>repeat forever\n    click picture \"Buy button\" wait 3\n", page, "examples keep their lines and spaces");
            Assert.Contains("<li>a <b>macro</b> plays keys and clicks", page, "lists and bold");
            Assert.Contains("float4 main(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target", page, "shader text is left alone");
            Assert.Contains("col *= 1.15;", page, "a star in an example is a star");
            foreach (string left in new string[] { "```", "| ---", "**", "<script", "http" })
                Assert.False(page.Contains(left), "nothing left over, nothing run, nothing fetched: " + left);
            Assert.Equal(page.Split(new string[] { "<pre>" }, StringSplitOptions.None).Length,
                         page.Split(new string[] { "</pre>" }, StringSplitOptions.None).Length, "every example is closed");
        }

        // The guide is inside the exe now, so the exe carries its words. The
        // same words the source is checked for: an exe that names scripts and
        // shells is what antivirus takes for a dropper.
        public static void TestTheGuideCarriesNothingThatReadsAsAScript()
        {
            string guide = Guide.Text();
            foreach (string bad in new string[] { ".bat", "tasklist", "ping -n", "%~f0", "cmd.exe", "powershell", ".ps1", ".vbs" })
                Assert.False(guide.IndexOf(bad, StringComparison.OrdinalIgnoreCase) >= 0, "the guide has " + bad);
        }

        static string Section(string guide, string from, string to)
        {
            int a = guide.IndexOf(from, StringComparison.Ordinal);
            int b = to == null ? guide.Length : guide.IndexOf(to, a + 1, StringComparison.Ordinal);
            Assert.True(a >= 0 && b > a, "the guide has a part called " + from);
            return guide.Substring(a, b - a);
        }

        // The ``` blocks of a piece of the guide, of one kind ("" for plain).
        static List<string> Blocks(string text, string kind)
        {
            List<string> blocks = new List<string>();
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            StringBuilder now = null;
            foreach (string line in lines)
            {
                if (line.StartsWith("```"))
                {
                    if (now != null) { blocks.Add(now.ToString()); now = null; }
                    else if (line.Substring(3).Trim() == kind) now = new StringBuilder();
                    else now = new StringBuilder("\0");      // a block of another kind: read past it
                }
                else if (now != null) now.Append(line).Append('\n');
            }
            blocks.RemoveAll(delegate(string b) { return b.StartsWith("\0"); });
            return blocks;
        }

        static PictureShelf GuidePictures()
        {
            PictureShelf s = new PictureShelf();
            foreach (string name in new string[] { "Buy button", "Buy", "Collect", "Shop title", "Shop", "Loading", "Full" })
                s.Add(name + "-20261110-213000.png", null);
            return s;
        }

        public static void TestTheGuideIsInTheExe()
        {
            string guide = Guide.Text();
            Assert.True(guide != null && guide.Length > 2000, "it is carried as a resource");
            Assert.Contains("why there is no Lua", guide, "and says what cannot be done, first");
        }

        // The guide lists what comes with the app. A pack or a look added
        // without a line there, or a line for one that is gone, fails here.
        public static void TestTheGuideListsWhatComesWithTheApp()
        {
            string guide = Guide.Text();
            foreach (PremadePack p in PremadeMods.All) Assert.Contains("| " + p.Name + " |", guide, "the pack " + p.Name);
            foreach (ShaderPreset p in ShaderLooks.Presets) Assert.Contains("| " + p.Name + " | " + p.Says.TrimEnd('.') + " |", guide, "the look " + p.Name);
            Assert.Contains("`" + string.Join(" ", LooksMods.Blocked) + "`", guide, "every kind of file a pack is refused for");
            foreach (string folder in LooksMods.Folders) Assert.Contains("`" + folder + "`", guide, "allowed: " + folder);
            Assert.Contains(PremadeMods.POINTER.Substring(PremadeMods.POINTER.LastIndexOf('\\') + 1), guide, "the pointer's file name");
        }

        public static void TestEveryMacroInTheGuideIsUnderstood()
        {
            string part = Section(Guide.Text(), "## 3. Macros as text", null);
            List<string> blocks = Blocks(part, "");
            int macros = 0;
            foreach (string block in blocks)
            {
                if (block.StartsWith("Line ")) continue;        // what an error looks like, not a macro
                List<MacroStep> steps = new List<MacroStep>();
                Assert.Equal(null, MacroText.Read(block, GuidePictures(), steps), "this one from the guide:\r\n" + block);
                Macro m = new Macro();
                m.Name = "from the guide";
                m.Steps.AddRange(steps);
                Assert.Equal(null, m.Problem(), "and the editor would save it:\r\n" + block);
                macros++;
            }
            Assert.True(macros >= 6, "the examples were found: " + macros);
        }

        // The table of lines: the first thing in each row is something that
        // can be typed.
        public static void TestEveryLineInTheGuidesTableIsUnderstood()
        {
            string part = Section(Guide.Text(), "### Every line it knows", "### Pictures");
            int rows = 0;
            foreach (string line in part.Replace("\r\n", "\n").Split('\n'))
            {
                if (!line.StartsWith("| `")) continue;
                int end = line.IndexOf('`', 3);
                string typed = line.Substring(3, end - 3);
                string text = typed.StartsWith("repeat") ? typed + "\nwait 1\nend"
                            : typed.StartsWith("end") ? "repeat 2\nwait 1\n" + typed
                            : typed.StartsWith("alike") || typed.StartsWith("in ") || typed.StartsWith("offset") ? "click picture \"Buy\" " + typed
                            : typed;
                List<MacroStep> steps = new List<MacroStep>();
                Assert.Equal(null, MacroText.Read(text, GuidePictures(), steps), "the guide's line: " + typed);
                rows++;
            }
            Assert.True(rows >= 33, "the rows of both tables were found: " + rows);
        }

        // What the guide shows a wrong line saying is what one says.
        public static void TestTheGuidesExampleOfAWrongLineIsWhatTheAppSays()
        {
            string shown = null;
            foreach (string block in Blocks(Section(Guide.Text(), "### When a line is wrong", "### Some to start from"), ""))
                if (block.StartsWith("Line ")) shown = block.Trim();
            Assert.True(shown != null && shown.EndsWith("..."), "the guide shows one, cut short: " + shown);
            string said = Wrong(Lines("press E", "wait 1", "press Spcae"));
            Assert.True(said.StartsWith(shown.Substring(0, shown.Length - 3)), "the guide: " + shown + " - the app: " + said);
        }

        // Every key the guide names is one that can be written.
        public static void TestEveryKeyTheGuideNamesIsAKey()
        {
            string guide = Guide.Text().Replace("\r\n", "\n");
            int from = guide.IndexOf("Keys are written as they are printed on the keyboard:", StringComparison.Ordinal);
            int to = guide.IndexOf(". The mouse", from, StringComparison.Ordinal);
            Assert.True(from >= 0 && to > from, "the guide lists the keys");
            string[] parts = guide.Substring(from, to - from).Split('`');
            int keys = 0;
            for (int i = 1; i < parts.Length; i += 2)
            {
                List<MacroStep> steps = Read("press " + parts[i]);
                Assert.True(steps.Count == 1 && steps[0].Vk != 0, "a key: " + parts[i]);
                keys++;
            }
            Assert.True(keys >= 12, "they were found: " + keys);
        }

        public static void TestEveryShaderInTheGuideCompiles()
        {
            string part = Section(Guide.Text(), "## 2. Shader looks", "## 3. Macros as text");
            List<string> blocks = Blocks(part, "hlsl");
            Assert.Equal(2, blocks.Count, "the smallest shader, and the lines to copy");
            Assert.Equal(null, Shader.Overlay.CompileProblem(ShaderLooks.Header + blocks[0]), "the smallest shader");

            // Every line to copy, put where the guide says to put them.
            string with = blocks[0].Replace("    return float4", blocks[1] + "    return float4");
            Assert.True(with.Contains("sin(time"), "they went in");
            Assert.Equal(null, Shader.Overlay.CompileProblem(ShaderLooks.Header + with), "with every line to copy in it");

            // And each by itself, which is how somebody copies one: a line
            // that leans on another's would fail them with nothing to say why.
            string[] each = blocks[1].Trim().Split(new string[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.True(each.Length >= 8, "the lines to copy were told apart: " + each.Length);
            foreach (string one in each)
                Assert.Equal(null, Shader.Overlay.CompileProblem(ShaderLooks.Header + blocks[0].Replace("    return float4", one + "\n    return float4")),
                             "by itself:\r\n" + one);
        }
    }
}
