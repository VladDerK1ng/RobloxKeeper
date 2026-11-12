using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace RobloxKeeper
{
    // Game icons and friends' faces: fetched once, kept on disk, and a few
    // dozen held in memory while a page is showing them.
    //
    // The memory side is small on purpose - this app sits beside several
    // Roblox clients - and is emptied when the window goes to the tray.
    // Pictures only ever come from Roblox's own picture servers.
    static class Thumbs
    {
        // About what two pages of cards show. A 150x150 icon is 90KB decoded.
        public const int MAX_IN_MEMORY = 96;

        // An icon changes when a game's developer changes it; a week-old copy
        // is asked for again.
        static readonly TimeSpan KEEP_ON_DISK = TimeSpan.FromDays(7);
        const int AT_ONCE = 4;
        const int MAX_BYTES = 2 * 1024 * 1024;

        static readonly object gate = new object();
        static readonly Dictionary<string, Bitmap> memory = new Dictionary<string, Bitmap>();
        static readonly List<string> order = new List<string>();          // least lately used first
        static readonly List<string> fetching = new List<string>();
        static readonly Dictionary<string, DateTime> failed = new Dictionary<string, DateTime>();
        static readonly Queue<KeyValuePair<string, Action>> waiting = new Queue<KeyValuePair<string, Action>>();
        static int running;

        public static string Folder
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "RobloxKeeper", "cache", "thumbs");
            }
        }

        // Roblox serves its pictures from rbxcdn.com. Anywhere else is not a
        // picture this app asked for.
        public static bool IsRobloxPicture(string url)
        {
            Uri u;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            return u.Scheme == Uri.UriSchemeHttps && u.Host.EndsWith(".rbxcdn.com", StringComparison.OrdinalIgnoreCase);
        }

        // A file name for an address: its hash, so nothing in the address can
        // name a path.
        public static string FileFor(string url)
        {
            using (SHA1 sha = SHA1.Create())
            {
                StringBuilder sb = new StringBuilder();
                foreach (byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(url ?? ""))) sb.Append(b.ToString("x2"));
                return sb.ToString() + ".png";
            }
        }

        // The picture if it is in memory; otherwise null, and it is fetched
        // in the background - arrived is called (on a worker thread) once it
        // is there to ask for again. Never blocks, never throws.
        public static Bitmap Get(string url, Action arrived)
        {
            if (!IsRobloxPicture(url)) return null;
            lock (gate)
            {
                Bitmap b;
                if (memory.TryGetValue(url, out b))
                {
                    order.Remove(url);
                    order.Add(url);
                    return b;
                }
                DateTime when;
                // One that failed is not asked for again on every repaint.
                if (failed.TryGetValue(url, out when) && DateTime.UtcNow - when < TimeSpan.FromMinutes(2)) return null;
                if (fetching.Contains(url)) return null;
                fetching.Add(url);
                waiting.Enqueue(new KeyValuePair<string, Action>(url, arrived));
                Pump();
            }
            return null;
        }

        // Called with the gate held.
        static void Pump()
        {
            while (running < AT_ONCE && waiting.Count > 0)
            {
                KeyValuePair<string, Action> next = waiting.Dequeue();
                running++;
                ThreadPool.QueueUserWorkItem(delegate { Fetch(next.Key, next.Value); });
            }
        }

        static void Fetch(string url, Action arrived)
        {
            Bitmap b = null;
            try { b = Load(url); } catch { }
            lock (gate)
            {
                fetching.Remove(url);
                running--;
                if (b == null) failed[url] = DateTime.UtcNow;
                else
                {
                    memory[url] = b;
                    order.Add(url);
                    // The ones looked at longest ago go. They are not
                    // disposed: a page may be half-way through drawing one.
                    while (order.Count > MAX_IN_MEMORY)
                    {
                        memory.Remove(order[0]);
                        order.RemoveAt(0);
                    }
                }
                Pump();
            }
            if (b != null && arrived != null) try { arrived(); } catch { }
        }

        static Bitmap Load(string url)
        {
            string path = Path.Combine(Folder, FileFor(url));
            byte[] bytes = null;
            if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < KEEP_ON_DISK)
                bytes = File.ReadAllBytes(path);
            if (bytes == null)
            {
                bytes = Download(url);
                if (bytes == null) return null;
                try
                {
                    Directory.CreateDirectory(Folder);
                    File.WriteAllBytes(path, bytes);
                }
                catch { }       // a full disk is no reason not to show it
            }
            // Decoded from memory and copied: a Bitmap made on a stream needs
            // that stream for as long as it lives.
            using (MemoryStream ms = new MemoryStream(bytes))
            using (Bitmap decoded = new Bitmap(ms))
                return new Bitmap(decoded);
        }

        static byte[] Download(string url)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "RobloxKeeper";
            req.Timeout = 15000;
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            using (Stream s = res.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                // Sent on somewhere that is not Roblox's picture server:
                // whatever is there, it is not read.
                if (!IsRobloxPicture(res.ResponseUri.AbsoluteUri)) return null;
                byte[] buffer = new byte[8192];
                int n;
                while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, n);
                    if (ms.Length > MAX_BYTES) return null;     // not an icon
                }
                return ms.ToArray();
            }
        }

        // The window has gone to the tray: nothing is looking at them.
        public static void ForgetMemory()
        {
            lock (gate)
            {
                memory.Clear();
                order.Clear();
            }
        }

        public static int InMemory { get { lock (gate) return memory.Count; } }

        // Pictures nobody has asked for in a month, removed from disk.
        public static void TidyDisk()
        {
            try
            {
                if (!Directory.Exists(Folder)) return;
                foreach (string f in Directory.GetFiles(Folder, "*.png"))
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromDays(30))
                        try { File.Delete(f); } catch { }
            }
            catch { }
        }
    }
}
