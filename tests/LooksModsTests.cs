using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace RobloxKeeper.Tests
{
    // Mod packs overwrite Roblox's own files, so the promise that matters is
    // the undo: whatever was applied, in whatever order, Restore leaves the
    // install exactly as Roblox shipped it. Everything here runs against a
    // made-up install under the temp folder; the real one is never looked at.
    static class LooksModsTests
    {
        // A pretend machine: a mod library, an install with one version in
        // it, and somewhere for the originals to be kept.
        class World : IDisposable
        {
            public readonly string Root;
            public readonly string Library;
            public readonly string Versions;
            public readonly string Backup;

            public World()
            {
                Root = Path.Combine(Path.GetTempPath(), "rk_looksmods_" + Guid.NewGuid().ToString("n"));
                Library = Path.Combine(Root, "mods");
                Versions = Path.Combine(Root, "Versions");
                Backup = Path.Combine(Root, "backup");
                Directory.CreateDirectory(Library);
                Directory.CreateDirectory(Versions);
                AddVersion("version-aaa111");
            }

            public string AddVersion(string name)
            {
                string v = Path.Combine(Versions, name);
                Put(Path.Combine(v, "RobloxPlayerBeta.exe"), "not really a program");
                Put(Path.Combine(v, "content", "textures", "cursor.png"), "roblox cursor " + name);
                Put(Path.Combine(v, "content", "sounds", "ouch.ogg"), "roblox ouch " + name);
                Put(Path.Combine(v, "ExtraContent", "places", "sky.tex"), "roblox sky " + name);
                Put(Path.Combine(v, "PlatformContent", "pc", "fonts.dat"), "roblox fonts " + name);
                Directory.CreateDirectory(Path.Combine(v, "content", "empty"));
                return v;
            }

            public string Version(string name) { return Path.Combine(Versions, name); }

            // A pack put straight into the library, as file path then text.
            public void AddPack(string name, params string[] pathThenText)
            {
                for (int i = 0; i < pathThenText.Length; i += 2)
                    Put(Path.Combine(Library, name, pathThenText[i]), pathThenText[i + 1]);
            }

            // The library as the app would open it, with these packs on, in
            // this order.
            public LooksMods Open(params string[] enabledInOrder)
            {
                LooksMods mods = new LooksMods(Library);
                for (int i = 0; i < enabledInOrder.Length; i++)
                {
                    mods.Move(enabledInOrder[i], i);
                    mods.SetEnabled(enabledInOrder[i], true);
                }
                return mods;
            }

            public void Dispose()
            {
                // A read-only file would stop the folder being deleted.
                foreach (string f in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(Root, true);
            }
        }

        static bool ReadOnly(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
        }

        static void Put(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        static string Text(string path)
        {
            return File.ReadAllText(path);
        }

        // Every folder and every file's bytes under a folder.
        static SortedDictionary<string, string> Snapshot(string folder)
        {
            SortedDictionary<string, string> all = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string d in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories))
                all[d.Substring(folder.Length + 1)] = "(folder)";
            foreach (string f in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                all[f.Substring(folder.Length + 1)] = Convert.ToBase64String(File.ReadAllBytes(f));
            return all;
        }

        static void SameTree(SortedDictionary<string, string> before, string folder, string what)
        {
            SortedDictionary<string, string> after = Snapshot(folder);
            foreach (string path in before.Keys)
            {
                Assert.True(after.ContainsKey(path), what + ": " + path + " is still there");
                Assert.Equal(before[path], after[path], what + ": " + path + " holds the same bytes");
            }
            foreach (string path in after.Keys)
                Assert.True(before.ContainsKey(path), what + ": " + path + " was not there to begin with");
        }

        static string Reasons(LooksModsResult result)
        {
            string all = "";
            foreach (LooksFailure f in result.Failed) all += f.Where + ": " + f.Why + "; ";
            return all;
        }

        static void MakeZip(string path, params string[] nameThenText)
        {
            using (FileStream file = new FileStream(path, FileMode.Create))
            using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Create))
                for (int i = 0; i < nameThenText.Length; i += 2)
                    using (StreamWriter w = new StreamWriter(zip.CreateEntry(nameThenText[i]).Open()))
                        w.Write(nameThenText[i + 1]);
        }

        // ---------- What a pack may hold ----------

        public static void TestOnlyTheThreeContentFoldersCanBeChanged()
        {
            Assert.Equal(null, LooksMods.WhyNot("content\\textures\\cursor.png"), "content");
            Assert.Equal(null, LooksMods.WhyNot("ExtraContent\\places\\sky.tex"), "ExtraContent");
            Assert.Equal(null, LooksMods.WhyNot("PlatformContent\\pc\\fonts.dat"), "PlatformContent");
            Assert.Equal(null, LooksMods.WhyNot("CONTENT/textures/cursor.png"), "any case, either slash");

            Assert.True(LooksMods.WhyNot("shaders\\shaders_d3d11.pack") != null, "any other folder is refused");
            Assert.True(LooksMods.WhyNot("readme.txt") != null, "so is a loose file at the top");
            Assert.True(LooksMods.WhyNot("content") != null, "and a file where the folder should be");
            Assert.True(LooksMods.WhyNot("contents\\a.png") != null, "a near miss is still a miss");
            Assert.True(LooksMods.WhyNot("") != null, "no path");
            Assert.True(LooksMods.WhyNot(null) != null, "nothing at all");
        }

        public static void TestTheFlagsFileIsNotAPacksToChange()
        {
            string why = LooksMods.WhyNot("ClientSettings\\ClientAppSettings.json");
            Assert.True(why != null, "refused");
            Assert.Contains("ClientSettings", why, "and it says which folder is the trouble");
            Assert.True(LooksMods.WhyNot("clientsettings\\ClientAppSettings.json") != null, "in any case");
        }

        public static void TestAPathThatLeavesThePackIsRefused()
        {
            string[] escapes =
            {
                "..\\evil.png",
                "content\\..\\..\\evil.png",
                "content/../../evil.png",
                "content\\textures\\..",
                "content\\.\\a.png",
                "C:\\Windows\\evil.png",
                "c:evil.png",
                "\\\\server\\share\\content\\a.png",
                "\\content\\a.png",
                "/content/a.png",
                "content\\\\a.png",
                "content\\a.png:hidden",
                "content\\a?.png"
            };
            foreach (string path in escapes)
                Assert.True(LooksMods.WhyNot(path) != null, "refused: " + path);
        }

        public static void TestProgramsAndScriptsAreRefused()
        {
            foreach (string ext in new string[] { ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".msi", ".scr", ".com", ".lnk" })
            {
                Assert.True(LooksMods.WhyNot("content\\tools\\thing" + ext) != null, "refused: " + ext);
                Assert.True(LooksMods.WhyNot("content\\tools\\thing" + ext.ToUpperInvariant()) != null, "in capitals too: " + ext);
            }
            Assert.True(LooksMods.WhyNot("content\\cursor.png.exe") != null, "a picture name in front does not help");
            Assert.True(LooksMods.WhyNot("content\\thing.exe.") != null, "nor a dot after, which Windows would drop");
            Assert.True(LooksMods.WhyNot("content\\thing.exe ") != null, "nor a space");
            Assert.Equal(null, LooksMods.WhyNot("content\\exe\\cursor.png"), "a folder that merely has the word in it is fine");
            Assert.Equal(null, LooksMods.WhyNot("content\\fonts\\font.json"), "and so is plain data");
        }

        // ---------- Getting packs into the library ----------

        public static void TestAFolderIsImportedAsAPack()
        {
            using (World w = new World())
            {
                string source = Path.Combine(w.Root, "Pink Cursor v1.2");
                Put(Path.Combine(source, "content", "textures", "cursor.png"), "pink");
                Put(Path.Combine(source, "ExtraContent", "places", "sky.tex"), "pink sky");

                LooksMods mods = new LooksMods(w.Library);
                Assert.Equal(null, mods.Import(source), "it goes in");
                Assert.Equal(1, mods.Packs.Count, "the library has one pack");
                Assert.Equal("Pink Cursor v1.2", mods.Packs[0].Name, "named after its folder, dots and all");
                Assert.True(mods.Packs[0].Enabled, "and switched on");
                Assert.Equal("pink", Text(Path.Combine(w.Library, "Pink Cursor v1.2", "content", "textures", "cursor.png")), "its files were copied in");
                Assert.True(File.Exists(Path.Combine(source, "content", "textures", "cursor.png")), "and the folder it came from is untouched");

                Assert.True(mods.Import(source) != null, "the same name twice is refused");
                Assert.Equal(1, mods.Packs.Count, "and does not make a second pack");
            }
        }

        // Zipping a folder puts everything one level down, inside a folder
        // named after the pack.
        public static void TestAFolderThatIsOnlyWrappingIsSteppedThrough()
        {
            using (World w = new World())
            {
                string source = Path.Combine(w.Root, "download");
                Put(Path.Combine(source, "CoolPack-main", "content", "textures", "cursor.png"), "cool");

                LooksMods mods = new LooksMods(w.Library);
                Assert.Equal(null, mods.Import(source, "Cool"), "it goes in");
                Assert.True(File.Exists(Path.Combine(w.Library, "Cool", "content", "textures", "cursor.png")), "with the wrapping folder left out");
            }
        }

        public static void TestAPackWithAProgramInItIsRefusedWhole()
        {
            using (World w = new World())
            {
                string source = Path.Combine(w.Root, "Dodgy");
                Put(Path.Combine(source, "content", "textures", "cursor.png"), "looks innocent");
                Put(Path.Combine(source, "content", "textures", "helper.exe"), "is not");

                LooksMods mods = new LooksMods(w.Library);
                string why = mods.Import(source);
                Assert.True(why != null, "refused");
                Assert.Contains("helper.exe", why, "and it says which file");
                Assert.Equal(0, mods.Packs.Count, "nothing joined the library");
                Assert.False(Directory.Exists(Path.Combine(w.Library, "Dodgy")), "not even the picture that was fine");
            }
        }

        public static void TestAPackWithFilesForAnyOtherFolderIsRefused()
        {
            using (World w = new World())
            {
                string source = Path.Combine(w.Root, "Flags");
                Put(Path.Combine(source, "content", "textures", "cursor.png"), "fine");
                Put(Path.Combine(source, "ClientSettings", "ClientAppSettings.json"), "{ \"DFIntTaskSchedulerTargetFps\": 9999 }");

                LooksMods mods = new LooksMods(w.Library);
                Assert.True(mods.Import(source) != null, "a pack that carries a flags file is refused");
                Assert.False(Directory.Exists(Path.Combine(w.Library, "Flags")), "and nothing of it is kept");

                string empty = Path.Combine(w.Root, "Empty");
                Directory.CreateDirectory(Path.Combine(empty, "content"));
                Assert.True(mods.Import(empty) != null, "a pack with no files is not a pack");
                Assert.True(mods.Import(Path.Combine(w.Root, "no-such-folder")) != null, "nor is a path with nothing at it");
                Assert.True(mods.Import(source, "bad|name") != null, "and a name the manifest could not hold is refused");
                Assert.Equal(0, mods.Packs.Count, "the library is still empty");
            }
        }

        public static void TestAZipIsImportedAsAPack()
        {
            using (World w = new World())
            {
                string zip = Path.Combine(w.Root, "Night Sky.zip");
                MakeZip(zip,
                    "Night Sky/content/textures/cursor.png", "night cursor",
                    "Night Sky/ExtraContent/places/sky.tex", "night sky");

                LooksMods mods = new LooksMods(w.Library);
                Assert.Equal(null, mods.Import(zip), "it goes in");
                Assert.Equal("Night Sky", mods.Packs[0].Name, "named after the zip");
                Assert.Equal("night cursor", Text(Path.Combine(w.Library, "Night Sky", "content", "textures", "cursor.png")), "unpacked, wrapping folder left out");
                Assert.Equal("night sky", Text(Path.Combine(w.Library, "Night Sky", "ExtraContent", "places", "sky.tex")), "every file of it");

                File.WriteAllText(Path.Combine(w.Root, "broken.zip"), "this is not a zip");
                Assert.True(mods.Import(Path.Combine(w.Root, "broken.zip")) != null, "a file that only claims to be a zip is refused");
                Assert.False(Directory.Exists(Path.Combine(w.Library, "broken")), "and leaves nothing behind");
            }
        }

        // The classic trick: a name inside the zip that climbs out of the
        // folder it is being unpacked into.
        public static void TestAZipThatUnpacksOutsideTheLibraryIsRefused()
        {
            using (World w = new World())
            {
                string climbing = Path.Combine(w.Root, "climb.zip");
                MakeZip(climbing,
                    "content/textures/cursor.png", "fine",
                    "content/../../../escaped.txt", "out");
                string rooted = Path.Combine(w.Root, "rooted.zip");
                MakeZip(rooted, "../escaped.txt", "out");
                string program = Path.Combine(w.Root, "program.zip");
                MakeZip(program, "content/textures/cursor.png", "fine", "content/run.bat", "echo hi");

                LooksMods mods = new LooksMods(w.Library);
                Assert.True(mods.Import(climbing) != null, "a path that climbs out is refused");
                Assert.True(mods.Import(rooted) != null, "so is one that starts by climbing");
                Assert.True(mods.Import(program) != null, "and a zip with a script in it");

                Assert.Equal(0, mods.Packs.Count, "nothing joined the library");
                Assert.Equal(0, Directory.GetFileSystemEntries(w.Library).Length, "and nothing was unpacked into it");
                Assert.Equal(0, Directory.GetFiles(w.Root, "escaped.txt", SearchOption.AllDirectories).Length, "or anywhere near it");
                Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(w.Root), "escaped.txt")), "or above it");
            }
        }

        // A link in a pack could point at any folder on the machine.
        public static void TestALinkInAPackIsRefused()
        {
            using (World w = new World())
            {
                string elsewhere = Path.Combine(w.Root, "elsewhere");
                Put(Path.Combine(elsewhere, "private.txt"), "not part of any pack");
                string source = Path.Combine(w.Root, "Linked");
                Put(Path.Combine(source, "content", "textures", "cursor.png"), "fine");
                string link = Path.Combine(source, "content", "linked");

                ProcessStartInfo make = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + elsewhere + "\"");
                make.UseShellExecute = false;
                make.CreateNoWindow = true;
                using (Process p = Process.Start(make)) p.WaitForExit();
                // Some machines do not let a link be made; nothing to prove there.
                if (!Directory.Exists(link)) return;

                try
                {
                    LooksMods mods = new LooksMods(w.Library);
                    string why = mods.Import(source);
                    Assert.True(why != null, "refused");
                    Assert.Contains("link", why, "and it says why");
                    Assert.False(Directory.Exists(Path.Combine(w.Library, "Linked")), "nothing was copied in");
                }
                finally { Directory.Delete(link); }
                Assert.True(File.Exists(Path.Combine(elsewhere, "private.txt")), "and what the link pointed at was never touched");
            }
        }

        // ---------- The library ----------

        public static void TestTheLibraryRemembersTheOrderAndWhichAreOn()
        {
            using (World w = new World())
            {
                w.AddPack("Alpha", "content\\a.png", "a");
                w.AddPack("Beta", "content\\b.png", "b");
                w.AddPack("Gamma", "content\\c.png", "c");

                LooksMods mods = new LooksMods(w.Library);
                Assert.Equal(3, mods.Packs.Count, "all three found");
                Assert.False(mods.AnyEnabled, "a pack nobody switched on is off");

                Assert.True(mods.SetEnabled("Gamma", true), "switched on");
                Assert.True(mods.Move("Gamma", 0), "and moved to the front");
                Assert.True(mods.SetEnabled("alpha", true), "names are found in any case");
                Assert.False(mods.SetEnabled("Nope", true), "a pack that is not there cannot be switched on");

                LooksMods again = new LooksMods(w.Library);
                Assert.Equal("Gamma", again.Packs[0].Name, "the order was kept");
                Assert.Equal("Alpha", again.Packs[1].Name, "all the way down");
                Assert.Equal("Beta", again.Packs[2].Name, "to the last");
                Assert.True(again.Packs[0].Enabled, "Gamma is still on");
                Assert.True(again.Packs[1].Enabled, "so is Alpha");
                Assert.False(again.Packs[2].Enabled, "and Beta is still off");
            }
        }

        public static void TestThePacksAreTheFoldersNotTheManifest()
        {
            using (World w = new World())
            {
                w.AddPack("Alpha", "content\\a.png", "a");
                w.AddPack("Beta", "content\\b.png", "b");
                w.Open("Alpha", "Beta");

                // One deleted and one dropped in, both by hand.
                Directory.Delete(Path.Combine(w.Library, "Alpha"), true);
                w.AddPack("Dropped", "content\\d.png", "d");

                LooksMods mods = new LooksMods(w.Library);
                Assert.Equal(2, mods.Packs.Count, "what is on disk is what there is");
                Assert.Equal("Beta", mods.Packs[0].Name, "the pack that is still there keeps its place");
                Assert.True(mods.Packs[0].Enabled, "and stays on");
                Assert.Equal("Dropped", mods.Packs[1].Name, "the new one goes last");
                Assert.False(mods.Packs[1].Enabled, "and is off until somebody switches it on");

                Assert.Equal(0, new LooksMods(Path.Combine(w.Root, "no-library-yet")).Packs.Count, "no library folder is just no packs");
            }
        }

        public static void TestTheManifestIsPlainLines()
        {
            List<LooksModPack> packs = LooksMods.Parse(new string[]
            {
                "pack=1|Night Sky", "pack=0|Pink Cursor v1.2", "", "something=else", "pack=1", "pack=1|..", "pack=1|a\\b"
            });
            Assert.Equal(2, packs.Count, "two real packs; the rest is skipped");
            Assert.Equal("Night Sky", packs[0].Name, "first in the file, first in the order");
            Assert.True(packs[0].Enabled, "1 is on");
            Assert.Equal("Pink Cursor v1.2", packs[1].Name, "names keep their spaces and dots");
            Assert.False(packs[1].Enabled, "0 is off");
        }

        // ---------- Which pack wins ----------

        public static void TestTheLaterPackWinsTheSameFile()
        {
            using (World w = new World())
            {
                w.AddPack("Early", "content\\textures\\cursor.png", "early cursor", "content\\sounds\\ouch.ogg", "early ouch");
                w.AddPack("Late", "Content\\Textures\\Cursor.png", "late cursor");
                LooksMods mods = w.Open("Early", "Late");

                List<LooksModPreview> preview = mods.Preview();
                Assert.Equal(2, preview.Count, "both packs are listed");
                Assert.Equal("Early", preview[0].Pack, "in order");
                Assert.Equal(2, preview[0].Files.Count, "with both of Early's files");
                foreach (LooksModFile f in preview[0].Files)
                {
                    bool cursor = f.Path.EndsWith("cursor.png", StringComparison.OrdinalIgnoreCase);
                    Assert.Equal(cursor ? "Late" : "Early", f.WonBy, "who wins " + f.Path);
                    Assert.Equal(!cursor, f.Wins, "whether Early's copy of " + f.Path + " is the one used");
                }
                Assert.True(preview[1].Files[0].Wins, "Late wins its one file, though it spells the folders differently");

                LooksModsResult result = mods.Apply(w.Versions, w.Backup);
                Assert.Equal(0, result.Failed.Count, "applied cleanly: " + Reasons(result));
                string v = w.Version("version-aaa111");
                Assert.Equal("late cursor", Text(Path.Combine(v, "content", "textures", "cursor.png")), "the later pack's cursor");
                Assert.Equal("early ouch", Text(Path.Combine(v, "content", "sounds", "ouch.ogg")), "the earlier pack's sound, which nobody else supplies");

                mods.Move("Late", 0);
                mods.Apply(w.Versions, w.Backup);
                Assert.Equal("early cursor", Text(Path.Combine(v, "content", "textures", "cursor.png")), "swap the order and the other one wins");
            }
        }

        public static void TestAPackThatIsOffChangesNothing()
        {
            using (World w = new World())
            {
                w.AddPack("Off", "content\\textures\\cursor.png", "should not appear");
                LooksMods mods = new LooksMods(w.Library);
                SortedDictionary<string, string> before = Snapshot(w.Versions);

                Assert.Equal(0, mods.Preview().Count, "nothing to preview");
                LooksModsResult result = mods.Apply(w.Versions, w.Backup);
                Assert.Equal(0, result.Replaced + result.Added, "nothing replaced");
                SameTree(before, w.Versions, "with every pack off");
                Assert.False(Directory.Exists(Path.Combine(w.Backup, "version-aaa111")), "and no backup folder made for nothing");
            }
        }

        // ---------- Apply ----------

        public static void TestApplyGoesIntoEveryInstalledVersion()
        {
            using (World w = new World())
            {
                w.AddVersion("version-bbb222");
                Directory.CreateDirectory(Path.Combine(w.Versions, "version-ccc333", "content"));   // no player in it
                w.AddPack("Pink", "content\\textures\\cursor.png", "pink", "content\\new\\deep\\extra.png", "extra");
                LooksMods mods = w.Open("Pink");

                LooksModsResult result = mods.Apply(w.Versions, w.Backup);
                Assert.Equal(0, result.Failed.Count, "applied cleanly: " + Reasons(result));
                Assert.Equal(2, result.Versions, "both installed versions");
                Assert.Equal(2, result.Replaced, "the cursor in each");
                Assert.Equal(2, result.Added, "the new file in each");
                Assert.Equal(2, result.BackedUp, "and each original saved");

                foreach (string name in new string[] { "version-aaa111", "version-bbb222" })
                {
                    Assert.Equal("pink", Text(Path.Combine(w.Version(name), "content", "textures", "cursor.png")), name + " has the pack's cursor");
                    Assert.Equal("extra", Text(Path.Combine(w.Version(name), "content", "new", "deep", "extra.png")), name + " has the added file");
                    Assert.Equal("roblox cursor " + name, Text(Path.Combine(w.Backup, name, "content", "textures", "cursor.png")), name + "'s own original is saved");
                    Assert.True(File.Exists(Path.Combine(w.Backup, name, "manifest.txt")), name + " has its record");
                    Assert.False(File.Exists(Path.Combine(w.Backup, name, "content", "new", "deep", "extra.png")), "a file Roblox never had has no original to save");
                }
                Assert.False(Directory.Exists(Path.Combine(w.Versions, "version-ccc333", "content", "textures")), "a folder with no player in it is not an install");
            }
        }

        public static void TestTheOriginalIsSavedOnceAcrossTwoApplies()
        {
            using (World w = new World())
            {
                w.AddPack("Pink", "content\\textures\\cursor.png", "pink");
                LooksMods mods = w.Open("Pink");
                string saved = Path.Combine(w.Backup, "version-aaa111", "content", "textures", "cursor.png");

                Assert.Equal(1, mods.Apply(w.Versions, w.Backup).BackedUp, "saved the first time");
                Assert.Equal("roblox cursor version-aaa111", Text(saved), "and it is Roblox's file");

                // The pack changes, and is applied over its own earlier copy.
                w.AddPack("Pink", "content\\textures\\cursor.png", "pinker");
                LooksModsResult second = mods.Apply(w.Versions, w.Backup);
                Assert.Equal(0, second.BackedUp, "nothing saved the second time");
                Assert.Equal(1, second.Replaced, "though the file was replaced again");
                Assert.Equal("pinker", Text(Path.Combine(w.Version("version-aaa111"), "content", "textures", "cursor.png")), "the newer pack file is in");
                Assert.Equal("roblox cursor version-aaa111", Text(saved), "and the saved original is still Roblox's, not the first pack file");
            }
        }

        // Losing the record must not cost the original as well.
        public static void TestASavedOriginalIsNotOverwrittenWhenTheRecordIsLost()
        {
            using (World w = new World())
            {
                w.AddPack("Pink", "content\\textures\\cursor.png", "pink");
                LooksMods mods = w.Open("Pink");
                SortedDictionary<string, string> before = Snapshot(w.Versions);
                mods.Apply(w.Versions, w.Backup);

                File.Delete(Path.Combine(w.Backup, "version-aaa111", "manifest.txt"));
                LooksModsResult again = mods.Apply(w.Versions, w.Backup);
                Assert.Equal(0, again.BackedUp, "the pack's file is not saved as if it were the original");
                Assert.Equal("roblox cursor version-aaa111",
                    Text(Path.Combine(w.Backup, "version-aaa111", "content", "textures", "cursor.png")), "the real original is still what is saved");

                // And with the record gone a second time, the saved file alone
                // is enough to put things back.
                File.Delete(Path.Combine(w.Backup, "version-aaa111", "manifest.txt"));
                LooksMods.Restore(w.Versions, w.Backup);
                SameTree(before, w.Versions, "restored from the saved files alone");
            }
        }

        // The library is an ordinary folder; what is in it at apply time is
        // what counts, not what was there at import.
        public static void TestAPackIsCheckedAgainWhenItIsApplied()
        {
            using (World w = new World())
            {
                w.AddPack("Tampered", "content\\textures\\cursor.png", "fine", "content\\textures\\payload.exe", "not fine");
                w.AddPack("Honest", "content\\sounds\\ouch.ogg", "honest ouch");
                LooksMods mods = w.Open("Tampered", "Honest");

                List<LooksModPreview> preview = mods.Preview();
                Assert.Equal(1, preview[0].Problems.Count, "the preview says what is wrong with it");
                Assert.Equal(0, preview[0].Files.Count, "and lists nothing it would replace");

                LooksModsResult result = mods.Apply(w.Versions, w.Backup);
                string v = w.Version("version-aaa111");
                Assert.False(File.Exists(Path.Combine(v, "content", "textures", "payload.exe")), "the program never reaches Roblox's folder");
                Assert.Equal("roblox cursor version-aaa111", Text(Path.Combine(v, "content", "textures", "cursor.png")), "nor does anything else from that pack");
                Assert.Equal("honest ouch", Text(Path.Combine(v, "content", "sounds", "ouch.ogg")), "the pack with nothing wrong still applies");
                Assert.Equal(1, result.Failed.Count, "and the pack left out is reported");
                Assert.Equal("Tampered", result.Failed[0].Where, "by name");
                Assert.Contains("payload.exe", result.Failed[0].Why, "with the file that did it");
            }
        }

        public static void TestSwitchingAPackOffAndApplyingAgainPutsItsFilesBack()
        {
            using (World w = new World())
            {
                w.AddPack("Pink", "content\\textures\\cursor.png", "pink", "content\\new\\extra.png", "extra");
                w.AddPack("Loud", "content\\sounds\\ouch.ogg", "loud");
                LooksMods mods = w.Open("Pink", "Loud");
                SortedDictionary<string, string> before = Snapshot(w.Versions);
                mods.Apply(w.Versions, w.Backup);

                mods.SetEnabled("Pink", false);
                LooksModsResult result = mods.Apply(w.Versions, w.Backup);
                string v = w.Version("version-aaa111");
                Assert.Equal(1, result.Restored, "its replaced file was put back");
                Assert.Equal(1, result.Removed, "its added file was taken out");
                Assert.Equal("roblox cursor version-aaa111", Text(Path.Combine(v, "content", "textures", "cursor.png")), "Roblox's cursor is back");
                Assert.False(Directory.Exists(Path.Combine(v, "content", "new")), "the folder made for the added file is gone");
                Assert.Equal("loud", Text(Path.Combine(v, "content", "sounds", "ouch.ogg")), "the pack still on is still applied");

                mods.SetEnabled("Loud", false);
                mods.Apply(w.Versions, w.Backup);
                SameTree(before, w.Versions, "with the last pack switched off");
                Assert.False(Directory.Exists(Path.Combine(w.Backup, "version-aaa111")), "and nothing is kept for a version with nothing changed");
            }
        }

        // ---------- Restore ----------

        public static void TestRestorePutsEveryByteBack()
        {
            using (World w = new World())
            {
                w.AddVersion("version-bbb222");
                w.AddPack("Pink",
                    "content\\textures\\cursor.png", "pink",
                    "ExtraContent\\places\\sky.tex", "pink sky",
                    "content\\new\\deep\\extra.png", "extra",
                    "content\\empty\\filled.png", "in a folder Roblox left empty");
                w.AddPack("Loud",
                    "content\\sounds\\ouch.ogg", "loud",
                    "PlatformContent\\pc\\fonts.dat", "loud fonts",
                    "content\\textures\\cursor.png", "loud cursor");
                LooksMods mods = w.Open("Pink", "Loud");
                SortedDictionary<string, string> before = Snapshot(w.Versions);

                LooksModsResult applied = mods.Apply(w.Versions, w.Backup);
                Assert.Equal(0, applied.Failed.Count, "applied cleanly: " + Reasons(applied));
                Assert.Equal(8, applied.Replaced, "four of Roblox's files in each version");
                Assert.Equal(4, applied.Added, "and two new ones in each");

                LooksModsResult restored = LooksMods.Restore(w.Versions, w.Backup);
                Assert.Equal(0, restored.Failed.Count, "restored cleanly: " + Reasons(restored));
                Assert.Equal(2, restored.Versions, "both versions");
                Assert.Equal(8, restored.Restored, "every original put back");
                Assert.Equal(4, restored.Removed, "every added file taken out");

                SameTree(before, w.Versions, "after restore");
                Assert.Equal(0, Directory.GetFileSystemEntries(w.Backup).Length, "and the record is cleared");
            }
        }

        public static void TestAddedFilesAndTheFoldersMadeForThemAreRemoved()
        {
            using (World w = new World())
            {
                w.AddPack("Extra", "content\\new\\deep\\extra.png", "extra", "content\\empty\\filled.png", "filled");
                LooksMods mods = w.Open("Extra");
                mods.Apply(w.Versions, w.Backup);
                string v = w.Version("version-aaa111");
                Assert.True(File.Exists(Path.Combine(v, "content", "new", "deep", "extra.png")), "added, folders and all");

                LooksMods.Restore(w.Versions, w.Backup);
                Assert.False(Directory.Exists(Path.Combine(v, "content", "new")), "the folders made for it are gone");
                Assert.False(File.Exists(Path.Combine(v, "content", "empty", "filled.png")), "the file put in Roblox's own folder is gone");
                Assert.True(Directory.Exists(Path.Combine(v, "content", "empty")), "but that folder was Roblox's, empty or not, and stays");
            }
        }

        // Somebody else's file in a folder we made is not ours to delete.
        public static void TestAFolderWeMadeIsKeptIfSomethingElseIsNowInIt()
        {
            using (World w = new World())
            {
                w.AddPack("Extra", "content\\new\\extra.png", "extra");
                LooksMods mods = w.Open("Extra");
                mods.Apply(w.Versions, w.Backup);
                string theirs = Path.Combine(w.Version("version-aaa111"), "content", "new", "theirs.png");
                File.WriteAllText(theirs, "theirs");

                LooksModsResult result = LooksMods.Restore(w.Versions, w.Backup);
                Assert.Equal(0, result.Failed.Count, "nothing to complain about");
                Assert.True(File.Exists(theirs), "their file is still there");
                Assert.False(File.Exists(Path.Combine(w.Version("version-aaa111"), "content", "new", "extra.png")), "ours is gone");
                Assert.False(Directory.Exists(Path.Combine(w.Backup, "version-aaa111")), "and the record is cleared all the same");
            }
        }

        public static void TestRestoreTwiceIsHarmless()
        {
            using (World w = new World())
            {
                w.AddPack("Pink", "content\\textures\\cursor.png", "pink", "content\\new\\extra.png", "extra");
                LooksMods mods = w.Open("Pink");
                SortedDictionary<string, string> before = Snapshot(w.Versions);
                mods.Apply(w.Versions, w.Backup);

                LooksMods.Restore(w.Versions, w.Backup);
                LooksModsResult second = LooksMods.Restore(w.Versions, w.Backup);
                Assert.Equal(0, second.Failed.Count, "the second time finds nothing wrong");
                Assert.Equal(0, second.Restored + second.Removed, "and nothing to do");
                SameTree(before, w.Versions, "after restoring twice");

                LooksModsResult never = LooksMods.Restore(w.Versions, Path.Combine(w.Root, "never-made"));
                Assert.Equal(0, never.Failed.Count, "restoring when nothing was ever applied is not an error either");
            }
        }

        public static void TestRestoreWorksAfterThePackHasBeenDeleted()
        {
            using (World w = new World())
            {
                w.AddPack("Pink", "content\\textures\\cursor.png", "pink", "content\\new\\extra.png", "extra");
                LooksMods mods = w.Open("Pink");
                SortedDictionary<string, string> before = Snapshot(w.Versions);
                mods.Apply(w.Versions, w.Backup);

                Assert.Equal(null, mods.Remove("Pink"), "the pack is deleted from the library");
                Assert.False(Directory.Exists(Path.Combine(w.Library, "Pink")), "folder and all");
                Assert.Equal("pink", Text(Path.Combine(w.Version("version-aaa111"), "content", "textures", "cursor.png")), "which changes nothing in Roblox by itself");

                LooksModsResult result = LooksMods.Restore(w.Versions, w.Backup);
                Assert.Equal(0, result.Failed.Count, "the undo needs nothing from the library: " + Reasons(result));
                SameTree(before, w.Versions, "after restore with the pack gone");
            }
        }

        public static void TestAVersionThatIsGoneIsSkippedAndItsBackupRemoved()
        {
            using (World w = new World())
            {
                w.AddVersion("version-bbb222");
                w.AddPack("Pink", "content\\textures\\cursor.png", "pink");
                LooksMods mods = w.Open("Pink");
                mods.Apply(w.Versions, w.Backup);

                // Roblox updated and removed the old version.
                Directory.Delete(w.Version("version-aaa111"), true);

                LooksModsResult result = LooksMods.Restore(w.Versions, w.Backup);
                Assert.Equal(0, result.Failed.Count, "a version that has gone is not an error: " + Reasons(result));
                Assert.Equal(1, result.Versions, "the one still installed was restored");
                Assert.False(Directory.Exists(w.Version("version-aaa111")), "the missing version was not brought back as a stray folder");
                Assert.False(Directory.Exists(Path.Combine(w.Backup, "version-aaa111")), "and its saved originals are cleared away");
                Assert.Equal("roblox cursor version-bbb222", Text(Path.Combine(w.Version("version-bbb222"), "content", "textures", "cursor.png")), "the other has its own cursor back");
            }
        }

        // A wrong folder, or a drive that is not plugged in, must not be read
        // as "every version has gone" - that would throw every original away.
        public static void TestTheOriginalsAreKeptWhenNoVersionsCanBeFound()
        {
            using (World w = new World())
            {
                w.AddPack("Pink", "content\\textures\\cursor.png", "pink");
                LooksMods mods = w.Open("Pink");
                SortedDictionary<string, string> before = Snapshot(w.Versions);
                mods.Apply(w.Versions, w.Backup);
                string saved = Path.Combine(w.Backup, "version-aaa111", "content", "textures", "cursor.png");

                LooksModsResult wrong = LooksMods.Restore(Path.Combine(w.Root, "not-the-versions-folder"), w.Backup);
                Assert.Equal(1, wrong.Failed.Count, "it says it could not find Roblox");
                Assert.True(File.Exists(saved), "and the saved original is still there");

                LooksModsResult empty = LooksMods.Restore(w.Root, w.Backup);
                Assert.Equal(1, empty.Failed.Count, "a folder with no versions in it is treated the same");
                Assert.True(File.Exists(saved), "with the original still safe");

                LooksModsResult applied = mods.Apply(Path.Combine(w.Root, "not-the-versions-folder"), w.Backup);
                Assert.Equal(1, applied.Failed.Count, "applying to the wrong folder says so too");
                Assert.True(File.Exists(saved), "and does not tidy the originals away either");

                LooksMods.Restore(w.Versions, w.Backup);
                SameTree(before, w.Versions, "restored once pointed at the right folder");
            }
        }

        // The record sits in a folder anybody can edit, and it is a list of
        // files to overwrite and delete.
        public static void TestARecordThatPointsOutsideRobloxIsIgnored()
        {
            using (World w = new World())
            {
                string victim = Path.Combine(w.Versions, "victim.txt");
                File.WriteAllText(victim, "nothing to do with any pack");
                string playerExe = Path.Combine(w.Version("version-aaa111"), "RobloxPlayerBeta.exe");

                string backup = Path.Combine(w.Backup, "version-aaa111");
                Directory.CreateDirectory(backup);
                File.WriteAllLines(Path.Combine(backup, "manifest.txt"), new string[]
                {
                    "added=..\\victim.txt",
                    "added=content\\..\\..\\victim.txt",
                    "added=RobloxPlayerBeta.exe",
                    "added=" + victim,
                    "dir=..",
                    "orig=..\\victim.txt"
                });
                File.WriteAllText(Path.Combine(backup, "RobloxPlayerBeta.exe"), "a planted replacement");

                LooksMods.Restore(w.Versions, w.Backup);
                Assert.True(File.Exists(victim), "a file outside the version folder is not deleted");
                Assert.Equal("nothing to do with any pack", Text(victim), "or overwritten");
                Assert.Equal("not really a program", Text(playerExe), "and nothing outside the content folders is put in place");
            }
        }

        // ---------- A running client ----------

        public static void TestAFileInUseIsReportedAndTheRestCarryOn()
        {
            using (World w = new World())
            {
                w.AddPack("Pink", "content\\textures\\cursor.png", "pink", "content\\sounds\\ouch.ogg", "pink ouch");
                LooksMods mods = w.Open("Pink");
                SortedDictionary<string, string> before = Snapshot(w.Versions);
                string v = w.Version("version-aaa111");
                string cursor = Path.Combine(v, "content", "textures", "cursor.png");

                LooksModsResult result;
                using (new FileStream(cursor, FileMode.Open, FileAccess.Read, FileShare.Read))
                    result = mods.Apply(w.Versions, w.Backup);

                Assert.Equal(1, result.Failed.Count, "the one file a client is holding is reported");
                Assert.Equal("version-aaa111\\content\\textures\\cursor.png", result.Failed[0].Where, "by version and path");
                Assert.Contains("in use", result.Failed[0].Why, "in words a person can act on");
                Assert.Equal(1, result.Replaced, "and the other file went in regardless");
                Assert.Equal("roblox cursor version-aaa111", Text(cursor), "the held file is still Roblox's");
                Assert.Equal("pink ouch", Text(Path.Combine(v, "content", "sounds", "ouch.ogg")), "the free one is the pack's");

                LooksMods.Restore(w.Versions, w.Backup);
                SameTree(before, w.Versions, "after restore");
            }
        }

        public static void TestAFileThatCannotBePutBackYetStaysOnTheList()
        {
            using (World w = new World())
            {
                w.AddPack("Pink", "content\\textures\\cursor.png", "pink", "content\\new\\extra.png", "extra");
                LooksMods mods = w.Open("Pink");
                SortedDictionary<string, string> before = Snapshot(w.Versions);
                mods.Apply(w.Versions, w.Backup);
                string v = w.Version("version-aaa111");
                string cursor = Path.Combine(v, "content", "textures", "cursor.png");
                string extra = Path.Combine(v, "content", "new", "extra.png");
                string saved = Path.Combine(w.Backup, "version-aaa111", "content", "textures", "cursor.png");

                LooksModsResult first;
                using (new FileStream(cursor, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (new FileStream(extra, FileMode.Open, FileAccess.Read, FileShare.Read))
                    first = LooksMods.Restore(w.Versions, w.Backup);

                Assert.Equal(2, first.Failed.Count, "both held files are reported");
                Assert.Contains("in use", first.Failed[0].Why, "with the reason");
                Assert.True(File.Exists(saved), "the original is still saved, not thrown away with the job half done");
                Assert.Equal("pink", Text(cursor), "the held file is still the pack's");

                LooksModsResult second = LooksMods.Restore(w.Versions, w.Backup);
                Assert.Equal(0, second.Failed.Count, "once the client has closed, a second go finishes it: " + Reasons(second));
                Assert.Equal(1, second.Restored, "the original goes back");
                Assert.Equal(1, second.Removed, "and the added file comes out");
                SameTree(before, w.Versions, "after the second restore");
                Assert.False(Directory.Exists(Path.Combine(w.Backup, "version-aaa111")), "and only then is the record cleared");
            }
        }

        // Windows will not overwrite or delete a read-only file, and copying
        // one makes the copy read-only too. Packs unpacked from some archives
        // arrive that way, and it must not strand them in Roblox's folder.
        public static void TestReadOnlyFilesDoNotBreakTheUndo()
        {
            using (World w = new World())
            {
                string source = Path.Combine(w.Root, "Stubborn");
                Put(Path.Combine(source, "content", "textures", "cursor.png"), "stubborn cursor");
                Put(Path.Combine(source, "content", "sounds", "ouch.ogg"), "stubborn ouch");
                Put(Path.Combine(source, "content", "new", "extra.png"), "stubborn extra");
                foreach (string f in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.ReadOnly);

                // A pack dropped in by hand keeps its read-only files too.
                w.AddPack("ByHand", "ExtraContent\\places\\sky.tex", "by hand sky");
                File.SetAttributes(Path.Combine(w.Library, "ByHand", "ExtraContent", "places", "sky.tex"), FileAttributes.ReadOnly);

                // And one of Roblox's own files is read-only to begin with.
                string v = w.Version("version-aaa111");
                string ouch = Path.Combine(v, "content", "sounds", "ouch.ogg");
                File.SetAttributes(ouch, FileAttributes.ReadOnly);
                SortedDictionary<string, string> before = Snapshot(w.Versions);

                LooksMods mods = new LooksMods(w.Library);
                Assert.Equal(null, mods.Import(source), "the pack goes in");
                mods.SetEnabled("ByHand", true);

                LooksModsResult applied = mods.Apply(w.Versions, w.Backup);
                Assert.Equal(0, applied.Failed.Count, "applied cleanly: " + Reasons(applied));
                Assert.Equal("stubborn ouch", Text(ouch), "Roblox's read-only file was replaced");
                Assert.Equal("by hand sky", Text(Path.Combine(v, "ExtraContent", "places", "sky.tex")), "and the read-only pack file went in");
                Assert.False(ReadOnly(Path.Combine(v, "content", "textures", "cursor.png")), "without leaving Roblox's folder full of read-only files");

                // Applied a second time over its own files.
                Assert.Equal(0, mods.Apply(w.Versions, w.Backup).Failed.Count, "and applies again over itself");

                LooksModsResult restored = LooksMods.Restore(w.Versions, w.Backup);
                Assert.Equal(0, restored.Failed.Count, "restored cleanly: " + Reasons(restored));
                SameTree(before, w.Versions, "after restore");
                Assert.True(ReadOnly(ouch), "Roblox's file is read-only again, as it was");
                Assert.Equal(0, Directory.GetFileSystemEntries(w.Backup).Length, "and nothing is left behind in the backup");

                Assert.Equal(null, mods.Remove("ByHand"), "a pack of read-only files can still be deleted");
                Assert.False(Directory.Exists(Path.Combine(w.Library, "ByHand")), "folder and all");
            }
        }

        // ---------- A Roblox update ----------

        public static void TestANewVersionFolderNeedsThePacksAppliedAgain()
        {
            using (World w = new World())
            {
                w.AddPack("Pink", "content\\textures\\cursor.png", "pink");
                LooksMods mods = new LooksMods(w.Library);
                Assert.False(mods.NeedsReapply(w.Versions, w.Backup), "with no pack on there is nothing to apply");

                // A tick is a wish, not a request: nothing goes into Roblox
                // by itself until Apply has been pressed once.
                mods.SetEnabled("Pink", true);
                Assert.False(mods.NeedsReapply(w.Versions, w.Backup), "a pack is ticked and has never been applied: that waits for Apply");
                mods.Apply(w.Versions, w.Backup);
                Assert.False(mods.NeedsReapply(w.Versions, w.Backup), "applied everywhere");

                // Roblox updates: a new folder, original files in it.
                string fresh = w.AddVersion("version-bbb222");
                Assert.True(mods.NeedsReapply(w.Versions, w.Backup), "the new version has not had the packs");
                Assert.Equal("roblox cursor version-bbb222", Text(Path.Combine(fresh, "content", "textures", "cursor.png")), "and indeed has Roblox's own cursor");

                LooksModsResult result = mods.Reapply(w.Versions, w.Backup);
                Assert.Equal(1, result.BackedUp, "only the new version's original needed saving");
                Assert.False(mods.NeedsReapply(w.Versions, w.Backup), "and now nothing is waiting");
                Assert.Equal("pink", Text(Path.Combine(fresh, "content", "textures", "cursor.png")), "the new version has the pack");

                Directory.CreateDirectory(Path.Combine(w.Versions, "version-ccc333"));
                Assert.False(mods.NeedsReapply(w.Versions, w.Backup), "a version folder with no player in it is not an install");
            }
        }

        public static void TestApplyClearsAwayWhatWasSavedForAVersionThatHasGone()
        {
            using (World w = new World())
            {
                w.AddPack("Pink", "content\\textures\\cursor.png", "pink");
                LooksMods mods = w.Open("Pink");
                mods.Apply(w.Versions, w.Backup);

                w.AddVersion("version-bbb222");
                Directory.Delete(w.Version("version-aaa111"), true);
                mods.Apply(w.Versions, w.Backup);

                Assert.False(Directory.Exists(Path.Combine(w.Backup, "version-aaa111")), "originals for a version that no longer exists are not kept for ever");
                Assert.True(File.Exists(Path.Combine(w.Backup, "version-bbb222", "manifest.txt")), "the new version has its record");
            }
        }

        // A pack that is on but supplies nothing must not leave the app
        // asking to reapply for ever.
        public static void TestAPackWithNothingUsableInItDoesNotAskForReapplyForEver()
        {
            using (World w = new World())
            {
                w.AddPack("Broken", "shaders\\thing.pack", "not somewhere a pack may write");
                LooksMods mods = w.Open("Broken");
                SortedDictionary<string, string> before = Snapshot(w.Versions);

                LooksModsResult result = mods.Apply(w.Versions, w.Backup);
                Assert.Equal(1, result.Failed.Count, "the pack is reported as left out");
                Assert.False(mods.NeedsReapply(w.Versions, w.Backup), "and having tried, there is nothing more to apply");
                SameTree(before, w.Versions, "with nothing usable to apply");

                LooksMods.Restore(w.Versions, w.Backup);
                Assert.Equal(0, Directory.GetFileSystemEntries(w.Backup).Length, "restore clears the empty record away");
            }
        }
    }
}
