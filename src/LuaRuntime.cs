using System;
using System.IO;
using System.Reflection;

namespace RobloxKeeper
{
    // Lets the app ship as one file while still running Lua scripts.
    //
    // Scripts run in MoonSharp, a Lua interpreter written in C#. Shipping its
    // DLL beside the exe would turn the single download into two files, so it
    // is embedded as a resource - the way the WebView2 libraries are - and
    // handed to .NET straight from memory the first time a script needs it.
    // Nothing is unpacked to disk: it is a plain managed library with no
    // native part, so there is nothing that has to be a file.
    //
    // The resolver must be in place before anything that mentions a MoonSharp
    // type is JITted. Program.Main is not where it is installed: the main
    // window installs it first thing, and every class that touches MoonSharp
    // (LuaRun) is only ever reached from a script being started, long after.
    // The test runner installs it first thing too, before it looks at a type.
    // This class itself mentions no MoonSharp type, so calling it is safe from
    // anywhere.
    static class LuaRuntime
    {
        public const string NAME = "MoonSharp.Interpreter";
        const string RESOURCE = "MoonSharp.Interpreter.dll";

        static readonly object gate = new object();
        static bool installed;
        // Loaded once and kept: loading the same bytes twice gives two copies
        // of every type, and a script made with one would not be the other's.
        static Assembly loaded;

        public static string LastError { get; private set; }

        // Cheap after the first call. True once the resolver is in place.
        public static bool Ensure()
        {
            lock (gate)
            {
                if (installed) return true;
                try
                {
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
        }

        // Whether the interpreter is packed into this exe, for a plain line
        // when a script can't start rather than a crash.
        public static bool Packed
        {
            get
            {
                try
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(RESOURCE))
                        return s != null;
                }
                catch { return false; }
            }
        }

        static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            try
            {
                if (new AssemblyName(args.Name).Name != NAME) return null;
                lock (gate)
                {
                    if (loaded != null) return loaded;
                    using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream(RESOURCE))
                    {
                        if (src == null) { LastError = "the Lua interpreter is missing from this build"; return null; }
                        using (MemoryStream ms = new MemoryStream())
                        {
                            src.CopyTo(ms);
                            loaded = Assembly.Load(ms.ToArray());
                        }
                    }
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }
    }
}
