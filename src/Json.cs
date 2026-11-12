using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RobloxKeeper
{
    // Reading what Roblox's website answers with.
    //
    // The rest of the app reads Roblox's answers with a few tested patterns,
    // and for one number out of one answer that is the least there could be.
    // The launcher's pages read lists of games inside lists of sorts, where a
    // name can hold a quote, a brace or another game's field names - so those
    // are read properly. Small, and with nothing to install.
    //
    // An object is a Dictionary, a list a List, text a string, a number a
    // double, true and false a bool, and null is null. Anything that isn't
    // well formed reads as null rather than throwing: an answer that can't be
    // read is an ordinary thing to happen, not a fault.
    static class Json
    {
        public static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                int at = 0;
                object v = Value(text, ref at, 0);
                Space(text, ref at);
                return at == text.Length ? v : null;
            }
            catch { return null; }
        }

        // ---------- getting at what was read ----------

        public static object Get(object o, string name)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(name, out v) ? v : null;
        }

        // Never null: something that isn't a list is an empty one.
        public static List<object> List(object o)
        {
            return o as List<object> ?? new List<object>();
        }

        public static List<object> List(object o, string name) { return List(Get(o, name)); }

        public static string Str(object o, string name)
        {
            object v = Get(o, name);
            if (v is string) return (string)v;
            // Roblox writes ids as numbers; they are kept as text everywhere here.
            if (v is double) return Whole((double)v);
            return null;
        }

        public static long Num(object o, string name)
        {
            object v = Get(o, name);
            if (v is double) return (long)(double)v;
            long n;
            return v is string && long.TryParse((string)v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        public static bool Flag(object o, string name)
        {
            object v = Get(o, name);
            return v is bool && (bool)v;
        }

        static string Whole(double d)
        {
            return d == Math.Floor(d) && Math.Abs(d) < 1e18
                ? ((long)d).ToString(CultureInfo.InvariantCulture)
                : d.ToString("R", CultureInfo.InvariantCulture);
        }

        // ---------- reading ----------

        // Deeper than anything Roblox sends; a file made to be this deep is
        // refused rather than followed down.
        const int MAX_DEPTH = 64;

        static void Space(string s, ref int at)
        {
            while (at < s.Length && (s[at] == ' ' || s[at] == '\t' || s[at] == '\r' || s[at] == '\n')) at++;
        }

        static object Value(string s, ref int at, int depth)
        {
            if (depth > MAX_DEPTH) throw new FormatException();
            Space(s, ref at);
            if (at >= s.Length) throw new FormatException();
            char c = s[at];
            if (c == '{') return Object(s, ref at, depth);
            if (c == '[') return Array(s, ref at, depth);
            if (c == '"') return Text(s, ref at);
            if (Word(s, ref at, "true")) return true;
            if (Word(s, ref at, "false")) return false;
            if (Word(s, ref at, "null")) return null;
            return Number(s, ref at);
        }

        static bool Word(string s, ref int at, string word)
        {
            if (string.CompareOrdinal(s, at, word, 0, word.Length) != 0) return false;
            at += word.Length;
            return true;
        }

        static object Object(string s, ref int at, int depth)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            at++;
            Space(s, ref at);
            if (at < s.Length && s[at] == '}') { at++; return d; }
            while (true)
            {
                Space(s, ref at);
                if (at >= s.Length || s[at] != '"') throw new FormatException();
                string name = Text(s, ref at);
                Space(s, ref at);
                if (at >= s.Length || s[at] != ':') throw new FormatException();
                at++;
                d[name] = Value(s, ref at, depth + 1);
                Space(s, ref at);
                if (at >= s.Length) throw new FormatException();
                if (s[at] == ',') { at++; continue; }
                if (s[at] == '}') { at++; return d; }
                throw new FormatException();
            }
        }

        static object Array(string s, ref int at, int depth)
        {
            List<object> list = new List<object>();
            at++;
            Space(s, ref at);
            if (at < s.Length && s[at] == ']') { at++; return list; }
            while (true)
            {
                list.Add(Value(s, ref at, depth + 1));
                Space(s, ref at);
                if (at >= s.Length) throw new FormatException();
                if (s[at] == ',') { at++; continue; }
                if (s[at] == ']') { at++; return list; }
                throw new FormatException();
            }
        }

        static string Text(string s, ref int at)
        {
            StringBuilder sb = new StringBuilder();
            at++;
            while (true)
            {
                if (at >= s.Length) throw new FormatException();
                char c = s[at++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (at >= s.Length) throw new FormatException();
                char e = s[at++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (at + 4 > s.Length) throw new FormatException();
                        sb.Append((char)int.Parse(s.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        at += 4;
                        break;
                    default: sb.Append(e); break;      // \" \\ \/
                }
            }
        }

        static object Number(string s, ref int at)
        {
            int from = at;
            while (at < s.Length && (char.IsDigit(s[at]) || s[at] == '-' || s[at] == '+' || s[at] == '.' || s[at] == 'e' || s[at] == 'E')) at++;
            if (at == from) throw new FormatException();
            return double.Parse(s.Substring(from, at - from), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // ---------- writing: only what a request to Roblox needs ----------

        public static string Quote(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
