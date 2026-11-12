using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RobloxKeeper.Tests
{
    // Graphics presets go through the one settings file Roblox reads, and
    // Roblox only honours a short list of flags in it. Getting anything else
    // in there is what gets accounts banned, so the thing most worth proving
    // is what this code will not write. Every folder here is a made-up one
    // under the temp folder; the real Roblox install is never looked at.
    static class LooksFlagsTests
    {
        const string UNLISTED = "DFIntTaskSchedulerTargetFps";

        static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "rk_looksflags_" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(d);
            return d;
        }

        static string MakeVersion(string root, string name)
        {
            string folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "RobloxPlayerBeta.exe"), "not really a program");
            return folder;
        }

        static string WriteSettings(string versionFolder, string json)
        {
            string path = LooksFlags.PathIn(versionFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
            return path;
        }

        static string Q(string plain)
        {
            return LooksFlags.Quoted(plain);
        }

        static LooksChoices Everything()
        {
            LooksChoices c = new LooksChoices();
            c.AntiAliasing = LooksAntiAliasing.X4;
            c.QualityLevel = 7;
            c.Grass = LooksGrass.Short;
            c.DpiScalingOff = true;
            c.Textures = LooksTextures.Medium;
            return c;
        }

        static void SameChoices(LooksChoices want, LooksChoices got, string what)
        {
            Assert.Equal(want.AntiAliasing, got.AntiAliasing, what + ": anti-aliasing");
            Assert.Equal(want.QualityLevel, got.QualityLevel, what + ": quality level");
            Assert.Equal(want.Grass, got.Grass, what + ": grass");
            Assert.Equal(want.DpiScalingOff, got.DpiScalingOff, what + ": DPI scaling");
            Assert.Equal(want.Textures, got.Textures, what + ": textures");
        }

        // ---------- The allowlist ----------

        public static void TestOnlyTheFlagsRobloxListsAreAllowed()
        {
            // Spelled out again here rather than read from the code, so a slip
            // of the keyboard in the one list that matters shows up.
            string[] published =
            {
                "DFIntCSGLevelOfDetailSwitchingDistance",
                "DFIntCSGLevelOfDetailSwitchingDistanceL12",
                "DFIntCSGLevelOfDetailSwitchingDistanceL23",
                "DFIntCSGLevelOfDetailSwitchingDistanceL34",
                "FFlagHandleAltEnterFullscreenManually",
                "DFFlagTextureQualityOverrideEnabled",
                "DFIntTextureQualityOverride",
                "FIntDebugForceMSAASamples",
                "DFFlagDisableDPIScale",
                "FFlagDebugGraphicsPreferD3D11",
                "FFlagDebugGraphicsPreferVulkan",
                "FFlagDebugGraphicsPreferOpenGL",
                "FFlagDebugSkyGray",
                "DFFlagDebugPauseVoxelizer",
                "DFIntDebugFRMQualityLevelOverride",
                "FIntFRMMaxGrassDistance",
                "FIntFRMMinGrassDistance",
                "FIntGrassMovementReducedMotionFactor"
            };
            foreach (string name in published)
                Assert.True(LooksFlags.IsAllowed(name), name + " is on Roblox's list");
            Assert.Equal(published.Length, LooksFlags.Allowed.Length, "the whole published list and nothing more");

            Assert.False(LooksFlags.IsAllowed(UNLISTED), "the frame rate cap is not");
            Assert.False(LooksFlags.IsAllowed("fintdebugforcemsaasamples"), "a listed name in the wrong case is a different name");
            Assert.False(LooksFlags.IsAllowed("FIntDebugForceMSAASamples "), "so is one with a space after it");
            Assert.False(LooksFlags.IsAllowed(""), "no name");
            Assert.False(LooksFlags.IsAllowed(null), "nothing at all");
        }

        public static void TestEveryFlagTheChoicesCanSetIsOnTheList()
        {
            foreach (string name in LooksFlags.Owned)
                Assert.True(LooksFlags.IsAllowed(name), name + " is one Roblox allows");
            foreach (string name in Everything().ToFlags().Keys)
                Assert.True(LooksFlags.IsOwned(name), name + " is one we would also take back out");
        }

        // ---------- Choices ----------

        public static void TestChoicesLeftAtDefaultSetNothing()
        {
            Assert.Equal(0, new LooksChoices().ToFlags().Count, "default means Roblox decides, so no flag is written");
        }

        public static void TestEachChoiceBecomesItsFlag()
        {
            Dictionary<string, string> flags = Everything().ToFlags();
            Assert.Equal("4", LooksFlags.Text(flags["FIntDebugForceMSAASamples"]), "4x anti-aliasing");
            Assert.Equal("7", LooksFlags.Text(flags["DFIntDebugFRMQualityLevelOverride"]), "quality level 7");
            Assert.Equal("True", LooksFlags.Text(flags["DFFlagDisableDPIScale"]), "DPI scaling off");
            Assert.Equal("True", LooksFlags.Text(flags["DFFlagTextureQualityOverrideEnabled"]), "the texture override switched on");
            Assert.Equal(LooksChoices.TEXTURES_MEDIUM.ToString(), LooksFlags.Text(flags["DFIntTextureQualityOverride"]), "at medium");
            Assert.Equal(LooksChoices.GRASS_SHORT_MIN.ToString(), LooksFlags.Text(flags["FIntFRMMinGrassDistance"]), "grass starting near");
            Assert.Equal(LooksChoices.GRASS_SHORT_MAX.ToString(), LooksFlags.Text(flags["FIntFRMMaxGrassDistance"]), "and ending not far off");
            Assert.Equal(7, flags.Count, "and nothing else");

            LooksChoices none = new LooksChoices();
            none.Grass = LooksGrass.None;
            Assert.Equal("0", LooksFlags.Text(none.ToFlags()["FIntFRMMaxGrassDistance"]), "no grass is a distance of nothing");
        }

        public static void TestTheQualityLevelIsHeldToWhatRobloxHas()
        {
            LooksChoices c = new LooksChoices();
            c.QualityLevel = 50;
            Assert.Equal("21", LooksFlags.Text(c.ToFlags()["DFIntDebugFRMQualityLevelOverride"]), "there is no level above 21");
            c.QualityLevel = -3;
            Assert.False(c.ToFlags().ContainsKey("DFIntDebugFRMQualityLevelOverride"), "below 1 is no override at all");
        }

        public static void TestChoicesComeBackFromTheirOwnFlags()
        {
            SameChoices(Everything(), LooksChoices.FromFlags(Everything().ToFlags()), "everything set");
            SameChoices(new LooksChoices(), LooksChoices.FromFlags(new LooksChoices().ToFlags()), "nothing set");
            SameChoices(new LooksChoices(), LooksChoices.FromFlags(null), "no flags at all");

            foreach (LooksAntiAliasing aa in Enum.GetValues(typeof(LooksAntiAliasing)))
                foreach (LooksGrass grass in Enum.GetValues(typeof(LooksGrass)))
                    foreach (LooksTextures textures in Enum.GetValues(typeof(LooksTextures)))
                    {
                        LooksChoices c = new LooksChoices();
                        c.AntiAliasing = aa;
                        c.Grass = grass;
                        c.Textures = textures;
                        c.QualityLevel = 21;
                        SameChoices(c, LooksChoices.FromFlags(c.ToFlags()), aa + "/" + grass + "/" + textures);
                    }
        }

        // People edit this file by hand, and write values every which way.
        public static void TestHandTypedValuesReadAsTheNearestChoice()
        {
            bool unreadable;
            Dictionary<string, string> flags = LooksFlags.Parse(
                "{ \"FIntDebugForceMSAASamples\": 3, \"DFFlagDisableDPIScale\": true," +
                " \"DFIntDebugFRMQualityLevelOverride\": \"99\", \"FIntFRMMaxGrassDistance\": \"5000\"," +
                " \"DFFlagTextureQualityOverrideEnabled\": \"true\", \"DFIntTextureQualityOverride\": 2 }", out unreadable);
            Assert.False(unreadable, "it is a perfectly good file");

            LooksChoices c = LooksChoices.FromFlags(flags);
            Assert.Equal(LooksAntiAliasing.X2, c.AntiAliasing, "3 samples is nearest 2x");
            Assert.True(c.DpiScalingOff, "a bare true counts");
            Assert.Equal(21, c.QualityLevel, "99 shows as the top level");
            Assert.Equal(LooksGrass.Far, c.Grass, "a long way is far");
            Assert.Equal(LooksTextures.High, c.Textures, "a lower-case quoted true switches the override on");
        }

        public static void TestATextureLevelWithItsSwitchOffIsNotAChoice()
        {
            Dictionary<string, string> flags = new Dictionary<string, string>();
            flags["DFIntTextureQualityOverride"] = Q("0");
            flags["DFFlagTextureQualityOverrideEnabled"] = Q("False");
            Assert.Equal(LooksTextures.Default, LooksChoices.FromFlags(flags).Textures, "Roblox ignores the level, so it shows as default");
        }

        // ---------- Reading ----------

        public static void TestValuesAreKeptAsTheyWereWritten()
        {
            bool unreadable;
            Dictionary<string, string> flags = LooksFlags.Parse(
                "{\r\n  \"A\": \"true\",\r\n  \"B\": true,\r\n  \"C\": 12,\r\n  \"D\": \"12\",\r\n  \"E\": -1.5e3,\r\n" +
                "  \"F\": \"say \\\"hi\\\"\",\r\n  \"G\": null,\r\n  \"H\": { \"x\": [1, \"}\", 2] }\r\n}", out unreadable);
            Assert.False(unreadable, "a tidy file");
            Assert.Equal(8, flags.Count, "every name");
            Assert.Equal("\"true\"", flags["A"], "a quoted true stays quoted");
            Assert.Equal("true", flags["B"], "a bare true stays bare");
            Assert.Equal("12", flags["C"], "a bare number stays bare");
            Assert.Equal("\"12\"", flags["D"], "a quoted number stays quoted");
            Assert.Equal("-1.5e3", flags["E"], "a number written the long way is not rewritten");
            Assert.Equal("say \"hi\"", LooksFlags.Text(flags["F"]), "quotes inside a value come back out");
            Assert.Equal("null", flags["G"], "an empty value is kept");
            Assert.Equal("{ \"x\": [1, \"}\", 2] }", flags["H"], "something that is not a flag is carried whole");
            Assert.Equal("12", LooksFlags.Text(flags["D"]), "the plain value has no quotes");
        }

        public static void TestAMissingFileIsNoFlagsAndNotAnError()
        {
            string dir = TempDir();
            try
            {
                bool unreadable;
                Dictionary<string, string> flags = LooksFlags.Read(Path.Combine(dir, "ClientSettings", "ClientAppSettings.json"), out unreadable);
                Assert.Equal(0, flags.Count, "nothing set");
                Assert.False(unreadable, "nothing wrong either");
                Assert.Equal(0, LooksFlags.Read(Path.Combine(dir, "nope.json")).Count, "the short form agrees");
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void TestABrokenFileReadsAsEmptyAndSaysSo()
        {
            string[] broken =
            {
                "{ \"A\": 1, }",                // a comma with nothing after it
                "{ \"A\": 1",                   // cut short
                "{ \"A\" 1 }",                  // no colon
                "{ A: 1 }",                     // a name without quotes
                "{ \"A\": True }",              // not a JSON word
                "{ \"A\": 01 }",                // not a JSON number
                "[ 1, 2 ]",                     // not an object
                "{ \"A\": 1 } trailing",        // something after the end
                "{ \"A\": \"no end }",          // a string never closed
                "this is not JSON"
            };
            foreach (string json in broken)
            {
                bool unreadable;
                Dictionary<string, string> flags = LooksFlags.Parse(json, out unreadable);
                Assert.True(unreadable, "flagged as unreadable: " + json);
                Assert.Equal(0, flags.Count, "and nothing half-read is handed back: " + json);
            }

            StringBuilder deep = new StringBuilder("{ \"A\": ");
            for (int i = 0; i < 5000; i++) deep.Append('[');
            bool tooDeep;
            LooksFlags.Parse(deep.ToString(), out tooDeep);
            Assert.True(tooDeep, "a file of nothing but brackets is refused, not followed to the bottom");
        }

        public static void TestAnEmptyFileOrOneSavedByNotepadIsNotBroken()
        {
            bool unreadable;
            Assert.Equal(0, LooksFlags.Parse("", out unreadable).Count, "an empty file holds no flags");
            Assert.False(unreadable, "and is not broken");
            Assert.Equal(0, LooksFlags.Parse(" \r\n ", out unreadable).Count, "nor is one of blank lines");
            Assert.False(unreadable, "blank lines are not broken");
            Assert.Equal(0, LooksFlags.Parse("{}", out unreadable).Count, "an empty object");
            Assert.False(unreadable, "an empty object is fine");

            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "bom.json");
                File.WriteAllText(path, "{ \"FFlagDebugSkyGray\": \"True\" }", new UTF8Encoding(true));
                Dictionary<string, string> flags = LooksFlags.Read(path, out unreadable);
                Assert.False(unreadable, "a byte order mark at the front is not a broken file");
                Assert.Equal("True", LooksFlags.Text(flags["FFlagDebugSkyGray"]), "and the flag after it is read");
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---------- Merging ----------

        public static void TestMergeSetsOursAndLeavesEverythingElseAsItWas()
        {
            Dictionary<string, string> existing = new Dictionary<string, string>();
            existing["FIntDebugForceMSAASamples"] = "8";              // ours, an old value
            existing["DFIntDebugFRMQualityLevelOverride"] = Q("3");   // ours, no longer wanted
            existing["FFlagDebugSkyGray"] = "true";                   // listed, but not ours
            existing[UNLISTED] = Q("240");                            // the user's own, unlisted
            existing["Nested"] = "{ \"a\": 1 }";

            LooksChoices c = new LooksChoices();
            c.AntiAliasing = LooksAntiAliasing.X2;
            Dictionary<string, string> merged = LooksFlags.Merge(existing, c.ToFlags());

            Assert.Equal("2", LooksFlags.Text(merged["FIntDebugForceMSAASamples"]), "our flag takes the new choice");
            Assert.False(merged.ContainsKey("DFIntDebugFRMQualityLevelOverride"), "a choice back at default takes its flag out");
            Assert.Equal("true", merged["FFlagDebugSkyGray"], "a listed flag that is not ours is untouched, bare as it was");
            Assert.Equal("\"240\"", merged[UNLISTED], "the user's own unlisted flag is untouched");
            Assert.Equal("{ \"a\": 1 }", merged["Nested"], "and so is anything else of theirs");
            Assert.Equal(4, merged.Count, "nothing added beyond that");
            Assert.Equal(5, existing.Count, "and what was handed in is not changed");
        }

        public static void TestMergeRefusesAFlagThatIsNotOnTheList()
        {
            Dictionary<string, string> wanted = new Dictionary<string, string>();
            wanted[UNLISTED] = Q("240");
            wanted["FFlagDebugSkyGray"] = Q("True");
            List<string> refused = new List<string>();

            Dictionary<string, string> merged = LooksFlags.Merge(new Dictionary<string, string>(), wanted, refused);
            Assert.False(merged.ContainsKey(UNLISTED), "the unlisted flag is not added");
            Assert.True(merged.ContainsKey("FFlagDebugSkyGray"), "the listed one is");
            Assert.Equal(1, refused.Count, "one name was turned away");
            Assert.Equal(UNLISTED, refused[0], "and it is named");
            Assert.False(LooksFlags.Merge(null, wanted).ContainsKey(UNLISTED), "the short form refuses it too");
        }

        // ---------- Writing ----------

        // Write is the last thing before the disk, so it holds the list
        // itself rather than trusting whoever called it.
        public static void TestWriteRefusesToAddAFlagThatIsNotOnTheList()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "ClientSettings", "ClientAppSettings.json");
                Dictionary<string, string> flags = new Dictionary<string, string>();
                flags["FFlagDebugSkyGray"] = Q("True");
                flags[UNLISTED] = Q("240");

                string why = LooksFlags.Write(path, flags);
                Assert.True(why != null, "the write is refused");
                Assert.Contains(UNLISTED, why, "and the reason names the flag");
                Assert.False(File.Exists(path), "nothing was written, not even the flag that was fine");
                Assert.False(Directory.Exists(Path.GetDirectoryName(path)), "and no folder was left behind");
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void TestWriteRefusesToChangeAFlagThatIsNotOnTheList()
        {
            string dir = TempDir();
            try
            {
                string path = WriteSettings(dir, "{ \"" + UNLISTED + "\": \"240\" }");
                byte[] before = File.ReadAllBytes(path);

                Dictionary<string, string> flags = new Dictionary<string, string>();
                flags[UNLISTED] = Q("9999");
                Assert.True(LooksFlags.Write(path, flags) != null, "changing an unlisted flag is refused as well");
                Assert.Equal(Convert.ToBase64String(before), Convert.ToBase64String(File.ReadAllBytes(path)), "the file is byte for byte what it was");
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void TestWriteKeepsAnUnlistedFlagThatIsAlreadyThere()
        {
            string dir = TempDir();
            try
            {
                string path = WriteSettings(dir, "{ \"" + UNLISTED + "\": 240, \"FFlagDebugSkyGray\": \"True\" }");

                // Handed back unchanged: allowed, because nothing is being added.
                Dictionary<string, string> same = LooksFlags.Read(path);
                same["DFFlagDisableDPIScale"] = Q("True");
                Assert.Equal(null, LooksFlags.Write(path, same), "writing it back as it was is fine");

                // Left out altogether: it still stays.
                Dictionary<string, string> without = new Dictionary<string, string>();
                without["DFFlagDisableDPIScale"] = Q("True");
                Assert.Equal(null, LooksFlags.Write(path, without), "and so is not mentioning it");

                Dictionary<string, string> after = LooksFlags.Read(path);
                Assert.Equal("240", after[UNLISTED], "the user's flag is still there, bare as they wrote it");
                Assert.False(after.ContainsKey("FFlagDebugSkyGray"), "a listed flag left out is taken out");
                Assert.True(after.ContainsKey("DFFlagDisableDPIScale"), "and the new one is in");
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void TestWriteMakesTheFolderAndLeavesNoScraps()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "ClientSettings", "ClientAppSettings.json");
                Dictionary<string, string> flags = Everything().ToFlags();
                Assert.Equal(null, LooksFlags.Write(path, flags), "written");
                Assert.True(File.Exists(path), "the folder was made for it");
                Assert.Equal(1, Directory.GetFiles(Path.GetDirectoryName(path)).Length, "the temporary copy is gone");

                byte[] bytes = File.ReadAllBytes(path);
                Assert.Equal((byte)'{', bytes[0], "no byte order mark in front of the brace");

                // And again over the top of an existing file.
                Assert.Equal(null, LooksFlags.Write(path, flags), "written a second time");
                Assert.Equal(1, Directory.GetFiles(Path.GetDirectoryName(path)).Length, "still only the one file");
                Assert.Equal(Convert.ToBase64String(bytes), Convert.ToBase64String(File.ReadAllBytes(path)), "the same flags give the same bytes");
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void TestNamesAreWrittenInTheSameOrderEveryTime()
        {
            Dictionary<string, string> oneWay = new Dictionary<string, string>();
            oneWay["FFlagDebugSkyGray"] = Q("True");
            oneWay["DFFlagDisableDPIScale"] = Q("True");
            oneWay["FIntDebugForceMSAASamples"] = Q("4");

            Dictionary<string, string> otherWay = new Dictionary<string, string>();
            otherWay["FIntDebugForceMSAASamples"] = Q("4");
            otherWay["DFFlagDisableDPIScale"] = Q("True");
            otherWay["FFlagDebugSkyGray"] = Q("True");

            string json = LooksFlags.ToJson(oneWay);
            Assert.Equal(json, LooksFlags.ToJson(otherWay), "the order they were added in does not show");
            Assert.True(json.IndexOf("DFFlagDisableDPIScale") < json.IndexOf("FFlagDebugSkyGray"), "names go alphabetically");
            Assert.True(json.IndexOf("FFlagDebugSkyGray") < json.IndexOf("FIntDebugForceMSAASamples"), "all the way down");

            bool unreadable;
            Assert.Equal(3, LooksFlags.Parse(json, out unreadable).Count, "and what is written reads back");
            Assert.False(unreadable, "as good JSON");
            Assert.Equal(0, LooksFlags.Parse(LooksFlags.ToJson(new Dictionary<string, string>()), out unreadable).Count, "no flags is an empty object");
            Assert.False(unreadable, "which is good JSON too");
        }

        // A value handed over plain must not turn the file into something
        // Roblox cannot read.
        public static void TestAPlainValueIsQuotedRatherThanWrittenRaw()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "ClientSettings", "ClientAppSettings.json");
                Dictionary<string, string> flags = new Dictionary<string, string>();
                flags["FFlagDebugSkyGray"] = "True";          // not JSON as it stands
                flags["FIntDebugForceMSAASamples"] = "4";     // a bare number, which is
                Assert.Equal(null, LooksFlags.Write(path, flags), "written");

                bool unreadable;
                Dictionary<string, string> back = LooksFlags.Read(path, out unreadable);
                Assert.False(unreadable, "the file is good JSON");
                Assert.Equal("\"True\"", back["FFlagDebugSkyGray"], "the stray word was quoted");
                Assert.Equal("4", back["FIntDebugForceMSAASamples"], "the number was fine as it was");
            }
            finally { Directory.Delete(dir, true); }
        }

        public static void TestChoicesWrittenStraightToAFileReadBack()
        {
            string dir = TempDir();
            try
            {
                string path = Path.Combine(dir, "ClientSettings", "ClientAppSettings.json");
                Assert.Equal(null, LooksFlags.Write(path, Everything().ToFlags()), "written");
                SameChoices(Everything(), LooksChoices.FromFlags(LooksFlags.Read(path)), "through a file and back");
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---------- Every installed version ----------

        public static void TestApplyWritesEveryInstalledVersion()
        {
            string root = TempDir();
            try
            {
                string a = MakeVersion(root, "version-aaa111");
                string b = MakeVersion(root, "version-bbb222");

                // Neither of these is an installed player.
                string noExe = Path.Combine(root, "version-ccc333");
                Directory.CreateDirectory(noExe);
                string notAVersion = MakeVersion(root, "Downloads");

                LooksFlagsResult result = LooksFlags.Apply(root, Everything());
                Assert.Equal(2, result.Written, "both installed versions");
                Assert.Equal(0, result.Failed.Count, "and nothing went wrong");

                SameChoices(Everything(), LooksChoices.FromFlags(LooksFlags.Read(LooksFlags.PathIn(a))), "the first version");
                SameChoices(Everything(), LooksChoices.FromFlags(LooksFlags.Read(LooksFlags.PathIn(b))), "the second version");
                Assert.False(Directory.Exists(Path.Combine(noExe, "ClientSettings")), "a version folder with no player in it is left alone");
                Assert.False(Directory.Exists(Path.Combine(notAVersion, "ClientSettings")), "so is a folder that is not a version");

                List<string> found = LooksFlags.VersionFolders(root);
                Assert.Equal(2, found.Count, "the same two are what counts as installed");
                Assert.Equal("version-aaa111", Path.GetFileName(found[0]), "in a steady order");
            }
            finally { Directory.Delete(root, true); }
        }

        public static void TestApplyKeepsAHandEditedFile()
        {
            string root = TempDir();
            try
            {
                string v = MakeVersion(root, "version-aaa111");
                string path = WriteSettings(v,
                    "{\n\t\"" + UNLISTED + "\": 240,\n\t\"FFlagDebugSkyGray\": true,\n" +
                    "\t\"FFlagDebugGraphicsPreferVulkan\": \"True\",\n\t\"FIntDebugForceMSAASamples\": \"8\",\n" +
                    "\t\"Mine\": { \"note\": \"keep, me\" }\n}\n");

                LooksChoices c = new LooksChoices();
                c.AntiAliasing = LooksAntiAliasing.X2;
                c.DpiScalingOff = true;
                LooksFlagsResult result = LooksFlags.Apply(root, c);
                Assert.Equal(1, result.Written, "the file was updated");

                bool unreadable;
                Dictionary<string, string> after = LooksFlags.Read(path, out unreadable);
                Assert.False(unreadable, "it is still good JSON");
                Assert.Equal("240", after[UNLISTED], "their unlisted flag is exactly as they typed it");
                Assert.Equal("true", after["FFlagDebugSkyGray"], "their bare true is still bare");
                Assert.Equal("\"True\"", after["FFlagDebugGraphicsPreferVulkan"], "their quoted True is still quoted");
                Assert.Equal("{ \"note\": \"keep, me\" }", after["Mine"], "something that is not a flag at all is still there");
                Assert.Equal("2", LooksFlags.Text(after["FIntDebugForceMSAASamples"]), "the anti-aliasing they had is now the one chosen");
                Assert.True(after.ContainsKey("DFFlagDisableDPIScale"), "and the new choice is in");
                Assert.Equal(6, after.Count, "with nothing else added");
            }
            finally { Directory.Delete(root, true); }
        }

        public static void TestApplyLeavesAFileItCannotReadAlone()
        {
            string root = TempDir();
            try
            {
                string broken = MakeVersion(root, "version-aaa111");
                string fine = MakeVersion(root, "version-bbb222");
                string path = WriteSettings(broken, "{ \"" + UNLISTED + "\": 240, }");
                byte[] before = File.ReadAllBytes(path);

                LooksFlagsResult result = LooksFlags.Apply(root, Everything());
                Assert.Equal(1, result.Written, "the version with nothing wrong is written");
                Assert.Equal(1, result.Failed.Count, "the other is reported");
                Assert.Equal("version-aaa111", result.Failed[0].Where, "by its folder name");
                Assert.Contains("not valid JSON", result.Failed[0].Why, "with the reason in plain words");
                Assert.Equal(Convert.ToBase64String(before), Convert.ToBase64String(File.ReadAllBytes(path)),
                    "and the file somebody was half way through editing is not written over");
                Assert.True(File.Exists(LooksFlags.PathIn(fine)), "the good version got its file");

                LooksFlagsResult cleared = LooksFlags.Clear(root);
                Assert.Equal(1, cleared.Failed.Count, "clearing reports it the same way");
                Assert.Equal(Convert.ToBase64String(before), Convert.ToBase64String(File.ReadAllBytes(path)), "and leaves it alone too");
            }
            finally { Directory.Delete(root, true); }
        }

        public static void TestAFileThatIsInUseIsReportedInPlainWords()
        {
            string root = TempDir();
            try
            {
                string v = MakeVersion(root, "version-aaa111");
                string path = WriteSettings(v, "{ \"FFlagDebugSkyGray\": \"True\" }");
                LooksFlagsResult result;
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    result = LooksFlags.Apply(root, Everything());

                Assert.Equal(0, result.Written, "nothing could be written");
                Assert.Equal(1, result.Failed.Count, "and that is reported");
                Assert.Contains("in use", result.Failed[0].Why, "as something a person can act on");
                Assert.Equal(1, LooksFlags.Read(path).Count, "the file is as it was");
            }
            finally { Directory.Delete(root, true); }
        }

        public static void TestPuttingAChoiceBackToDefaultTakesItsFlagOut()
        {
            string root = TempDir();
            try
            {
                string v = MakeVersion(root, "version-aaa111");
                LooksFlags.Apply(root, Everything());

                LooksChoices fewer = new LooksChoices();
                fewer.DpiScalingOff = true;
                LooksFlags.Apply(root, fewer);

                Dictionary<string, string> after = LooksFlags.Read(LooksFlags.PathIn(v));
                Assert.Equal(1, after.Count, "only the one choice still made is in the file");
                Assert.True(after.ContainsKey("DFFlagDisableDPIScale"), "and it is the right one");
            }
            finally { Directory.Delete(root, true); }
        }

        public static void TestClearTakesOutOnlyOurFlags()
        {
            string root = TempDir();
            try
            {
                string v = MakeVersion(root, "version-aaa111");
                string path = WriteSettings(v, "{ \"" + UNLISTED + "\": \"240\", \"FFlagDebugSkyGray\": \"True\" }");
                LooksFlags.Apply(root, Everything());
                Assert.Equal(9, LooksFlags.Read(path).Count, "theirs and ours together");

                LooksFlagsResult result = LooksFlags.Clear(root);
                Assert.Equal(1, result.Written, "cleared");
                Dictionary<string, string> after = LooksFlags.Read(path);
                Assert.Equal(2, after.Count, "only theirs are left");
                Assert.Equal("\"240\"", after[UNLISTED], "the unlisted one as it was");
                Assert.Equal("\"True\"", after["FFlagDebugSkyGray"], "and the listed one that was never ours");
            }
            finally { Directory.Delete(root, true); }
        }

        public static void TestClearRemovesAFileAndFolderThatHeldOnlyOurs()
        {
            string root = TempDir();
            try
            {
                string a = MakeVersion(root, "version-aaa111");
                string b = MakeVersion(root, "version-bbb222");
                LooksFlags.Apply(root, Everything());
                Assert.True(File.Exists(LooksFlags.PathIn(a)), "applied to the first");
                Assert.True(File.Exists(LooksFlags.PathIn(b)), "and the second");

                LooksFlagsResult result = LooksFlags.Clear(root);
                Assert.Equal(2, result.Written, "both cleared");
                Assert.False(Directory.Exists(Path.Combine(a, "ClientSettings")), "the folder made for it is gone from the first");
                Assert.False(Directory.Exists(Path.Combine(b, "ClientSettings")), "and from the second");
                Assert.True(File.Exists(Path.Combine(a, "RobloxPlayerBeta.exe")), "and nothing else was touched");

                Assert.Equal(0, LooksFlags.Clear(root).Failed.Count, "clearing again finds nothing to do and nothing wrong");
            }
            finally { Directory.Delete(root, true); }
        }

        public static void TestClearKeepsAFolderThatHoldsSomethingElse()
        {
            string root = TempDir();
            try
            {
                string v = MakeVersion(root, "version-aaa111");
                LooksFlags.Apply(root, Everything());
                string other = Path.Combine(v, "ClientSettings", "notes.txt");
                File.WriteAllText(other, "mine");

                LooksFlags.Clear(root);
                Assert.False(File.Exists(LooksFlags.PathIn(v)), "the settings file is gone");
                Assert.True(File.Exists(other), "the file beside it is not ours to remove");
            }
            finally { Directory.Delete(root, true); }
        }

        public static void TestApplyingNothingToACleanInstallMakesNothing()
        {
            string root = TempDir();
            try
            {
                string v = MakeVersion(root, "version-aaa111");
                LooksFlagsResult result = LooksFlags.Apply(root, new LooksChoices());
                Assert.Equal(1, result.Written, "the version is as asked");
                Assert.False(Directory.Exists(Path.Combine(v, "ClientSettings")), "without a folder being made to say nothing in");
            }
            finally { Directory.Delete(root, true); }
        }

        public static void TestAMissingVersionsFolderIsNotAnError()
        {
            string nowhere = Path.Combine(Path.GetTempPath(), "rk_looksflags_none_" + Guid.NewGuid().ToString("n"));
            LooksFlagsResult result = LooksFlags.Apply(nowhere, Everything());
            Assert.Equal(0, result.Written, "nothing to write to");
            Assert.Equal(0, result.Failed.Count, "and nothing failed");
            Assert.False(Directory.Exists(nowhere), "the folder was not made");
            Assert.Equal(0, LooksFlags.Apply(null, Everything()).Written, "no folder given");
            Assert.Equal(0, LooksFlags.Clear("").Written, "an empty one");
        }
    }
}
