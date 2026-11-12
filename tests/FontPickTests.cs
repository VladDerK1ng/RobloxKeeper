using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RobloxKeeper.Tests
{
    // The font picker ends by overwriting Roblox's own files, so two things
    // matter: nothing that is not a font Roblox can read gets that far, and
    // putting the font back leaves every file as Roblox shipped it. All of
    // it runs against a made-up install under the temp folder.
    static class FontPickTests
    {
        // The smallest file that is a font as far as its name goes.
        static byte[] Font(string fullName, string tag)
        {
            byte[] text = Encoding.BigEndianUnicode.GetBytes(fullName);
            List<byte> f = new List<byte>();
            if (tag == null) f.AddRange(new byte[] { 0, 1, 0, 0 });
            else f.AddRange(Encoding.ASCII.GetBytes(tag));
            f.AddRange(new byte[] { 0, 1, 0, 0, 0, 0, 0, 0 });                     // one table
            f.AddRange(Encoding.ASCII.GetBytes("name"));
            f.AddRange(new byte[] { 0, 0, 0, 0, 0, 0, 0, 28 });                     // checksum, offset
            int len = 6 + 12 + text.Length;
            f.AddRange(new byte[] { 0, 0, (byte)(len >> 8), (byte)len });
            f.AddRange(new byte[] { 0, 0, 0, 1, 0, 18 });                           // format, count, where the strings start
            f.AddRange(new byte[] { 0, 3, 0, 1, 4, 9, 0, 4, (byte)(text.Length >> 8), (byte)text.Length, 0, 0 });
            f.AddRange(text);
            return f.ToArray();
        }

        const string FAMILY_JSON =
            "{\n\t\"name\": \"Source Sans Pro\",\n\t\"faces\": [\n"
            + "\t\t{ \"name\": \"Light\", \"weight\": 300, \"style\": \"normal\", \"assetId\": \"rbxassetid://8075252781\" },\n"
            + "\t\t{ \"name\": \"Regular\", \"weight\": 400, \"style\": \"normal\", \"assetId\": \"rbxasset://fonts/SourceSansPro-Regular.ttf\" },\n"
            + "\t\t{ \"name\": \"Bold\", \"weight\": 700, \"style\": \"normal\", \"assetId\" : \"rbxasset://fonts/SourceSansPro-Bold.ttf\" }\n\t]\n}\n";

        class World : IDisposable
        {
            public readonly string Root, Version, FontFile;
            public readonly LooksPlaces At = new LooksPlaces();

            public World()
            {
                Root = Path.Combine(Path.GetTempPath(), "rk_fontpick_" + Guid.NewGuid().ToString("n"));
                At.Library = Path.Combine(Root, "mods");
                At.Versions = Path.Combine(Root, "Versions");
                At.Backup = Path.Combine(Root, "backup");
                Directory.CreateDirectory(At.Library);
                Version = Path.Combine(At.Versions, "version-aaa111");
                Put(Path.Combine(Version, "RobloxPlayerBeta.exe"), "not really a program");
                Put(Path.Combine(Version, "content", "fonts", "SourceSansPro-Regular.ttf"), "roblox's regular");
                Put(Path.Combine(Version, "content", "fonts", "families", "SourceSansPro.json"), FAMILY_JSON);
                Put(Path.Combine(Version, "content", "fonts", "families", "Arimo.json"), FAMILY_JSON.Replace("Source Sans Pro", "Arimo"));
                Put(Path.Combine(Version, "content", "fonts", "families", "NotoSansCJKFallback.json"), FAMILY_JSON.Replace("Source Sans Pro", "Noto"));
                FontFile = Path.Combine(Root, "Mine.ttf");
                File.WriteAllBytes(FontFile, Font("My Nice Font", null));
            }

            public static void Put(string path, string text)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text);
            }

            public string Read(params string[] parts)
            {
                return File.ReadAllText(Path.Combine(Version, Path.Combine(parts)));
            }

            public void Dispose()
            {
                try { Directory.Delete(Root, true); } catch { }
            }
        }

        public static void TestAFontsNameIsReadFromInsideTheFile()
        {
            string why, family;
            Assert.Equal("My Nice Font", FontPick.NameIn(Font("My Nice Font", null), out why), "a TrueType font");
            Assert.Equal("Outline One", FontPick.NameIn(Font("Outline One", "OTTO"), out family, out why), "an OpenType font");
        }

        public static void TestARealFontOnThisPcIsRead()
        {
            string arial = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
            if (!File.Exists(arial)) return;        // a PC without it has nothing to check here
            string why, family;
            Assert.Equal("Arial", FontPick.NameIn(File.ReadAllBytes(arial), out family, out why), "its full name");
            Assert.Equal("Arial", family, "and its family");
        }

        public static void TestWhatIsNotASingleFontIsRefusedWithAReason()
        {
            string why;
            Assert.True(FontPick.NameIn(Font("Many", "ttcf"), out why) == null, "a collection");
            Assert.Contains("collection", why, "says it is a collection");
            Assert.True(FontPick.NameIn(Font("Web", "wOFF"), out why) == null, "a web font");
            Assert.Contains("web font", why, "says so");
            Assert.True(FontPick.NameIn(Encoding.ASCII.GetBytes("MZ this is a program, not a font at all"), out why) == null, "a program");
            Assert.Contains("not a TrueType", why, "says it is not a font");
            Assert.True(FontPick.NameIn(new byte[3], out why) == null, "three bytes");
            Assert.True(FontPick.NameIn(null, out why) == null, "nothing");
        }

        public static void TestADamagedFontNeverThrows()
        {
            byte[] good = Font("My Nice Font", null);
            for (int cut = 0; cut < good.Length; cut++)
            {
                byte[] part = new byte[cut];
                Array.Copy(good, part, cut);
                string why;
                FontPick.NameIn(part, out why);         // any answer will do; an exception will not
            }
            byte[] wild = (byte[])good.Clone();
            wild[20] = 0xFF; wild[21] = 0xFF; wild[22] = 0xFF; wild[23] = 0xFF;   // the name table "starts" far past the end
            string reason;
            Assert.True(FontPick.NameIn(wild, out reason) == null, "a table that points outside the file");
        }

        public static void TestInstalledFontsAreListedByNameWithTheirFiles()
        {
            List<KeyValuePair<string, string>> reg = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Zebra (TrueType)", "zebra.ttf"),
                new KeyValuePair<string, string>("Arial Bold (TrueType)", "arialbd.ttf"),
                new KeyValuePair<string, string>("Cambria & Cambria Math (TrueType)", "cambria.ttc"),
                new KeyValuePair<string, string>("Mine (OpenType)", @"C:\Users\me\AppData\Local\Microsoft\Windows\Fonts\mine.otf"),
                new KeyValuePair<string, string>("Old Bitmap", "oldbitmap.fon"),
                new KeyValuePair<string, string>("", "nameless.ttf"),
                new KeyValuePair<string, string>("Broken (TrueType)", "")
            };
            List<FontChoice> list = FontPick.FromRegistry(reg, @"C:\Windows\Fonts");
            Assert.Equal(3, list.Count, "only single TrueType and OpenType files");
            Assert.Equal("Arial Bold", list[0].Name, "sorted by name, the (TrueType) dropped");
            Assert.Equal(@"C:\Windows\Fonts\arialbd.ttf", list[0].File, "a bare file name is in the Fonts folder");
            Assert.Equal(@"C:\Users\me\AppData\Local\Microsoft\Windows\Fonts\mine.otf", list[1].File, "a full path is kept");
            Assert.Equal("Zebra", list[2].Name, "last");
        }

        public static void TestEveryWeightIsPointedAtTheNewFontAndNothingElseChanges()
        {
            string after = FontPick.Rewrite(FAMILY_JSON, "CustomFont.ttf");
            Assert.False(after.Contains("rbxassetid://"), "no weight still comes from online");
            Assert.False(after.Contains("SourceSansPro-Regular.ttf"), "nor from Roblox's own file");
            Assert.Equal(3, after.Split(new string[] { "rbxasset://fonts/CustomFont.ttf" }, StringSplitOptions.None).Length - 1, "all three weights");
            Assert.Contains("\"name\": \"Source Sans Pro\"", after, "the family keeps its name");
            Assert.Contains("\"weight\": 700", after, "and its weights");
            Assert.Equal(after, FontPick.Rewrite(after, "CustomFont.ttf"), "doing it twice changes nothing more");
        }

        public static void TestThePackHoldsTheFontAndEveryFamilyButTheFallback()
        {
            using (World w = new World())
            {
                string pack = Path.Combine(w.Root, "pack");
                int families;
                Assert.True(FontPick.Build(w.FontFile, w.Version, pack, out families) == null, "it is made");
                Assert.Equal(2, families, "two families rewritten");
                Assert.True(File.Exists(Path.Combine(pack, "content", "fonts", "CustomFont.ttf")), "the font is in it");
                Assert.True(File.Exists(Path.Combine(pack, "content", "fonts", "families", "Arimo.json")), "a family");
                Assert.False(File.Exists(Path.Combine(pack, "content", "fonts", "families", "NotoSansCJKFallback.json")), "the fallback for other scripts is left alone");
                foreach (string f in Directory.GetFiles(pack, "*", SearchOption.AllDirectories))
                    Assert.True(LooksMods.WhyNot(f.Substring(pack.Length + 1)) == null, "the pack checker accepts " + Path.GetFileName(f));
            }
        }

        public static void TestAFileThatIsNotAFontNeverBecomesAPack()
        {
            using (World w = new World())
            {
                string bad = Path.Combine(w.Root, "virus.ttf");
                File.WriteAllText(bad, "MZ definitely not a font");
                LooksMods mods = new LooksMods(w.At.Library);
                string why = FontPick.Use(mods, bad, w.At.Versions);
                Assert.Contains("not a TrueType", why, "refused, with the reason");
                Assert.True(mods.Find(FontPick.PACK) == null, "nothing in the library");
                Assert.Contains("wasn't applied", FontPickDialog.ApplyFont(w.At, bad), "and the window says so");
                Assert.Contains("SourceSansPro-Regular.ttf", w.Read("content", "fonts", "families", "SourceSansPro.json"), "Roblox untouched");
            }
        }

        public static void TestApplyingAFontPointsRobloxAtItAndKeepsTheOriginals()
        {
            using (World w = new World())
            {
                string said = FontPickDialog.ApplyFont(w.At, w.FontFile);
                Assert.False(said.StartsWith("!"), "applied: " + said);
                Assert.Contains("CustomFont.ttf", w.Read("content", "fonts", "families", "SourceSansPro.json"), "the family points at the new font");
                Assert.False(w.Read("content", "fonts", "families", "SourceSansPro.json").Contains("rbxassetid"), "every weight of it");
                Assert.True(File.Exists(Path.Combine(w.Version, "content", "fonts", "CustomFont.ttf")), "the font is in Roblox");
                Assert.Contains("rbxassetid", w.Read("content", "fonts", "families", "NotoSansCJKFallback.json"), "the fallback is as it was");
                string kept = Path.Combine(w.At.Backup, "version-aaa111", "content", "fonts", "families", "SourceSansPro.json");
                Assert.Equal(FAMILY_JSON, File.ReadAllText(kept), "Roblox's own file is saved first");
            }
        }

        public static void TestPuttingTheFontBackLeavesRobloxExactlyAsItWas()
        {
            using (World w = new World())
            {
                FontPickDialog.ApplyFont(w.At, w.FontFile);
                string said = FontPickDialog.RemoveFont(w.At);
                Assert.False(said.StartsWith("!"), "put back: " + said);
                Assert.Equal(FAMILY_JSON, w.Read("content", "fonts", "families", "SourceSansPro.json"), "the family file is Roblox's again");
                Assert.False(File.Exists(Path.Combine(w.Version, "content", "fonts", "CustomFont.ttf")), "the added font is gone");
                Assert.Equal("roblox's regular", w.Read("content", "fonts", "SourceSansPro-Regular.ttf"), "Roblox's own font was never touched");
                Assert.Contains("already", FontPickDialog.RemoveFont(w.At), "asked again, there is nothing to put back");
            }
        }

        public static void TestChoosingAnotherFontReplacesTheFirst()
        {
            using (World w = new World())
            {
                FontPickDialog.ApplyFont(w.At, w.FontFile);
                string second = Path.Combine(w.Root, "Second.otf");
                File.WriteAllBytes(second, Font("Second Font", "OTTO"));
                string said = FontPickDialog.ApplyFont(w.At, second);
                Assert.False(said.StartsWith("!"), "applied: " + said);
                LooksMods mods = new LooksMods(w.At.Library);
                int fontPacks = 0;
                foreach (LooksModPack p in mods.Packs) if (p.Name == FontPick.PACK) fontPacks++;
                Assert.Equal(1, fontPacks, "one font pack, not two");
                Assert.Contains("CustomFont.otf", w.Read("content", "fonts", "families", "SourceSansPro.json"), "Roblox points at the second");
                Assert.False(File.Exists(Path.Combine(w.Version, "content", "fonts", "CustomFont.ttf")), "the first font's file was taken out");
                FontPickDialog.RemoveFont(w.At);
                Assert.Equal(FAMILY_JSON, w.Read("content", "fonts", "families", "SourceSansPro.json"), "and the way back still ends at Roblox's own");
            }
        }

        public static void TestOtherPacksStayWhenTheFontIsPutBack()
        {
            using (World w = new World())
            {
                World.Put(Path.Combine(w.Version, "content", "textures", "cursor.png"), "roblox cursor");
                string pack = Path.Combine(w.Root, "Pointer");
                World.Put(Path.Combine(pack, "content", "textures", "cursor.png"), "my cursor");
                LooksMods mods = new LooksMods(w.At.Library);
                Assert.True(mods.Import(pack, "Pointer") == null, "another pack is in the library");
                mods.Save();
                FontPickDialog.ApplyFont(w.At, w.FontFile);
                Assert.Equal("my cursor", w.Read("content", "textures", "cursor.png"), "it was applied along with the font");
                FontPickDialog.RemoveFont(w.At);
                Assert.Equal("my cursor", w.Read("content", "textures", "cursor.png"), "and is still there after the font is put back");
                Assert.Equal(FAMILY_JSON, w.Read("content", "fonts", "families", "SourceSansPro.json"), "while the fonts are Roblox's again");
            }
        }

        // Seen live: the font window applied a font while the Looks page behind
        // it still held the pack list from before, and the page's next save
        // wrote that older list back - the font pack's line gone from it. Read
        // again, the library finds the pack's folder and lists it unticked,
        // while the font is still in Roblox.
        public static void TestAFontDroppedFromTheListCanStillBePutBackAndReplaced()
        {
            using (World w = new World())
            {
                LooksMods page = new LooksMods(w.At.Library);       // the list as the page read it
                FontPickDialog.ApplyFont(w.At, w.FontFile);
                page.Save();                                         // ...written back over the newer one
                LooksModPack left = new LooksMods(w.At.Library).Find(FontPick.PACK);
                Assert.True(left != null && !left.Enabled, "the font pack is found again by its folder, unticked");
                Assert.True(FontPick.InRoblox(w.At.Versions), "and the font is still in Roblox");

                string second = Path.Combine(w.Root, "Second.otf");
                File.WriteAllBytes(second, Font("Second Font", "OTTO"));
                string said = FontPickDialog.ApplyFont(w.At, second);
                Assert.False(said.StartsWith("!"), "another font can still be applied: " + said);
                Assert.Contains("CustomFont.otf", w.Read("content", "fonts", "families", "SourceSansPro.json"), "and is the one in Roblox");

                page.Save();                                         // and the same thing happens again
                said = FontPickDialog.RemoveFont(w.At);
                Assert.False(said.StartsWith("!") || said.Contains("already"), "it can still be put back: " + said);
                Assert.Equal(FAMILY_JSON, w.Read("content", "fonts", "families", "SourceSansPro.json"), "Roblox's own file is back");
                Assert.False(FontPick.InRoblox(w.At.Versions), "and the font is out of Roblox");
                Assert.False(Directory.Exists(Path.Combine(w.At.Library, FontPick.PACK)), "nothing is left in the library");
            }
        }

        public static void TestWithNoRobloxInstalledItSaysSo()
        {
            using (World w = new World())
            {
                Directory.Delete(w.At.Versions, true);
                Assert.Contains("isn't installed", FontPick.Use(new LooksMods(w.At.Library), w.FontFile, w.At.Versions), "nothing to change");
            }
        }

        public static void TestTheWindowTakesOnlyAFontItCanRead()
        {
            using (World w = new World())
            using (FontPickDialog d = new FontPickDialog(null, w.At))
            {
                string bad = Path.Combine(w.Root, "notes.ttf");
                File.WriteAllText(bad, "just some notes");
                d.Choose(bad);
                Assert.True(d.ChosenFile == null, "not chosen");
                Assert.Contains("can't be used", d.Says, "and it says why");
                d.Apply();
                Assert.Contains("Click a font first", d.Says, "Apply has nothing to apply");
            }
        }
    }
}
