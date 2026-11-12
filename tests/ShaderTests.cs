using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace RobloxKeeper.Tests
{
    // The shader overlay's looks, and keeping one overlay over each Roblox
    // window. The shaders here are really compiled - with the compiler that
    // ships with Windows, which needs no screen - but nothing is drawn and no
    // overlay is started.
    static class ShaderTests
    {
        // ---------- the looks ----------

        public static void TestEveryBuiltInLookCompilesAtEveryStrength()
        {
            foreach (ShaderPreset p in ShaderLooks.Presets)
                for (int strength = 0; strength < ShaderLooks.StrengthNames.Length; strength++)
                    Assert.Equal(null, Shader.Overlay.CompileProblem(ShaderLooks.Source(ShaderLooks.At(p, strength))),
                        p.Name + " at " + ShaderLooks.StrengthNames[strength]);
        }

        public static void TestTheExampleToStartFromCompiles()
        {
            Assert.Equal(null, Shader.Overlay.CompileProblem(ShaderLooks.Example()), "what someone copies to make their own");
        }

        // Someone's own shader with a mistake in it: the compiler's words
        // come back, to be shown, rather than nothing happening.
        public static void TestAShaderWithAMistakeSaysWhatIsWrong()
        {
            string why = Shader.Overlay.CompileProblem(ShaderLooks.Header + "float4 main(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target { return nonsense; }");
            Assert.True(why != null, "refused");
            Assert.Contains("nonsense", why, "and it names the mistake");
        }

        public static void TestStrengthScalesHowFarALookGoes()
        {
            ShaderPreset vivid = ShaderLooks.Find("vivid");
            Assert.True(vivid != null, "found by name, however it is cased");
            ShaderPreset gentle = ShaderLooks.At(vivid, 0), strong = ShaderLooks.At(vivid, 2);
            Assert.True(gentle.Saturation < vivid.Saturation && vivid.Saturation < strong.Saturation, "colour");
            Assert.True(gentle.Saturation > 1, "gentle is still more colour than none");
            Assert.True(gentle.Sharpen < strong.Sharpen, "sharpness");
            Assert.True(ShaderLooks.At(vivid, 99).Saturation <= 2, "and a strength nobody offered is held to the strongest");
        }

        public static void TestBlackAndWhiteStaysBlackAndWhite()
        {
            ShaderPreset bw = ShaderLooks.Find("Black and white");
            for (int strength = 0; strength < 3; strength++)
                Assert.Equal(0.0, ShaderLooks.At(bw, strength).Saturation, "no colour at " + ShaderLooks.StrengthNames[strength]);
        }

        public static void TestALookIsSaidByItsName()
        {
            Assert.Equal("Off", ShaderLooks.Describe(ShaderLooks.OFF), "none");
            Assert.Equal("Vivid", ShaderLooks.Describe(ShaderLooks.PRESET + "Vivid"), "a built-in one");
            Assert.Equal("Your own: mine.hlsl", ShaderLooks.Describe(ShaderLooks.FILE + "mine.hlsl"), "one of your own");
            Assert.False(ShaderLooks.IsOn(ShaderLooks.OFF), "off is off");
        }

        // ---------- the file an overlay is started with ----------

        static string TempFolder()
        {
            string dir = Path.Combine(Path.GetTempPath(), "rk-shaders-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void TestABuiltInLookIsWrittenOnceNotEverySecond()
        {
            string dir = TempFolder();
            try
            {
                string problem;
                string file = ShaderLooks.FileFor(ShaderLooks.PRESET + "Vivid", 1, dir, out problem);
                Assert.True(file != null && File.Exists(file), "written: " + problem);
                Assert.Contains("SATURATION", File.ReadAllText(file), "with the look's numbers in it");
                DateTime first = new DateTime(2020, 1, 1);
                File.SetLastWriteTimeUtc(file, first);

                ShaderLooks.FileFor(ShaderLooks.PRESET + "Vivid", 1, dir, out problem);
                Assert.Equal(first, File.GetLastWriteTimeUtc(file), "the same look again leaves the file alone");
                ShaderLooks.FileFor(ShaderLooks.PRESET + "Vivid", 2, dir, out problem);
                Assert.NotEqual(first, File.GetLastWriteTimeUtc(file), "a stronger one rewrites it - which is how a running overlay is known to be stale");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // A look is a name in the shaders folder, never a path out of it.
        public static void TestYourOwnShaderOnlyEverComesFromTheShadersFolder()
        {
            string dir = TempFolder();
            string outside = Path.Combine(Path.GetTempPath(), "rk-outside-" + Guid.NewGuid().ToString("N") + ".hlsl");
            try
            {
                File.WriteAllText(Path.Combine(dir, "mine.hlsl"), ShaderLooks.Example());
                File.WriteAllText(outside, ShaderLooks.Example());
                string problem;
                Assert.Equal(Path.Combine(dir, "mine.hlsl"), ShaderLooks.FileFor(ShaderLooks.FILE + "mine.hlsl", 1, dir, out problem), "one that is there");
                Assert.Equal(null, ShaderLooks.FileFor(ShaderLooks.FILE + "..\\" + Path.GetFileName(outside), 1, dir, out problem), "one reached by climbing out is not found");
                Assert.Contains("no longer in the shaders folder", problem, "and it says so");
                Assert.Equal(null, ShaderLooks.FileFor(ShaderLooks.FILE + outside, 1, dir, out problem), "nor one named by a whole path");
                Assert.Equal(null, ShaderLooks.FileFor(ShaderLooks.PRESET + "No such look", 1, dir, out problem), "nor a look this version hasn't got");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
                try { File.Delete(outside); } catch { }
            }
        }

        public static void TestTheListOfYourOwnLeavesOutTheAppsFiles()
        {
            string dir = TempFolder();
            try
            {
                foreach (string name in new string[] { "_builtin.hlsl", "example.hlsl", "zeta.hlsl", "Alpha.hlsl", "notes.txt" })
                    File.WriteAllText(Path.Combine(dir, name), "x");
                Assert.Equal("Alpha.hlsl,zeta.hlsl", string.Join(",", ShaderLooks.UserFiles(dir).ToArray()), "yours only, in order");
                Assert.Equal(0, ShaderLooks.UserFiles(Path.Combine(dir, "missing")).Count, "no folder yet is not an error");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static void TestAnOverlayIsStartedWithTheWindowTheShaderAndWhoStartedIt()
        {
            string args = ShaderLooks.Arguments(new IntPtr(0x1A2B3C), "C:\\x y\\_builtin.hlsl", 4242, "C:\\t\\p.txt");
            Assert.Contains("--overlay", args, "it is an overlay, not the app");
            Assert.Contains("--hwnd 1715004", args, "which window");
            Assert.Contains("--parent 4242", args, "who to go with when they close");
            Assert.Contains("--shader \"C:\\x y\\_builtin.hlsl\"", args, "the shader, quoted for a path with a space");
        }

        // ---------- one overlay over each window ----------

        class Fake
        {
            public readonly ShaderRunner Runner = new ShaderRunner();
            public readonly List<string> Started = new List<string>();
            public readonly List<Process> Stopped = new List<Process>();
            public readonly List<Process> Dead = new List<Process>();
            public readonly string Folder = TempFolder();

            public Fake()
            {
                Runner.ProblemFolder = Folder;
                Runner.Start = delegate(string args) { Started.Add(args); return new Process(); };
                Runner.Alive = delegate(Process p) { return !Dead.Contains(p); };
                Runner.Stop = delegate(Process p) { Stopped.Add(p); };
            }

            public string Shader()
            {
                string file = Path.Combine(Folder, "_builtin.hlsl");
                if (!File.Exists(file)) File.WriteAllText(file, "x");
                return file;
            }
        }

        static Dictionary<int, IntPtr> Windows(params int[] pids)
        {
            Dictionary<int, IntPtr> w = new Dictionary<int, IntPtr>();
            foreach (int pid in pids) w[pid] = new IntPtr(pid * 100);
            return w;
        }

        static readonly DateTime T0 = new DateTime(2026, 11, 11, 12, 0, 0);

        public static void TestEachRobloxWindowGetsOneOverlay()
        {
            Fake f = new Fake();
            try
            {
                f.Runner.Sync(Windows(1, 2), f.Shader(), T0);
                Assert.Equal(2, f.Started.Count, "one each");
                f.Runner.Sync(Windows(1, 2), f.Shader(), T0.AddSeconds(1));
                Assert.Equal(2, f.Started.Count, "and not another every second");
                f.Runner.Sync(Windows(1, 2, 3), f.Shader(), T0.AddSeconds(2));
                Assert.Equal(3, f.Started.Count, "a window opened since gets one too");
                Assert.Equal(3, f.Runner.Count, "three going");
            }
            finally { try { Directory.Delete(f.Folder, true); } catch { } }
        }

        public static void TestAClosedWindowsOverlayIsEnded()
        {
            Fake f = new Fake();
            try
            {
                f.Runner.Sync(Windows(1, 2), f.Shader(), T0);
                f.Runner.Sync(Windows(2), f.Shader(), T0.AddSeconds(1));
                Assert.Equal(1, f.Stopped.Count, "the one whose window went");
                Assert.Equal(1, f.Runner.Count, "the other carries on");
            }
            finally { try { Directory.Delete(f.Folder, true); } catch { } }
        }

        public static void TestSwitchingTheLookOffEndsEveryOverlay()
        {
            Fake f = new Fake();
            try
            {
                f.Runner.Sync(Windows(1, 2), f.Shader(), T0);
                f.Runner.Sync(Windows(1, 2), null, T0.AddSeconds(1));
                Assert.Equal(2, f.Stopped.Count, "both");
                Assert.Equal(0, f.Runner.Count, "none left");
                Assert.Equal(2, f.Started.Count, "and none started");
            }
            finally { try { Directory.Delete(f.Folder, true); } catch { } }
        }

        // The look was changed, or the file edited and saved: the overlay
        // compiled the old one, so it is started again.
        public static void TestAChangedLookStartsTheOverlaysAgain()
        {
            Fake f = new Fake();
            try
            {
                string shader = f.Shader();
                File.SetLastWriteTimeUtc(shader, new DateTime(2026, 1, 1));
                f.Runner.Sync(Windows(1), shader, T0);
                File.SetLastWriteTimeUtc(shader, new DateTime(2026, 1, 2));
                f.Runner.Sync(Windows(1), shader, T0.AddSeconds(1));
                Assert.Equal(1, f.Stopped.Count, "the old one ended");
                Assert.Equal(2, f.Started.Count, "and one started with what the file says now");
            }
            finally { try { Directory.Delete(f.Folder, true); } catch { } }
        }

        // A shader with a mistake in it ends its overlay at once. It says why
        // once, and is not started and failed again every second.
        public static void TestAnOverlayThatFailsSaysWhyAndIsNotStartedEverySecond()
        {
            Fake f = new Fake();
            try
            {
                f.Runner.Sync(Windows(1), f.Shader(), T0);
                // It wrote why and ended.
                foreach (string file in Directory.GetFiles(f.Folder, "rk-shader-*.txt")) File.Delete(file);
                File.WriteAllText(Path.Combine(f.Folder, "rk-shader-1.txt"), "error X3004: undeclared identifier 'nonsense'");
                f.Dead.Add(new Process());
                f.Runner.Alive = delegate { return false; };

                f.Runner.Sync(Windows(1), f.Shader(), T0.AddSeconds(1));
                Assert.Contains("nonsense", f.Runner.Problem, "why, in the compiler's words");
                Assert.Equal(1, f.Started.Count, "not started again straight away");
                f.Runner.Sync(Windows(1), f.Shader(), T0.AddSeconds(10));
                Assert.Equal(1, f.Started.Count, "nor ten seconds on");

                f.Runner.Alive = delegate { return true; };
                f.Runner.Sync(Windows(1), f.Shader(), T0.AddSeconds(40));
                Assert.Equal(2, f.Started.Count, "but tried again after a while");
            }
            finally { try { Directory.Delete(f.Folder, true); } catch { } }
        }

        // A fault in graphics code ends an overlay without a word. That is
        // still a failure: it must not be started again every second.
        public static void TestAnOverlayThatDiesWithoutAWordIsNotStartedEverySecond()
        {
            Fake f = new Fake();
            try
            {
                f.Runner.Sync(Windows(1), f.Shader(), T0);
                f.Runner.Alive = delegate { return false; };
                for (int second = 1; second <= 20; second++) f.Runner.Sync(Windows(1), f.Shader(), T0.AddSeconds(second));
                Assert.Equal(1, f.Started.Count, "started once in twenty seconds, not twenty times");
                Assert.Contains("stopped by itself", f.Runner.Problem, "and it says what it can");
            }
            finally { try { Directory.Delete(f.Folder, true); } catch { } }
        }

        public static void TestALookChosenAgainIsTriedAtOnce()
        {
            Fake f = new Fake();
            try
            {
                f.Runner.Alive = delegate { return false; };
                f.Runner.Sync(Windows(1), f.Shader(), T0);
                File.WriteAllText(Path.Combine(f.Folder, "rk-shader-1.txt"), "it failed");
                f.Runner.Sync(Windows(1), f.Shader(), T0.AddSeconds(1));
                Assert.Equal(1, f.Started.Count, "waiting, after a failure");
                f.Runner.TryAgain();
                f.Runner.Sync(Windows(1), f.Shader(), T0.AddSeconds(2));
                Assert.Equal(2, f.Started.Count, "tried at once when asked");
            }
            finally { try { Directory.Delete(f.Folder, true); } catch { } }
        }

        // ---------- settings and the page ----------

        public static void TestTheLookIsKeptWithTheSettings()
        {
            AppSettings s = new AppSettings();
            Assert.False(ShaderLooks.IsOn(s.Shader), "off until someone switches it on");
            s.Shader = ShaderLooks.PRESET + "Cinema";
            s.ShaderStrength = 2;
            AppSettings back = AppSettings.Parse(s.ToLines());
            Assert.Equal(ShaderLooks.PRESET + "Cinema", back.Shader, "the look");
            Assert.Equal(2, back.ShaderStrength, "and how strong");
            Assert.False(ShaderLooks.IsOn(AppSettings.Parse(new string[] { "shader=C:\\evil.hlsl" }).Shader), "a hand-edited path is not a look");
        }

        public static void TestThePageListsOffThenEveryLook()
        {
            using (MainForm f = new MainForm(false))
            {
                f.ShowPage(MainForm.PAGE_LOOKS);
                LooksPage page = (LooksPage)f.PageNow;
                Assert.True(page.ShaderChoices >= ShaderLooks.Presets.Length + 1, "off, and every built-in look");
                page.ChooseShader(2);
                Assert.Equal(ShaderLooks.PRESET + ShaderLooks.Presets[1].Name, f.Shader, "choosing one switches it on");
                page.ChooseShader(0);
                Assert.False(ShaderLooks.IsOn(f.Shader), "and Off switches it off");
            }
        }
    }
}
