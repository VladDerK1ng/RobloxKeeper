using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RobloxKeeper
{
    // Where Start with Windows is recorded: a Task Scheduler task, and the
    // older Run-list entry. Behind this so the decisions can be tested.
    interface IAutostartPlace
    {
        string TaskCommand();               // the exe the task starts, or null for no task
        string Register(string exe);        // null when it worked, otherwise why not
        void RemoveTask();
        string RunEntry();                  // the Run-list value, or null
        void SetRunEntry(string exe);
        void RemoveRunEntry();
    }

    // Start with Windows, the way Wallpaper Engine's high-priority start does
    // it.
    //
    // The Run list is what most apps use, and Windows holds it back: it is
    // read only once the desktop has loaded, one entry after another, and by
    // default after a deliberate delay. A Task Scheduler task with a sign-in
    // trigger runs the moment you sign in, ahead of all of that - which
    // matters here, because RobloxKeeper has to hold Roblox's single-instance
    // lock before any client starts. It needs no administrator rights for
    // your own sign-in; measured on the owner's PC.
    //
    // Two of Task Scheduler's defaults would undo it and are set explicitly:
    // a task is stopped after 72 hours, and runs at below-normal priority.
    static class Autostart
    {
        public const string TaskName = "RobloxKeeper";
        public const string Arguments = "--minimized";

        public static string TaskXml(string user, string exe)
        {
            string u = SecurityElement.Escape(user), e = SecurityElement.Escape(exe);
            return
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo>\r\n" +
                "    <Description>Starts RobloxKeeper in the tray as soon as you sign in. Switch it off with Start with Windows in RobloxKeeper.</Description>\r\n" +
                "  </RegistrationInfo>\r\n" +
                "  <Triggers>\r\n" +
                "    <LogonTrigger>\r\n" +
                "      <Enabled>true</Enabled>\r\n" +
                "      <UserId>" + u + "</UserId>\r\n" +
                "    </LogonTrigger>\r\n" +
                "  </Triggers>\r\n" +
                "  <Principals>\r\n" +
                "    <Principal id=\"Author\">\r\n" +
                "      <UserId>" + u + "</UserId>\r\n" +
                "      <LogonType>InteractiveToken</LogonType>\r\n" +
                "      <RunLevel>LeastPrivilege</RunLevel>\r\n" +
                "    </Principal>\r\n" +
                "  </Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "    <Priority>4</Priority>\r\n" +
                "    <Enabled>true</Enabled>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\">\r\n" +
                "    <Exec>\r\n" +
                "      <Command>" + e + "</Command>\r\n" +
                "      <Arguments>" + Arguments + "</Arguments>\r\n" +
                "    </Exec>\r\n" +
                "  </Actions>\r\n" +
                "</Task>\r\n";
        }

        // The exe a task starts, read back from its definition.
        public static string CommandIn(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            Match m = Regex.Match(xml, "<Command>([^<]*)</Command>");
            if (!m.Success) return null;
            return m.Groups[1].Value.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"")
                                    .Replace("&apos;", "'").Replace("&amp;", "&").Trim();
        }

        // The exe in a Run-list value like "C:\...\RobloxKeeper.exe" --minimized.
        public static string ExeInRunEntry(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            string v = value.Trim();
            if (v.StartsWith("\""))
            {
                int end = v.IndexOf('"', 1);
                return end > 1 ? v.Substring(1, end - 1) : null;
            }
            int space = v.IndexOf(' ');
            return space > 0 ? v.Substring(0, space) : v;
        }

        // At every start: is it switched on, and does anything need doing?
        //
        // An old Run-list entry becomes a task, and is removed so the app
        // doesn't start twice. A start pointing at a copy of the app that is
        // gone is pointed at this one. And that is the only time it is moved:
        // moving it to whichever copy was running let a test copy in a work
        // folder take over the owner's Start with Windows.
        public static bool Check(IAutostartPlace p, string exe, Func<string, bool> exists, out string said)
        {
            said = null;
            string taskExe = p.TaskCommand();
            string run = p.RunEntry();

            if (taskExe != null)
            {
                if (run != null) p.RemoveRunEntry();        // one start is enough
                if (!exists(taskExe) && p.Register(exe) == null)
                    said = "Start with Windows pointed at a copy of RobloxKeeper that's gone - it now starts this one.";
                return true;
            }

            if (run != null)
            {
                string runExe = ExeInRunEntry(run);
                bool there = runExe != null && exists(runExe);
                if (p.Register(there ? runExe : exe) == null)
                {
                    p.RemoveRunEntry();
                    said = "Start with Windows now starts RobloxKeeper as soon as you sign in, ahead of other startup apps.";
                }
                else if (!there) p.SetRunEntry(exe);        // the old way, at least pointing at a copy that exists
                return true;
            }
            return false;
        }

        // Null when it is on the fast way; otherwise why not - and then it is
        // on the old way, which still works.
        public static string SwitchOn(IAutostartPlace p, string exe)
        {
            string why = p.Register(exe);
            if (why == null)
            {
                if (p.RunEntry() != null) p.RemoveRunEntry();
                return null;
            }
            p.SetRunEntry(exe);
            return why;
        }

        public static void SwitchOff(IAutostartPlace p)
        {
            p.RemoveTask();
            if (p.RunEntry() != null) p.RemoveRunEntry();
        }
    }

    // The real thing: Windows' Task Scheduler, spoken to in-process - no
    // schtasks window flashing up at sign-in - and the Run list.
    class LiveAutostart : IAutostartPlace
    {
        const string RUN_KEY = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        const int TASK_CREATE_OR_UPDATE = 6;
        const int TASK_LOGON_INTERACTIVE_TOKEN = 3;

        readonly string name;

        public LiveAutostart() : this(Autostart.TaskName) { }
        public LiveAutostart(string taskName) { name = taskName; }

        static object Call(object o, string method, params object[] args)
        {
            return o.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, o, args);
        }

        static object Folder()
        {
            object svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            Call(svc, "Connect");
            return Call(svc, "GetFolder", "\\");
        }

        public string TaskCommand()
        {
            try
            {
                object task = Call(Folder(), "GetTask", "\\" + name);
                string xml = (string)task.GetType().InvokeMember("Xml", BindingFlags.GetProperty, null, task, null);
                return Autostart.CommandIn(xml);
            }
            catch { return null; }      // no such task, or no Task Scheduler
        }

        public string Register(string exe)
        {
            try
            {
                string user = WindowsIdentity.GetCurrent().Name;
                Call(Folder(), "RegisterTask", name, Autostart.TaskXml(user, exe), TASK_CREATE_OR_UPDATE,
                     null, null, TASK_LOGON_INTERACTIVE_TOKEN, null);
                // Read back, rather than trusting it went in.
                string back = TaskCommand();
                if (!string.Equals(back, exe, StringComparison.OrdinalIgnoreCase))
                    return "Task Scheduler didn't keep it";
                return null;
            }
            catch (TargetInvocationException ex)
            {
                return ex.InnerException != null ? ex.InnerException.Message.Trim() : ex.Message;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public void RemoveTask()
        {
            try { Call(Folder(), "DeleteTask", name, 0); } catch { }    // not there
        }

        public string RunEntry()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, false))
                    return k == null ? null : k.GetValue(name) as string;
            }
            catch { return null; }
        }

        public void SetRunEntry(string exe)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RUN_KEY))
                k.SetValue(name, "\"" + exe + "\" " + Autostart.Arguments);
        }

        public void RemoveRunEntry()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                if (k != null) k.DeleteValue(name, false);
        }
    }
}
