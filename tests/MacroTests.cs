using System;
using System.IO;

namespace RobloxKeeper.Tests
{
    // What a macro is, what it says about itself, and that it survives being
    // saved - before anything is ever played.
    static class MacroTests
    {
        static Macro Sample()
        {
            Macro m = new Macro();
            m.Name = "Buy egg";
            m.Steps.Add(MacroStep.Key(0x45, 50));                   // E
            m.Steps.Add(MacroStep.Wait(1500));
            m.Steps.Add(MacroStep.Click(0.5, 0.3, false));
            m.Steps.Add(MacroStep.Click(0.25, 0.75, true));
            m.Steps.Add(MacroStep.Type("/e dance\tnow"));
            m.Steps.Add(MacroStep.Key(0x57, 2000));                 // W, held
            return m;
        }

        public static void TestAMacroSurvivesTheRoundTrip()
        {
            Macro back = WatchStore.DeserializeMacro(WatchStore.SerializeMacro(Sample()));
            Assert.Equal("Buy egg", back.Name, "name");
            Assert.Equal(6, back.Steps.Count, "every step");
            Assert.Equal(MacroStepKind.Key, back.Steps[0].Kind, "a key");
            Assert.Equal((byte)0x45, back.Steps[0].Vk, "which key");
            Assert.Equal(1500, back.Steps[1].Ms, "the wait");
            Assert.Equal(0.5, back.Steps[2].X, "where across");
            Assert.Equal(0.3, back.Steps[2].Y, "where down");
            Assert.False(back.Steps[2].RightButton, "left");
            Assert.True(back.Steps[3].RightButton, "right");
            Assert.Equal("/e dance\tnow", back.Steps[4].Text, "text, tab and all");
            Assert.Equal(2000, back.Steps[5].HoldMs, "the hold");
        }

        public static void TestMacrosAreSavedWithTheWatchers()
        {
            string path = Path.Combine(Path.GetTempPath(), "rk_macrotest_" + Guid.NewGuid().ToString("n") + ".dat");
            try
            {
                WatchStore s = new WatchStore(path);
                s.Watchers.Add(Watcher.Default("Secret", WatchKind.ChatLine));
                s.Macros.Add(Sample());
                s.Save();

                WatchStore back = new WatchStore(path);
                back.Load();
                Assert.Equal(1, back.Macros.Count, "the macro");
                Assert.Equal(6, back.FindMacro("BUY EGG").Steps.Count, "found by name, any case, whole");
                Assert.Equal(1, back.Watchers.Count, "and the watcher beside it");
                Assert.Equal(null, back.FindMacro("nothing"), "no such macro");
            }
            finally { try { File.Delete(path); } catch { } }
        }

        public static void TestNonsenseIsNotAMacro()
        {
            Assert.Equal(null, WatchStore.DeserializeMacro(""), "empty");
            Macro m = WatchStore.DeserializeMacro("Only a name");
            Assert.Equal(0, m == null ? 0 : m.Steps.Count, "a name and no steps is at most an empty macro");
        }

        // Focus is held the whole time a macro plays, so a long one is refused
        // with the reason rather than quietly allowed.
        public static void TestAMacroSaysWhatIsWrongWithIt()
        {
            Macro m = new Macro();
            Assert.Contains("name", m.Problem(), "no name");
            m.Name = "x";
            Assert.Contains("step", m.Problem(), "no steps");
            m.Steps.Add(MacroStep.Wait(61000));
            Assert.Contains("minute", m.Problem(), "too long");
            m.Steps[0] = MacroStep.Wait(1000);
            Assert.Equal(null, m.Problem(), "fine");
        }

        public static void TestHowLongAMacroTakes()
        {
            Macro m = new Macro();
            m.Steps.Add(MacroStep.Key(0x45, 50));
            m.Steps.Add(MacroStep.Wait(1500));
            m.Steps.Add(MacroStep.Key(0x57, 2000));
            Assert.Equal(3550, m.DurationMs, "holds and waits add up");
        }

        public static void TestEachStepSaysWhatItDoes()
        {
            Assert.Equal("Press E", MacroStep.Key(0x45, 50).Describe(), "a tap");
            Assert.Equal("Hold W for 2s", MacroStep.Key(0x57, 2000).Describe(), "a hold");
            Assert.Equal("Wait 1.5s", MacroStep.Wait(1500).Describe(), "a wait");
            Assert.Equal("Click 50% across, 30% down", MacroStep.Click(0.5, 0.3, false).Describe(), "a click");
            Assert.Equal("Right-click 25% across, 75% down", MacroStep.Click(0.25, 0.75, true).Describe(), "a right click");
            Assert.Equal("Type \"/e dance\"", MacroStep.Type("/e dance").Describe(), "typing");
        }

        public static void TestKeysAreCalledWhatIsOnThem()
        {
            Assert.Equal("W", MacroKeys.Name(0x57), "a letter");
            Assert.Equal("1", MacroKeys.Name(0x31), "a digit");
            Assert.Equal("Space", MacroKeys.Name(0x20), "space");
            Assert.Equal("Enter", MacroKeys.Name(0x0D), "enter");
            Assert.Equal("Left arrow", MacroKeys.Name(0x25), "an arrow");
            Assert.Equal("F1", MacroKeys.Name(0x70), "a function key");
            Assert.Equal("/", MacroKeys.Name(0xBF), "the chat key");
        }
    }
}
