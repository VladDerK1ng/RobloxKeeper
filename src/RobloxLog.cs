using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RobloxKeeper
{
    struct RobloxLogEvent
    {
        public enum Kind { None, Joined, Disconnected, Hung, Identified }

        public Kind Type;
        public string PlaceId;

        // Which Roblox account it is, by user id. Written on the line after
        // each join - so a join carries it when both were read together, and
        // Identified carries it on its own when they weren't.
        public string UserId;

        // The server, not just the game. Roblox writes it on the same line,
        // and it is what turns "a secret egg appeared" into a link you can
        // press.
        public string JobId;
        public int Reason;

        // A join that was already in a log from before watching started: it
        // says where a client is, but it is not something that just happened.
        public bool Earlier;
    }

    // Reading what Roblox writes about itself.
    //
    // Roblox records exactly why a client dropped; the dialog it shows the user
    // does not. An idle kick and a duplicate-login eviction both come up on
    // screen as a lost connection, and they mean completely different things -
    // one says the anti-AFK never reached that client, the other says two
    // accounts are fighting over a single session. Guessing between them is
    // what made the original problem take so long to find.
    //
    // Everything matched here was taken from real logs rather than invented.
    static class RobloxLog
    {
        public static string LogsDir
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Roblox", "logs");
            }
        }

        static readonly Regex JoinRx =
            new Regex(@"! Joining game '([^']*)' place (\d+)", RegexOptions.IgnoreCase);

        // The join report names the account that joined. Not the start-up
        // line's rbxuid: measured, that is whoever the shared cookie belongs
        // to, and in the same log it named someone else.
        static readonly Regex WhoRx =
            new Regex(@"game_join_loadtime:.*\buserid:(\d+)", RegexOptions.IgnoreCase);

        // Three spellings, because the client reports a disconnect differently
        // depending on whether the server sent it, the client sent it, or the
        // client decided on its own (which is how the idle kick arrives).
        static readonly Regex ReasonRx = new Regex(
            @"(?:Disconnect reason received|Sending disconnect with reason|" +
            @"setting replicator disconnect reason to)\s*:?\s*(\d+)",
            RegexOptions.IgnoreCase);

        public static RobloxLogEvent Parse(string line)
        {
            RobloxLogEvent e = new RobloxLogEvent();
            if (string.IsNullOrEmpty(line)) return e;

            if (line.IndexOf("HangMonitor", StringComparison.OrdinalIgnoreCase) >= 0 &&
                line.IndexOf("Timeout", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                e.Type = RobloxLogEvent.Kind.Hung;
                return e;
            }

            Match m = ReasonRx.Match(line);
            if (m.Success)
            {
                e.Type = RobloxLogEvent.Kind.Disconnected;
                int r;
                if (int.TryParse(m.Groups[1].Value, out r)) e.Reason = r;
                return e;
            }

            m = JoinRx.Match(line);
            if (m.Success)
            {
                e.Type = RobloxLogEvent.Kind.Joined;
                e.JobId = m.Groups[1].Value;
                e.PlaceId = m.Groups[2].Value;
                return e;
            }

            m = WhoRx.Match(line);
            if (m.Success)
            {
                e.Type = RobloxLogEvent.Kind.Identified;
                e.UserId = m.Groups[1].Value;
            }

            return e;
        }

        // Leaving a game on purpose is not a fault and must not be reported as
        // one, or the log fills with alarming lines every time a client closes.
        public static bool IsNormalExit(int reason)
        {
            return reason == 285;   // DisconnectClientInitiated
        }

        // What the code means, in words that say what to do about it.
        public static string Explain(int reason)
        {
            switch (reason)
            {
                case 273:
                    return "Roblox saw this account signed in from another device and dropped it. "
                         + "Not your internet - this is what disconnect protection is for.";
                case 277:
                    return "Lost connection to the game server.";
                case 278:
                    return "Kicked for being idle. The anti-AFK nudge didn't reach this client - "
                         + "check it's ticked in the Clients list.";
                case 285:
                    return "Left the game.";
                case 264:
                    return "The same account joined from somewhere else.";
                case 267:
                case 268:
                    return "The game itself kicked this account.";
                default:
                    return "Roblox disconnected this client (reason " + reason + ").";
            }
        }

        // Does this log file belong to a client that started at this time?
        //
        // The name carries a UTC timestamp of when the log opened, which is a
        // moment after the process started, so a few seconds of slack is
        // correct rather than sloppy.
        public static bool LooksLikeSameSession(string fileName, DateTime startedUtc)
        {
            DateTime opened;
            if (!TimestampOf(fileName, out opened)) return false;
            double drift = (opened - startedUtc).TotalSeconds;
            return drift >= -5 && drift <= 30;
        }

        // A link that opens this exact server rather than the game in general.
        //
        // Null unless both halves are known, because a link missing the server
        // silently joins a different copy of the game - which looks exactly
        // like the feature being broken.
        //
        // Roblox's web address rather than roblox://, because this goes into a
        // Discord message and Discord only makes http and https links
        // clickable - a roblox:// link showed up as raw text.
        public static string JoinLink(string placeId, string jobId)
        {
            if (string.IsNullOrEmpty(placeId) || string.IsNullOrEmpty(jobId)) return null;
            return "https://www.roblox.com/games/start?placeId=" + placeId + "&gameInstanceId=" + jobId;
        }

        public static bool TimestampOf(string fileName, out DateTime openedUtc)
        {
            openedUtc = DateTime.MinValue;
            if (string.IsNullOrEmpty(fileName)) return false;

            Match m = Regex.Match(fileName, @"(\d{8}T\d{6})Z");
            if (!m.Success) return false;

            return DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMddTHHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out openedUtc);
        }
    }
}
