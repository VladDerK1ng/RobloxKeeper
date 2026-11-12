using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace RobloxKeeper.Tests
{
    // The Looks page and what it shares with the main window - against a
    // pretend Roblox made in the temp folder, never the real one.
    static class LooksPageTests
    {
        class Pretend : IDisposable
        {
            public readonly string Root;
            public readonly LooksPlaces At = new LooksPlaces();

            public Pretend()
            {
                Root = Path.Combine(Path.GetTempPath(), "rk-looks-" + Guid.NewGuid().ToString("N"));
                At.Versions = Path.Combine(Root, "Versions");
                At.Library = Path.Combine(Root, "mods");
                At.Backup = Path.Combine(Root, "backup");
                Directory.CreateDirectory(At.Versions);
            }

            // A version folder as Roblox makes one: it has the player in it.
            public string Version(string name)
            {
                string folder = Path.Combine(At.Versions, name);
                Directory.CreateDirectory(Path.Combine(folder, "content", "fonts"));
                File.WriteAllText(Path.Combine(folder, "RobloxPlayerBeta.exe"), "");
                File.WriteAllText(Path.Combine(folder, "content", "fonts", "Arial.ttf"), "roblox's own font");
                return folder;
            }

            // A pack that replaces that font.
            public string Pack(string name)
            {
                string folder = Path.Combine(Root, "packs", name);
                Directory.CreateDirectory(Path.Combine(folder, "content", "fonts"));
                File.WriteAllText(Path.Combine(folder, "content", "fonts", "Arial.ttf"), "a nicer font");
                return folder;
            }

            public void Dispose()
            {
                try { Directory.Delete(Root, true); } catch { }
            }
        }

        static LooksChoices Smooth()
        {
            LooksChoices c = new LooksChoices();
            c.AntiAliasing = LooksAntiAliasing.X4;
            c.QualityLevel = 16;
            return c;
        }

        public static void TestChoicesAreSaidInPlainWords()
        {
            Assert.Equal("nothing changed", Looks.Describe(new LooksChoices()), "the defaults");
            LooksChoices c = Smooth();
            c.Grass = LooksGrass.None;
            c.DpiScalingOff = true;
            Assert.Equal("smooth edges 4x, quality 16, grass none, no display scaling", Looks.Describe(c), "everything set");
            Assert.False(Looks.Any(new LooksChoices()), "nothing asked for");
            Assert.True(Looks.Any(c), "something asked for");
        }

        public static void TestAProblemSaysWhatToDoAboutIt()
        {
            Assert.Equal(null, Looks.Failures(new List<LooksFailure>()), "no problem");
            List<LooksFailure> failed = new List<LooksFailure>();
            failed.Add(new LooksFailure("version-a\\content\\fonts\\Arial.ttf", "a Roblox window has it open"));
            string said = Looks.Failures(failed);
            Assert.Contains("1 file", said, "how many");
            Assert.Contains("has it open", said, "why");
            Assert.Contains("Close every Roblox window", said, "and what to do");
        }

        // Roblox installs a new version into a new folder. What was asked for
        // is written there without anyone opening the page again.
        public static void TestANewRobloxVersionGetsTheLooksPutBack()
        {
            using (Pretend p = new Pretend())
            {
                string first = p.Version("version-aaa");
                LooksChoices c = Smooth();
                Assert.Equal(1, LooksFlags.Apply(p.At.Versions, c).Written, "written to the one version there is");
                Assert.Equal(null, Looks.Reapply(p.At, c), "nothing new: nothing to do, and nothing said");

                string second = p.Version("version-bbb");
                Assert.True(Looks.FlagsMissingSomewhere(p.At.Versions), "the new one has no settings");
                string said = Looks.Reapply(p.At, c);
                Assert.Contains("new version", said, "it says what happened");
                Assert.Contains("graphics settings", said, "and what it put back");
                Assert.True(File.Exists(LooksFlags.PathIn(second)), "written to the new version");
                Assert.Equal(null, Looks.Reapply(p.At, c), "and only the once");
            }
        }

        // Nothing switched on: the once-a-minute look touches nothing.
        public static void TestNothingIsWrittenWhenNothingWasAskedFor()
        {
            using (Pretend p = new Pretend())
            {
                string v = p.Version("version-aaa");
                Assert.Equal(null, Looks.Reapply(p.At, new LooksChoices()), "nothing to do");
                Assert.False(File.Exists(LooksFlags.PathIn(v)), "no settings file made");
                Assert.False(Directory.Exists(p.At.Backup), "no backup folder made");
                Assert.False(Directory.Exists(p.At.Library), "and no library either");
            }
        }

        public static void TestModPacksGoOntoANewVersionToo()
        {
            using (Pretend p = new Pretend())
            {
                p.Version("version-aaa");
                LooksMods mods = new LooksMods(p.At.Library);
                Assert.Equal(null, mods.Import(p.Pack("Nice font")), "the pack is taken");
                mods.Apply(p.At.Versions, p.At.Backup);

                string second = p.Version("version-bbb");
                string said = Looks.Reapply(p.At, new LooksChoices());
                Assert.Contains("mod packs", said, "it says so");
                Assert.Equal("a nicer font", File.ReadAllText(Path.Combine(second, "content", "fonts", "Arial.ttf")), "the new version has the pack's font");
            }
        }

        // The one button: Roblox exactly as it was installed.
        public static void TestPuttingRobloxBackUndoesEverything()
        {
            using (Pretend p = new Pretend())
            {
                string v = p.Version("version-aaa");
                string font = Path.Combine(v, "content", "fonts", "Arial.ttf");
                LooksFlags.Apply(p.At.Versions, Smooth());
                LooksMods mods = new LooksMods(p.At.Library);
                mods.Import(p.Pack("Nice font"));
                mods.Apply(p.At.Versions, p.At.Backup);
                Assert.Equal("a nicer font", File.ReadAllText(font), "changed");
                Assert.True(File.Exists(LooksFlags.PathIn(v)), "and the settings written");

                string said = Looks.RestoreAll(p.At);
                Assert.Contains("as it was installed", said, "it says so");
                Assert.Equal("roblox's own font", File.ReadAllText(font), "the original is back");
                Assert.False(File.Exists(LooksFlags.PathIn(v)), "and the settings file is gone");
                Assert.Contains("as it was installed", Looks.RestoreAll(p.At), "and doing it again is harmless");
            }
        }

        public static void TestTheChoicesAreKeptWithTheSettings()
        {
            AppSettings s = new AppSettings();
            s.Looks.AntiAliasing = LooksAntiAliasing.X8;
            s.Looks.QualityLevel = 21;
            s.Looks.Grass = LooksGrass.Short;
            s.Looks.DpiScalingOff = true;
            AppSettings back = AppSettings.Parse(s.ToLines());
            Assert.Equal(Looks.Describe(s.Looks), Looks.Describe(back.Looks), "every choice comes back");
            Assert.False(Looks.Any(AppSettings.Parse(new string[] { "afk=1" }).Looks), "and settings from before Looks ask for nothing");
            Assert.Equal(0, AppSettings.Parse(new string[] { "looks_quality=99", "looks_edges=42" }).Looks.QualityLevel, "a hand-edited nonsense value is not taken");
        }

        // ---------- the page ----------

        public static void TestThePageCollectsWhatWasChosen()
        {
            using (Pretend p = new Pretend())
            using (MainForm f = new MainForm(false))
            using (LooksPage page = new LooksPage(f, new Size(902, 692), p.At))
            {
                Assert.False(Looks.Any(page.Collect()), "as first shown it asks for nothing");
                page.Choose(LooksAntiAliasing.X4, 4, LooksGrass.Far, true);
                LooksChoices c = page.Collect();
                Assert.Equal(LooksAntiAliasing.X4, c.AntiAliasing, "smooth edges");
                Assert.Equal(16, c.QualityLevel, "\"High\" is level 16");
                Assert.Equal(LooksGrass.Far, c.Grass, "grass");
                Assert.True(c.DpiScalingOff, "no stretching");
            }
        }

        // Quality is offered by name, and every name is a real level.
        public static void TestQualityNamesAreRobloxsOwnLevels()
        {
            Assert.Equal(Looks.QualityNames.Length, Looks.QualityLevels.Length, "a level for every name");
            Assert.Equal(0, Looks.QualityLevels[0], "\"Roblox decides\" sets nothing");
            foreach (int level in Looks.QualityLevels)
                Assert.True(level >= 0 && level <= LooksChoices.QUALITY_MAX, level + " is a level Roblox has");
        }
    }
}
