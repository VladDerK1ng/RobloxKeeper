using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RobloxKeeper
{
    // Lets the app ship as one file while still using WebView2.
    //
    // WebView2 needs three support libraries: two managed assemblies and one
    // native loader. Shipping them beside the exe would turn a single download
    // into a folder of four files, so instead they are embedded as resources
    // and unpacked to %LOCALAPPDATA%\RobloxKeeper\runtime on first use.
    //
    // The resolver must be installed before anything that mentions a WebView2
    // type is JITted, which is why Program.Main calls Install() as its first
    // action - by the time a method referencing CoreWebView2 is entered, it is
    // already too late to teach the runtime where to find it.
    //
    // The Edge WebView2 *runtime* itself is a separate thing and is not shipped
    // here: it comes with Windows 11 and with any current Edge install.
    static class WebView2Runtime
    {
        const string CORE = "Microsoft.Web.WebView2.Core.dll";
        const string WINFORMS = "Microsoft.Web.WebView2.WinForms.dll";
        const string LOADER = "WebView2Loader.dll";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetDllDirectoryW(string path);

        static bool installed;
        static string runtimeDir;

        public static string LastError { get; private set; }

        public static string Dir
        {
            get
            {
                if (runtimeDir == null)
                    runtimeDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "RobloxKeeper", "runtime");
                return runtimeDir;
            }
        }

        // Called before anything else. Cheap when the files are already there.
        public static bool Install()
        {
            if (installed) return true;
            try
            {
                Directory.CreateDirectory(Dir);
                Extract(CORE);
                Extract(WINFORMS);
                Extract(LOADER);

                // Where the native loader is looked up from.
                SetDllDirectoryW(Dir);

                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                installed = true;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        // Is the Edge WebView2 runtime present on this machine? Without it the
        // control cannot start, and that is worth saying plainly rather than
        // letting a browser window come up blank.
        public static bool RuntimeAvailable()
        {
            string[] keys =
            {
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
            };
            foreach (string k in keys)
            {
                try
                {
                    using (Microsoft.Win32.RegistryKey r = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(k))
                    {
                        if (r != null && r.GetValue("pv") != null) return true;
                    }
                }
                catch { }
            }
            return false;
        }

        static void Extract(string name)
        {
            string target = Path.Combine(Dir, name);

            using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (src == null) throw new FileNotFoundException("embedded resource missing: " + name);

                // Rewrite only when the size differs, so an upgraded build
                // replaces stale copies without rewriting on every launch.
                if (File.Exists(target))
                {
                    try { if (new FileInfo(target).Length == src.Length) return; }
                    catch { }
                }

                // Written beside and moved into place, so a half-written file
                // can never be loaded by a second instance starting at once.
                string tmp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (FileStream dst = File.Create(tmp)) src.CopyTo(dst);
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(tmp, target);
                }
                catch
                {
                    // Another instance won the race and the file is in use -
                    // which means it is already the right file.
                    try { File.Delete(tmp); } catch { }
                }
            }
        }

        static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            try
            {
                string simple = new AssemblyName(args.Name).Name + ".dll";
                if (simple != CORE && simple != WINFORMS) return null;

                string path = Path.Combine(Dir, simple);
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            }
            catch { return null; }
        }
    }
}
