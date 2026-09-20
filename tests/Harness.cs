using System;
using System.Collections.Generic;
using System.Reflection;

namespace RobloxKeeper.Tests
{
    // A test runner small enough to need no dependencies, because the project
    // has none and a test harness is a bad place to acquire the first one. It
    // compiles with the same csc.exe as build.bat.
    //
    // Any public static void method named Test* on any type in this namespace
    // is a test. A test fails by throwing.
    static class Harness
    {
        static int passed, failed;
        static readonly List<string> failures = new List<string>();

        [STAThread]
        public static int Main()
        {
            Console.WriteLine("RobloxKeeper tests");
            Console.WriteLine("------------------");

            foreach (Type t in LoadableTypes())
            {
                if (t.Namespace != "RobloxKeeper.Tests") continue;
                if (t == typeof(Harness) || t == typeof(Assert)) continue;

                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!m.Name.StartsWith("Test") || m.GetParameters().Length != 0) continue;
                    Run(t.Name + "." + m.Name, m);
                }
            }

            Console.WriteLine();
            foreach (string f in failures) Console.WriteLine(f);
            Console.WriteLine();
            Console.WriteLine(failed == 0
                ? "OK - " + passed + " passed"
                : "FAILED - " + failed + " failed, " + passed + " passed");
            return failed == 0 ? 0 : 1;
        }

        // Every type the runner can actually see.
        //
        // The account manager's login window references WebView2, whose
        // assemblies live embedded in RobloxKeeper.exe and are unpacked at
        // runtime. The test runner is a different executable that never opens a
        // browser, so those assemblies are not resolvable here and GetTypes()
        // throws rather than returning what it managed to load. The types it
        // did load are exactly the ones worth testing.
        static Type[] LoadableTypes()
        {
            try { return Assembly.GetExecutingAssembly().GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                List<Type> ok = new List<Type>();
                foreach (Type t in ex.Types) if (t != null) ok.Add(t);
                return ok.ToArray();
            }
        }

        static void Run(string name, MethodInfo m)
        {
            try
            {
                m.Invoke(null, null);
                passed++;
                Console.WriteLine("  pass  " + name);
            }
            catch (TargetInvocationException ex)
            {
                failed++;
                Exception inner = ex.InnerException ?? ex;
                Console.WriteLine("  FAIL  " + name);
                failures.Add("FAIL " + name + Environment.NewLine + "     " + inner.Message);
            }
        }
    }

    static class Assert
    {
        public static void True(bool condition, string what)
        {
            if (!condition) throw new Exception("expected true: " + what);
        }

        public static void False(bool condition, string what)
        {
            if (condition) throw new Exception("expected false: " + what);
        }

        public static void Equal(object expected, object actual, string what)
        {
            if (!Equals(expected, actual))
                throw new Exception(what + ": expected <" + Describe(expected) + "> but was <" + Describe(actual) + ">");
        }

        public static void NotEqual(object notExpected, object actual, string what)
        {
            if (Equals(notExpected, actual))
                throw new Exception(what + ": expected anything but <" + Describe(notExpected) + ">");
        }

        public static void Contains(string needle, string haystack, string what)
        {
            if (haystack == null || haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                throw new Exception(what + ": expected to find <" + needle + "> in <" + Describe(haystack) + ">");
        }

        public static void Throws(Action a, string what)
        {
            try { a(); }
            catch { return; }
            throw new Exception("expected an exception: " + what);
        }

        static string Describe(object o)
        {
            return o == null ? "null" : o.ToString();
        }
    }
}
