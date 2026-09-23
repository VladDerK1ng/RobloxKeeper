using System;
using System.Collections.Generic;
using System.IO;

namespace RobloxKeeper
{
    // Follows Roblox's client logs and reports what actually happened.
    //
    // Roblox writes the real reason a client dropped and then shows the user a
    // dialog that does not. This turns "it just disconnected" into "kicked for
    // being idle" or "Roblox saw this account on another device", which are
    // different problems with different fixes.
    //
    // Only new lines are ever read. On first sight of a file it seeks to the
    // end, so starting the app does not replay hours of history, and each file
    // is read from where it was left off rather than re-scanned - the logs from
    // a long session run to tens of megabytes.
    class RobloxLogWatch
    {
        // A file that has not been written to in this long is finished with.
        const int STALE_SECONDS = 120;

        public Action<string> Log;

        // Which client a log belongs to, when that is known. Optional.
        public Func<string, string> NameForLog;

        // A client joined a game: the log it is in, and the join. Also raised
        // once for every log already open when watching starts, with the last
        // join in it - so a client that was in a game before the app started
        // is known too, without that history being replayed into the log.
        public Action<string, RobloxLogEvent> Joined;

        // Which account a client is, when Roblox says so in a later read than
        // the join it belongs to. Optional.
        public Action<string, RobloxLogEvent> Identified;

        // How far past a join to look for the line naming the account. It is
        // the very next line in every log measured; a little slack costs
        // nothing.
        const int WHO_LOOKAHEAD = 5;

        readonly Dictionary<string, long> offsets = new Dictionary<string, long>();
        readonly string dir;

        // When watching began. A log that already existed then holds joins
        // that happened before; one created since is a client started since,
        // and everything in it is news.
        public DateTime Started = DateTime.Now;

        public RobloxLogWatch() : this(RobloxLog.LogsDir) { }
        public RobloxLogWatch(string dir) { this.dir = dir; }

        public void Tick()
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                DateTime cutoff = DateTime.Now.AddSeconds(-STALE_SECONDS);

                foreach (string path in Directory.GetFiles(dir, "*_Player_*.log"))
                {
                    FileInfo f = new FileInfo(path);
                    if (f.LastWriteTime < cutoff) continue;
                    ReadNew(f);
                }
            }
            catch { }   // logs are Roblox's, and being unable to read them is not our problem
        }

        void ReadNew(FileInfo f)
        {
            long from;
            bool known = offsets.TryGetValue(f.FullName, out from);

            // First sight: start at the end. Anything already written happened
            // before the app was watching and is not news - except where the
            // client is now, which the watchers need to know.
            if (!known)
            {
                offsets[f.FullName] = f.Length;
                AnnounceLastJoin(f);
                return;
            }

            // Roblox rotates a log by truncating it; start over rather than
            // seeking past the end.
            if (f.Length < from) from = 0;
            if (f.Length == from) return;

            try
            {
                using (FileStream fs = new FileStream(f.FullName, FileMode.Open,
                           FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(from, SeekOrigin.Begin);
                    List<string> lines = new List<string>();
                    using (StreamReader r = new StreamReader(fs))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null) lines.Add(line);
                        offsets[f.FullName] = fs.Position;
                    }
                    ReportAll(f.Name, lines);
                }
            }
            catch
            {
                // Locked mid-write; try again on the next tick from the same
                // place rather than losing our position.
            }
        }

        // The last join already in a log, told once when the log is first seen.
        // Read line by line and only parsed where a join could be, because a
        // long session's log runs to tens of megabytes.
        void AnnounceLastJoin(FileInfo f)
        {
            if (Joined == null) return;

            RobloxLogEvent last = new RobloxLogEvent();
            bool found = false;
            try
            {
                using (FileStream fs = new FileStream(f.FullName, FileMode.Open,
                           FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader r = new StreamReader(fs))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        bool join = line.IndexOf("Joining game", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool who = !join && found && line.IndexOf("game_join_loadtime", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!join && !who) continue;
                        RobloxLogEvent e = RobloxLog.Parse(line);
                        if (e.Type == RobloxLogEvent.Kind.Joined) { last = e; found = true; }
                        else if (e.Type == RobloxLogEvent.Kind.Identified) last.UserId = e.UserId;
                    }
                }
            }
            catch { return; }   // locked or gone; the next join will be seen as it happens

            if (!found) return;
            last.Earlier = f.CreationTime < Started;
            Joined(f.Name, last);
        }

        // A batch of new lines. A join takes who joined from the lines just
        // after it, when they are already here; the line naming the account
        // is then not told a second time.
        void ReportAll(string fileName, List<string> lines)
        {
            List<RobloxLogEvent> events = new List<RobloxLogEvent>();
            foreach (string line in lines) events.Add(RobloxLog.Parse(line));

            for (int i = 0; i < events.Count; i++)
            {
                RobloxLogEvent e = events[i];
                if (e.Type == RobloxLogEvent.Kind.Joined)
                {
                    for (int k = i + 1; k < events.Count && k <= i + WHO_LOOKAHEAD; k++)
                    {
                        if (events[k].Type == RobloxLogEvent.Kind.Joined) break;
                        if (events[k].Type != RobloxLogEvent.Kind.Identified) continue;
                        e.UserId = events[k].UserId;
                        RobloxLogEvent told = new RobloxLogEvent();
                        events[k] = told;                  // Kind.None: already said
                        break;
                    }
                }
                Report(fileName, e);
            }
        }

        void Report(string fileName, RobloxLogEvent e)
        {
            if (e.Type == RobloxLogEvent.Kind.None) return;

            // Told to the app before the activity list, so the line can use
            // the name the app has just given the client.
            if (e.Type == RobloxLogEvent.Kind.Joined && Joined != null) Joined(fileName, e);
            if (e.Type == RobloxLogEvent.Kind.Identified)
            {
                if (Identified != null) Identified(fileName, e);
                return;
            }

            string who = null;
            if (NameForLog != null) { try { who = NameForLog(fileName); } catch { } }
            string prefix = string.IsNullOrEmpty(who) ? "A client " : who + " ";

            switch (e.Type)
            {
                case RobloxLogEvent.Kind.Joined:
                    Log(prefix + "joined place " + e.PlaceId + ".");
                    break;

                case RobloxLogEvent.Kind.Hung:
                    Log(prefix + "stopped responding and Roblox closed it. That's a hang, "
                        + "not a disconnect - nothing on your end caused it.");
                    break;

                case RobloxLogEvent.Kind.Disconnected:
                    // Leaving on purpose is not worth a line; every closed
                    // client would produce one.
                    if (RobloxLog.IsNormalExit(e.Reason)) break;
                    Log(prefix + "was disconnected. " + RobloxLog.Explain(e.Reason));
                    break;
            }
        }
    }
}
