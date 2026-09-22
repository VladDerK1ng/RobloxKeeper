using System;
using System.Collections.Generic;

namespace RobloxKeeper.Tests
{
    // Start with Windows, the way Wallpaper Engine's high-priority start
    // does it: a Task Scheduler task that runs the moment you sign in,
    // instead of the Run list, which Windows holds back until the desktop has
    // loaded. What the task says, and when the app registers, moves or
    // removes it, are tested here against a pretend Task Scheduler.
    static class AutostartTests
    {
        const string User = @"DESKTOP-CFRRFA6\Vlad";
        const string Exe = @"C:\antiafk\RobloxKeeper.exe";

        // ---------- the task ----------

        public static void TestTheTaskRunsAtSignInForThisUser()
        {
            string xml = Autostart.TaskXml(User, Exe);
            Assert.Contains("<LogonTrigger>", xml, "at sign-in");
            Assert.Contains("<UserId>" + User.Replace(@"\", @"\") + "</UserId>", xml, "for this user");
            Assert.Contains("<RunLevel>LeastPrivilege</RunLevel>", xml, "without administrator rights");
            Assert.False(xml.Contains("<Delay>"), "and no delay");
        }

        public static void TestTheTaskStartsTheAppInTheTray()
        {
            string xml = Autostart.TaskXml(User, Exe);
            Assert.Contains("<Command>" + Exe + "</Command>", xml, "this exe");
            Assert.Contains("<Arguments>--minimized</Arguments>", xml, "straight to the tray");
        }

        // Task Scheduler's own defaults would undo the point of it.
        public static void TestTheTaskIsNeitherStoppedNorSlowedDown()
        {
            string xml = Autostart.TaskXml(User, Exe);
            Assert.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", xml, "not stopped after three days");
            Assert.Contains("<Priority>4</Priority>", xml, "normal priority, not below normal");
            Assert.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>", xml, "starts on battery");
            Assert.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>", xml, "and keeps going on it");
            Assert.Contains("<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>", xml, "one at a time");
        }

        public static void TestAPathWithAnAmpersandIsStillAValidTask()
        {
            string xml = Autostart.TaskXml(User, @"C:\Games & Tools\RobloxKeeper.exe");
            Assert.Contains(@"<Command>C:\Games &amp; Tools\RobloxKeeper.exe</Command>", xml, "escaped");
            Assert.Equal(@"C:\Games & Tools\RobloxKeeper.exe", Autostart.CommandIn(xml), "and read back as it was");
        }

        public static void TestTheExeIsReadBackFromATask()
        {
            Assert.Equal(Exe, Autostart.CommandIn(Autostart.TaskXml(User, Exe)), "what it starts");
            Assert.Equal(null, Autostart.CommandIn(null), "no task");
            Assert.Equal(null, Autostart.CommandIn("<Task/>"), "a task with nothing to run");
        }

        // ---------- when it is registered, moved or removed ----------

        class FakeScheduler : IAutostartPlace
        {
            public string TaskExe;              // null: no task
            public string RunValue;             // null: no Run entry
            public string RefuseTask;           // set: registering fails with this
            public readonly List<string> Did = new List<string>();

            public string TaskCommand() { return TaskExe; }
            public string Register(string exe)
            {
                Did.Add("register " + exe);
                if (RefuseTask != null) return RefuseTask;
                TaskExe = exe;
                return null;
            }
            public void RemoveTask() { Did.Add("remove task"); TaskExe = null; }
            public string RunEntry() { return RunValue; }
            public void SetRunEntry(string exe) { Did.Add("run entry " + exe); RunValue = "\"" + exe + "\" --minimized"; }
            public void RemoveRunEntry() { Did.Add("remove run entry"); RunValue = null; }
        }

        static readonly Func<string, bool> Exists = delegate(string p) { return true; };

        // Already switched on the old way: moved to the fast way, and the old
        // entry removed so it doesn't start twice.
        public static void TestAnOldRunEntryIsMovedToTheFastStart()
        {
            FakeScheduler s = new FakeScheduler();
            s.RunValue = "\"" + Exe + "\" --minimized";
            string said;
            Assert.True(Autostart.Check(s, Exe, Exists, out said), "still on");
            Assert.Equal(Exe, s.TaskExe, "now a task");
            Assert.Equal(null, s.RunValue, "and the old entry gone");
            Assert.Contains("as soon as you sign in", said, "and says so");
        }

        // If Task Scheduler says no, the old way keeps working.
        public static void TestIfTheTaskCantBeMadeTheOldEntryStays()
        {
            FakeScheduler s = new FakeScheduler();
            s.RunValue = "\"" + Exe + "\" --minimized";
            s.RefuseTask = "Access is denied";
            string said;
            Assert.True(Autostart.Check(s, Exe, Exists, out said), "still on");
            Assert.True(s.RunValue != null, "the old entry kept");
            Assert.Equal(null, said, "nothing to say at every start");
        }

        // The owner's autostart pointed at a test copy in a work folder,
        // because every start pointed it at whichever copy was running. It is
        // only moved when the copy it points at is gone.
        public static void TestAnotherCopyRunningNeverTakesOverTheStart()
        {
            FakeScheduler s = new FakeScheduler();
            s.TaskExe = Exe;
            string said;
            Autostart.Check(s, @"C:\somewhere\else\RobloxKeeper.exe", Exists, out said);
            Assert.Equal(Exe, s.TaskExe, "left pointing where it was");
            Assert.Equal(0, s.Did.Count, "nothing done");
        }

        public static void TestAStartPointingAtAMissingCopyIsMovedToThisOne()
        {
            FakeScheduler s = new FakeScheduler();
            s.TaskExe = @"C:\old\RobloxKeeper.exe";
            string said;
            Autostart.Check(s, Exe, delegate(string p) { return p != @"C:\old\RobloxKeeper.exe"; }, out said);
            Assert.Equal(Exe, s.TaskExe, "moved to the copy that exists");
        }

        public static void TestNothingSwitchedOnStaysOff()
        {
            FakeScheduler s = new FakeScheduler();
            string said;
            Assert.False(Autostart.Check(s, Exe, Exists, out said), "off");
            Assert.Equal(0, s.Did.Count, "nothing done");
        }

        public static void TestSwitchingOnMakesTheTaskAndSwitchingOffRemovesEverything()
        {
            FakeScheduler s = new FakeScheduler();
            Assert.Equal(null, Autostart.SwitchOn(s, Exe), "on");
            Assert.Equal(Exe, s.TaskExe, "as a task");
            Assert.Equal(null, s.RunValue, "not also a Run entry");

            s.RunValue = "\"" + Exe + "\" --minimized";
            Autostart.SwitchOff(s);
            Assert.Equal(null, s.TaskExe, "task gone");
            Assert.Equal(null, s.RunValue, "and any Run entry");
        }

        public static void TestSwitchingOnFallsBackToTheRunEntry()
        {
            FakeScheduler s = new FakeScheduler();
            s.RefuseTask = "Access is denied";
            string said = Autostart.SwitchOn(s, Exe);
            Assert.True(s.RunValue != null, "started the old way instead");
            Assert.Contains("Access is denied", said, "and says why");
        }

        // ---------- a second copy at sign-in ----------

        // Started twice at sign-in (an old Run entry and the task), the
        // second copy used to pop the first one's window open. Started
        // minimized, it just leaves.
        public static void TestASecondCopyStartedMinimizedDoesNotShowTheWindow()
        {
            Assert.False(Program.ShouldShowExisting(new string[] { "RobloxKeeper.exe", "--minimized" }), "from autostart");
            Assert.True(Program.ShouldShowExisting(new string[] { "RobloxKeeper.exe" }), "opened by hand: shown");
        }
    }
}
