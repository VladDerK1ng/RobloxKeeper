using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    static class Program
    {
        const string APP_MUTEX = "RobloxKeeper_SingleInstance_7C41A9E2";
        static readonly IntPtr HWND_BROADCAST = (IntPtr)0xFFFF;
        const int ASFW_ANY = -1;

        public static readonly uint WM_SHOWME = Native.RegisterWindowMessage("RobloxKeeper_ShowExistingWindow");

        static Mutex appMutex;
        public static bool StartMinimized;

        [STAThread]
        static void Main()
        {
            // First, before anything else runs. WebView2's assemblies live
            // inside this exe, and the resolver that finds them has to be in
            // place before any method mentioning a WebView2 type is JITted.
            WebView2Runtime.Install();

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
                if (args[i] == "--minimized") StartMinimized = true;

            // Started by an update: the copy it replaced is still closing.
            // Waited for, or this one would find it running and step aside.
            int after = WaitForPid(args);
            if (after > 0)
            {
                try { using (Process old = Process.GetProcessById(after)) old.WaitForExit(30000); }
                catch { }   // already gone
            }

            // Single instance: a second launch surfaces the running window and quits.
            // This runs before any Roblox mutex work, so the live instance is untouched.
            bool createdNew;
            appMutex = new Mutex(true, APP_MUTEX, out createdNew);
            if (!createdNew)
            {
                if (ShouldShowExisting(args))
                {
                    Native.AllowSetForegroundWindow(ASFW_ANY);
                    Native.PostMessage(HWND_BROADCAST, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                }
                return;
            }

            // The copy an update or a build moved aside, once it has stopped.
            SelfSwap.CleanUp(Application.ExecutablePath);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            GC.KeepAlive(appMutex);
        }

        // A second copy opened by hand brings the running one forward. One
        // started minimized - at sign-in, say, by a leftover Run-list entry as
        // well as the task - just leaves, instead of popping the window open.
        // "--after <pid>": the process id of the copy an update replaced.
        public static int WaitForPid(string[] args)
        {
            for (int i = 1; i + 1 < args.Length; i++)
            {
                int pid;
                if (args[i] == "--after" && int.TryParse(args[i + 1], out pid) && pid > 0) return pid;
            }
            return 0;
        }

        public static bool ShouldShowExisting(string[] args)
        {
            for (int i = 1; i < args.Length; i++)
                if (args[i] == "--minimized") return false;
            return true;
        }
    }
}
