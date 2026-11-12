using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RobloxKeeper.Tests
{
    // Around the scripts: the folder they are kept in, a macro turned into
    // one, sharing one as a .rkscript file, and keeping their keys apart
    // from every other key the app answers to.
    static class ScriptToolTests
    {
        // ---------- the store ----------

        public static void TestNamesAreFileNamesThatStayInTheFolder()
        {
            Assert.Equal(null, ScriptStore.NameProblem("Farm loop"), "an ordinary name");
            Assert.Equal(null, ScriptStore.NameProblem("Farm v2.1"), "dots inside are fine");
            foreach (string bad in new string[] { "", " ", "a/b", "a\\b", "..", "x.", ".x", " x", "x ", "CON", "nul.txt", "a:b", "a*b", "a?b", "a\"b", "a<b", "a|b", new string('x', 61) })
                Assert.NotEqual(null, ScriptStore.NameProblem(bad), "refused: [" + bad + "]");
        }

        public static void TestCreateListRenameAndRemoveTakeTheirFilesAlong()
        {
            string dir = ScriptRig.TempDir();
            try
            {
                ScriptStore s = new ScriptStore(dir);
                Assert.Equal(null, s.Create("b script", "press('e')"), "made");
                Assert.Equal(null, s.Create("A script", ""), "and another");
                Assert.NotEqual(null, s.Create("B SCRIPT", ""), "a name taken in another case is taken");
                Assert.Equal("A script,b script", string.Join(",", s.Names().ToArray()), "listed in order, whatever the case");

                ScriptSettings set = new ScriptSettings();
                set.PlaysOn = "Vlad"; set.Key = 0x75; set.SummaryToDiscord = true;
                Assert.True(s.SaveSettings("b script", set), "settings kept");
                Dictionary<string, object> mem = new Dictionary<string, object>();
                mem["n"] = 3.0;
                Assert.True(s.SaveMemory("b script", mem), "memory kept");

                Assert.Equal(null, s.Rename("b script", "Farm"), "renamed");
                Assert.Equal("press('e')", s.Load("Farm"), "the code went with it");
                ScriptSettings back = new ScriptStore(dir).Settings("Farm");
                Assert.Equal("Vlad", back.PlaysOn, "so did its settings");
                Assert.Equal((byte)0x75, back.Key, "and its key");
                Assert.True(back.SummaryToDiscord, "and the Discord choice");
                Assert.Equal(3.0, s.Memory("Farm")["n"], "and what it remembered");
                Assert.Equal("Farm", s.OnKey(0x75), "its key is known by the new name");
                Assert.Equal(null, s.Load("b script"), "nothing left under the old name");

                Assert.Equal(null, s.Rename("Farm", "farm"), "only the case changing is a rename too");
                Assert.Equal("A script,farm", string.Join(",", s.Names().ToArray()), "and shows as such");

                Assert.True(s.Remove("farm"), "removed");
                Assert.Equal(1, Directory.GetFiles(dir).Length, "its settings and memory went too");
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void TestSettingsReadBackAndAnythingOddIsTheDefault()
        {
            ScriptSettings s = ScriptSettings.FromJson("{\"playsOn\":\"screen\",\"key\":117,\"discord\":true}");
            Assert.True(s.OnScreen, "the screen");
            Assert.Equal((byte)117, s.Key, "the key");
            Assert.True(s.SummaryToDiscord, "discord");
            ScriptSettings d = ScriptSettings.FromJson("not json {");
            Assert.True(d.OnFront, "the Roblox window in front, by default");
            Assert.Equal((byte)0, d.Key, "no key");
            Assert.Equal(null, d.Account, "no account");
            Assert.Equal("Vlad", ScriptSettings.FromJson(new ScriptSettings { PlaysOn = "Vlad" }.ToJson()).Account, "an account, written and read");
        }

        // ---------- keys kept apart ----------

        static KeyOwners Owners()
        {
            KeyOwners k = new KeyOwners();
            k.StopKey = 0x79;                                           // F10
            k.ClickerKey = 0x76;                                        // F7
            k.ShotKey = 0x7B;                                           // F12
            k.RuleKeys.Add(new KeyValuePair<string, byte>("Sell", 0x7A));       // F11
            k.Macros.Add(new KeyValuePair<string, byte>("Jump", 0x74));         // F5
            k.Scripts.Add(new KeyValuePair<string, byte>("Farm", 0x75));        // F6
            return k;
        }

        public static void TestEverythingElseRefusesAScriptsKey()
        {
            KeyOwners k = Owners();
            foreach (string use in new string[] { KeyUse.STOP, KeyUse.CLICKER, KeyUse.SHOT, KeyUse.RULE, KeyUse.MACRO, KeyUse.EXTRAS })
                Assert.Equal("F6 starts the script Farm. Choose another.", KeyUse.Problem(0x75, use, null, k), "refused for " + use);
            Assert.NotEqual(null, KeyUse.Problem(0x75, KeyUse.MACRO, "Farm", k), "a macro called the same as the script is still another job");
        }

        public static void TestAScriptRefusesEveryOtherKey()
        {
            KeyOwners k = Owners();
            Assert.Contains("stops macros", KeyUse.Problem(0x79, KeyUse.SCRIPT, "Farm", k), "the stop key");
            Assert.Contains("autoclicker", KeyUse.Problem(0x76, KeyUse.SCRIPT, "Farm", k), "the autoclicker's");
            Assert.Contains("screenshots", KeyUse.Problem(0x7B, KeyUse.SCRIPT, "Farm", k), "the screenshot key");
            Assert.Contains("rule Sell", KeyUse.Problem(0x7A, KeyUse.SCRIPT, "Farm", k), "a rule's");
            Assert.Contains("macro Jump", KeyUse.Problem(0x74, KeyUse.SCRIPT, "Farm", k), "a macro's");
            Assert.Contains("ends a recording", KeyUse.Problem(0x77, KeyUse.SCRIPT, "Farm", k), "F8");
            Assert.Equal("F6 starts the script Farm. Choose another.", KeyUse.Problem(0x75, KeyUse.SCRIPT, "Other", k), "another script's");
            Assert.Equal(null, KeyUse.Problem(0x75, KeyUse.SCRIPT, "farm", k), "its own key is no clash");
            Assert.Equal(null, KeyUse.Problem(0x45, KeyUse.SCRIPT, "Farm", k), "a free key is free");
        }

        // ---------- a macro as a script ----------

        static Macro Of(params MacroStep[] steps)
        {
            Macro m = new Macro();
            m.Name = "Farm";
            m.Steps.AddRange(steps);
            return m;
        }

        public static void TestEachStepIsOneReadableLine()
        {
            MacroStep chat = MacroStep.Type("gg \"all\"");
            chat.InChat = true;
            MacroStep pic = MacroStep.ClickPicture("Buy button-20261110-213000.png", null, 3000);
            pic.StopIfMissing = true;
            MacroStep gone = MacroStep.WaitPicture("bag.png", null, 5000, true);
            MacroStep until = MacroStep.Repeat(RepeatMode.UntilSeen, 0);
            until.Picture = "full.png";
            MacroStep scrollHere = MacroStep.Scroll(0, 0, -2);
            scrollHere.AtPointer = true;
            string lua = ScriptConvert.ToLua(Of(
                MacroStep.Key(0x45, 50), MacroStep.Key(0x57, 2000), MacroStep.KeyDown(0xA0), MacroStep.KeyUp(0xA0),
                MacroStep.Wait(500), MacroStep.Click(0.5, 0.8, false), MacroStep.ClickHere(true), MacroStep.Click(0.123456, 0.5, true),
                MacroStep.Type("hi"), chat, pic, gone,
                MacroStep.Move(0.25, 0.75), MacroStep.Drag(0.1, 0.2, 0.3, 0.4, 1500, false), MacroStep.Scroll(0.5, 0.5, 3), scrollHere,
                MacroStep.Repeat(RepeatMode.Times, 3), MacroStep.Repeat(RepeatMode.Forever, 0), MacroStep.Key(0x20, 50),
                MacroStep.EndRepeat(), MacroStep.EndRepeat(), until, MacroStep.Wait(1000), MacroStep.EndRepeat()));
            string want =
                "-- Made from the macro \"Farm\".\r\n" +
                "press(\"e\")\r\n" +
                "press(\"w\", 2)\r\n" +
                "keydown(\"lshift\")\r\n" +
                "keyup(\"lshift\")\r\n" +
                "wait(0.5)\r\n" +
                "click(50, 80)\r\n" +
                "click(\"right\")\r\n" +
                "click(12.3456, 50, \"right\")\r\n" +
                "type(\"hi\")\r\n" +
                "chat(\"gg \\\"all\\\"\")\r\n" +
                "if not clickpicture(\"Buy button\", 3) then stop() end\r\n" +
                "waitgone(\"bag\", 5)\r\n" +
                "move(25, 75)\r\n" +
                "drag(10, 20, 30, 40, 1.5)\r\n" +
                "scroll(3, 50, 50)\r\n" +
                "scroll(-2)\r\n" +
                "for i = 1, 3 do\r\n" +
                "    while true do\r\n" +
                "        press(\"space\")\r\n" +
                "    end\r\n" +
                "end\r\n" +
                "while not find(\"full\") do\r\n" +
                "    wait(1)\r\n" +
                "end\r\n";
            Assert.Equal(want, lua, "the whole script");
        }

        public static void TestAnAnywhereMacroPlaysOnTheScreenAndARepeatWithNoEndIsClosed()
        {
            Macro m = Of(MacroStep.Repeat(RepeatMode.Forever, 0), MacroStep.Key(0x45, 50));
            m.Anywhere = true;
            m.Times = 3;
            string lua = ScriptConvert.ToLua(m);
            Assert.Contains("use(\"screen\")\r\nwhile true do\r\n    press(\"e\")\r\nend\r\n", lua, "on the screen, closed");
            Assert.Contains("3 times", lua, "and its own Repeat is said");
        }

        public static void TestEveryKeyReadsBackAsItself()
        {
            for (int vk = 1; vk <= 254; vk++)
                Assert.Equal((byte)vk, ScriptKeys.Parse(ScriptKeys.Name((byte)vk)), "key " + vk + " (" + ScriptKeys.Name((byte)vk) + ")");
        }

        public static void TestQuotedTextReadsBackExactly()
        {
            string[] texts = { "plain", "a \"quote\"", "back\\slash", "line\nbreak", "tab\there", "\u0001" + "2 after a control", "ünïcødé", "" };
            foreach (string t in texts)
            {
                FakeOutlet o = new FakeOutlet();
                ScriptRig.Run("print(" + ScriptConvert.Quote(t) + ")", new FakeDesk(), o);
                Assert.Equal(t, o.All, "read back: " + t);
            }
        }

        // A pretend client for the macro itself, the same size as the
        // script's pretend window, sharing its looks.
        class MacroWorld : IMacroWorld
        {
            public readonly List<string> Said = new List<string>();
            public long Now;
            public int StopAfter = int.MaxValue;
            public Func<string, int, bool> Seen = delegate { return true; };
            public Point SeenAt = new Point(333, 222);
            int looks;
            public Rectangle Client { get { return new Rectangle(100, 50, 801, 601); } }
            public bool InFront() { return true; }
            public void Send(InputAction a) { Said.Add(FakeDesk.Describe(a)); }
            public void Sleep(int ms) { Now += ms; }
            public long NowMs { get { return Now; } }
            public bool StopAsked { get { return Said.Count >= StopAfter; } }
            public bool GateWanted { get { return false; } }
            public bool OthersWaiting { get { return false; } }
            public PictureLook Look(MacroStep s)
            {
                PictureLook l = new PictureLook();
                l.Found = Seen(s.Picture, looks++);
                l.At = SeenAt;
                return l;
            }
        }

        // The script made from a macro sends what the macro itself sends.
        static void SameInput(Macro m, int upTo, Func<string, int, bool> seen)
        {
            MacroWorld mw = new MacroWorld();
            mw.StopAfter = upTo;
            mw.Seen = seen;
            new MacroRunner(m).Run(mw);
            List<string> macroSaid = mw.Said.GetRange(0, Math.Min(upTo, mw.Said.Count));

            FakeDesk d = new FakeDesk();
            d.SeenAt = mw.SeenAt;
            d.Seen = seen;
            d.Pictures["egg"] = "egg.png";
            d.Pictures["full"] = "full.png";
            LuaRun run = new LuaRun(m.Name, ScriptConvert.ToLua(m), d, ScriptTarget.Of(d.Windows[0]), new FakeOutlet(), null);
            d.Napped = delegate { if (d.Sent.Count >= upTo) run.Stop(); };
            ScriptResult r = run.Run();
            Assert.Equal(null, r.Problem, m.Name + " ran as a script");
            List<string> scriptSaid = d.Said().GetRange(0, Math.Min(upTo, d.Sent.Count));
            Assert.Equal(string.Join(" | ", macroSaid.ToArray()), string.Join(" | ", scriptSaid.ToArray()), m.Name + ": the same input");
            Assert.True(macroSaid.Count > 0, m.Name + " sent something");
        }

        public static void TestTheExampleMacrosAsScriptsSendTheSameInput()
        {
            for (int i = 0; i < MacroForm.ExampleNames.Length; i++)
            {
                Macro m = MacroForm.Example(i);
                foreach (MacroStep s in m.Steps) if (s.UsesPicture && s.Picture == null) s.Picture = "egg.png";
                SameInput(m, 12, delegate { return true; });
            }
        }

        public static void TestAMacroOfEveryKindOfStepAsAScriptSendsTheSameInput()
        {
            MacroStep chat = MacroStep.Type("gg");
            chat.InChat = true;
            MacroStep until = MacroStep.Repeat(RepeatMode.UntilSeen, 0);
            until.Picture = "full.png";
            MacroStep scrollHere = MacroStep.Scroll(0, 0, -2);
            scrollHere.AtPointer = true;
            Macro m = Of(
                MacroStep.Key(0x45, 50), MacroStep.Key(0x57, 300), MacroStep.KeyDown(0xA0), MacroStep.Click(0.5, 0.8, false),
                MacroStep.KeyUp(0xA0), MacroStep.ClickHere(true), MacroStep.Click(0.123456, 0.987654, true),
                MacroStep.Type("hi"), chat, MacroStep.ClickPicture("egg.png", null, 1000),
                MacroStep.Move(0.25, 0.75), MacroStep.Drag(0.1, 0.2, 0.3, 0.4, 300, true), MacroStep.Scroll(0.5, 0.5, 3), scrollHere,
                MacroStep.Repeat(RepeatMode.Times, 2), MacroStep.Repeat(RepeatMode.Times, 2), MacroStep.Key(0x20, 50),
                MacroStep.EndRepeat(), MacroStep.EndRepeat(),
                until, MacroStep.Key(0x46, 50), MacroStep.EndRepeat());
            SameInput(m, 1000, delegate(string pic, int looks) { return pic == "egg.png" || looks >= 3; });
        }

        // ---------- the quotes in a script ----------

        public static void TestQuotedTextIsFoundAndCommentsAreNot()
        {
            string code = "clickpicture(\"egg\") -- not \"this\"\nfind('bag \\'s')\n--[[ \"nor this\" ]]\nlocal t = [[long \"one\"]] print(\"a\\\"b\")";
            List<string> found = new List<string>();
            foreach (ScriptLiterals.Literal l in ScriptLiterals.All(code)) found.Add(l.Value);
            Assert.Equal("egg|bag 's|long \"one\"|a\"b", string.Join("|", found.ToArray()), "strings in either quote, long ones, escapes; no comments");
            Dictionary<string, string> to = new Dictionary<string, string>();
            to["egg"] = "egg 2";
            to["bag 's"] = "bag 2";
            Assert.Equal("clickpicture(\"egg 2\") -- not \"this\"\nfind(\"bag 2\")\n--[[ \"nor this\" ]]\nlocal t = [[long \"one\"]] print(\"a\\\"b\")",
                         ScriptLiterals.Rename(code, to), "renamed where it is named, and nowhere else");
        }

        // ---------- sharing ----------

        static byte[] Png(Color c)
        {
            using (Bitmap b = new Bitmap(4, 4))
            {
                using (Graphics g = Graphics.FromImage(b)) g.Clear(c);
                using (MemoryStream ms = new MemoryStream()) { b.Save(ms, System.Drawing.Imaging.ImageFormat.Png); return ms.ToArray(); }
            }
        }

        const string EGG = "egg-20261112-120000.png";
        const string EGG_MASK = "egg-20261112-120000-mask-20261112-120500.png";

        // A script on one PC that names a picture with a painted-out copy.
        static string Shared(string root, out string pics)
        {
            string scripts = Path.Combine(root, "a-scripts");
            pics = Path.Combine(root, "a-pictures");
            Directory.CreateDirectory(pics);
            File.WriteAllBytes(Path.Combine(pics, EGG), Png(Color.Red));
            File.WriteAllBytes(Path.Combine(pics, EGG_MASK), Png(Color.Blue));
            ScriptStore a = new ScriptStore(scripts);
            a.Create("Farm", "clickpicture(\"egg\") waitfor('egg', 2) print(\"hello\")");
            ScriptSettings s = new ScriptSettings();
            s.PlaysOn = ScriptSettings.SCREEN; s.Key = 0x75; s.SummaryToDiscord = true;
            a.SaveSettings("Farm", s);
            string zip = Path.Combine(root, "farm" + ScriptPack.EXT);
            Assert.Equal(null, ScriptPack.Export(zip, a, "Farm", pics, PictureShelf.For(null, null, pics)), "exported");
            return zip;
        }

        public static void TestAScriptAndItsPicturesGoAcrossInOneFile()
        {
            string root = ScriptRig.TempDir();
            try
            {
                string pics;
                string zip = Shared(root, out pics);
                string toPics = Path.Combine(root, "b-pictures");
                ScriptStore b = new ScriptStore(Path.Combine(root, "b-scripts"));
                ScriptImport r = ScriptPack.Import(zip, b, toPics, PictureShelf.For(null, null, toPics), delegate { return null; });
                Assert.Equal(null, r.Problem, "imported");
                Assert.Equal("Farm", r.Name, "by its own name");
                Assert.Equal("clickpicture(\"egg\") waitfor('egg', 2) print(\"hello\")", b.Load("Farm"), "the code as it was");
                Assert.True(File.Exists(Path.Combine(toPics, EGG)), "the picture came");
                Assert.True(File.Exists(Path.Combine(toPics, EGG_MASK)), "and its painted-out copy");
                ScriptSettings s = b.Settings("Farm");
                // Someone else's: not trusted, on the Roblox window in front,
                // no key of theirs - the rest as it was.
                Assert.True(!s.Trusted && s.OnFront && s.Key == 0 && s.SummaryToDiscord, "and its settings");
                Assert.Equal(0, r.Renamed.Count, "nothing needed a new name");

                // Again: the same picture is there already, and the script's
                // name is taken.
                ScriptImport again = ScriptPack.Import(zip, b, toPics, PictureShelf.For(null, null, toPics), delegate { return null; });
                Assert.Equal("Farm 2", again.Name, "the script is renamed");
                Assert.Equal(2, Directory.GetFiles(toPics).Length, "the same picture is not copied twice");
            }
            finally { Directory.Delete(root, true); }
        }

        public static void TestAPictureWhoseNameIsTakenIsRenamedAndTheScriptFollows()
        {
            string root = ScriptRig.TempDir();
            try
            {
                string pics;
                string zip = Shared(root, out pics);
                string toPics = Path.Combine(root, "b-pictures");
                Directory.CreateDirectory(toPics);
                // Somebody else's own "egg", a different picture.
                File.WriteAllBytes(Path.Combine(toPics, "egg-20250101-000000.png"), Png(Color.Green));
                ScriptStore b = new ScriptStore(Path.Combine(root, "b-scripts"));
                ScriptImport r = ScriptPack.Import(zip, b, toPics, PictureShelf.For(null, null, toPics), delegate { return "taken"; });
                Assert.Equal(null, r.Problem, "imported");
                Assert.Equal("egg 2", r.Renamed["egg"], "the picture has a new name");
                Assert.True(File.Exists(Path.Combine(toPics, "egg 2-20261112-120000.png")), "under it");
                Assert.True(File.Exists(Path.Combine(toPics, "egg 2-20261112-120000-mask-20261112-120500.png")), "and so has its painted-out copy");
                Assert.True(File.Exists(Path.Combine(toPics, "egg-20250101-000000.png")), "theirs is untouched");
                Assert.Equal("clickpicture(\"egg 2\") waitfor(\"egg 2\", 2) print(\"hello\")", b.Load(r.Name), "the script names the new one");
                Assert.True(r.KeyLeftOff, "a key taken here is left off");
                Assert.Equal((byte)0, b.Settings(r.Name).Key, "so it has none");

                string file, mask;
                Assert.True(ScriptPictures.Resolve(PictureShelf.For(null, null, toPics), toPics, "egg 2", out file, out mask), "the new name finds it");
                Assert.Equal("egg 2-20261112-120000.png", file, "the right file");
                Assert.Equal("egg 2-20261112-120000-mask-20261112-120500.png", mask, "with its mask");
            }
            finally { Directory.Delete(root, true); }
        }

        // A file made to write outside the folders: nothing of it is written
        // anywhere but where it belongs.
        public static void TestAFileWithMaliciousNamesWritesNothingOutside()
        {
            string root = ScriptRig.TempDir();
            try
            {
                string zip = Path.Combine(root, "evil" + ScriptPack.EXT);
                using (FileStream fs = File.Create(zip))
                using (ZipArchive z = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    Put(z, "../evil.lua", "os.execute('x')");
                    Put(z, "..\\evil.png", "x");
                    Put(z, "sub/evil.png", "x");
                    Put(z, "C:\\evil.png", "x");
                    Put(z, "/evil.png", "x");
                    Put(z, "script.lua", "print('ok')");
                    Put(z, "settings.json", "{\"name\":\"..\\\\..\\\\evil\",\"key\":0}");
                    Put(z, "pictures.json", "[{\"name\":\"x\",\"file\":\"../evil.png\"},{\"name\":\"y\",\"file\":\"sub/evil.png\"}]");
                }
                string scripts = Path.Combine(root, "in", "scripts"), pics = Path.Combine(root, "in", "pictures");
                ScriptStore s = new ScriptStore(scripts);
                ScriptImport r = ScriptPack.Import(zip, s, pics, null, null);
                Assert.Equal(null, r.Problem, "the good part imported");
                Assert.Equal("Shared script", r.Name, "under a safe name, not the one it gave");
                Assert.Equal("print('ok')", s.Load(r.Name), "the script itself");
                Assert.Equal(0, r.Pictures.Count, "no picture from outside");
                foreach (string f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    Assert.False(Path.GetFileName(f).StartsWith("evil", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(ScriptPack.EXT), "nothing called evil anywhere: " + f);
                Assert.False(File.Exists(Path.Combine(Path.GetPathRoot(root), "evil.png")), "not at the root of the drive either");
            }
            finally { Directory.Delete(root, true); }
        }

        public static void TestAFileThatIsNotAScriptSaysSo()
        {
            string root = ScriptRig.TempDir();
            try
            {
                string zip = Path.Combine(root, "junk" + ScriptPack.EXT);
                File.WriteAllText(zip, "this is not a zip");
                ScriptImport r = ScriptPack.Import(zip, new ScriptStore(Path.Combine(root, "s")), Path.Combine(root, "p"), null, null);
                Assert.NotEqual(null, r.Problem, "says why");
                Assert.Equal(null, r.Name, "and made nothing");
            }
            finally { Directory.Delete(root, true); }
        }

        static void Put(ZipArchive z, string name, string text)
        {
            ZipArchiveEntry e = z.CreateEntry(name);
            using (Stream s = e.Open()) { byte[] b = Encoding.UTF8.GetBytes(text); s.Write(b, 0, b.Length); }
        }
    }
}
