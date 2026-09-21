using System;

namespace RobloxKeeper
{
    enum WatchKind
    {
        // A word turns up somewhere in a region. Fires once on arrival.
        TextAppears,

        // A line appeared in a scrolling region that was not there before.
        // Fires per line and carries the whole line, because what you want to
        // act on is the message, not the word that caught it.
        ChatLine,

        // A cropped picture turns up in a region. Fires once on arrival.
        ImageFound
    }

    // One thing being watched for, on some clients, in some part of the window.
    class Watcher
    {
        public string Name;
        public WatchKind Kind;
        public bool Enabled = true;

        // Null means the whole window, which needs no setup but costs 89ms a
        // scan instead of 10 and so drops that client to about one scan a
        // second.
        public string RegionName;

        // Null or empty means every client. Otherwise account names, because
        // "Client 3" is whichever window happens to be third right now, and
        // closing the second one would silently move this to another account.
        public string[] Accounts;

        public MatchRule Rule = new MatchRule();
        public string ChatContains;          // blank means every new line

        public string TemplateFile;
        public string MaskFile;
        public double Tolerance = 0.82;

        public int ConfirmScans = 2;
        public int CooldownSeconds = 30;

        public bool SendDiscord = true;
        public bool ShowTray = true;
        public bool PlaySound;
        public bool WriteLog = true;

        // Blank uses the one webhook in settings. Set per watcher only when
        // somebody actually wants two channels.
        public string WebhookUrl;

        public bool WholeWindow { get { return string.IsNullOrEmpty(RegionName); } }

        public static Watcher Default(string name, WatchKind kind)
        {
            Watcher w = new Watcher();
            w.Name = name;
            w.Kind = kind;
            return w;
        }

        public bool RunsOn(string accountName)
        {
            if (Accounts == null || Accounts.Length == 0) return true;

            // A client we cannot name is never assumed to be one of the named
            // ones. Guessing here would put a watcher on the wrong account.
            if (string.IsNullOrEmpty(accountName)) return false;

            foreach (string a in Accounts)
                if (string.Equals(a, accountName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // A fresh one each time, because every client keeps its own count.
        // Sharing one would let two clients seeing the same thing confirm each
        // other's sightings.
        public FireControl NewFireControl()
        {
            return new FireControl(ConfirmScans, TimeSpan.FromSeconds(CooldownSeconds));
        }
    }

    // What happened, on which client.
    //
    // Everything downstream - the Discord post, the tray balloon, the activity
    // log, and later the macros - consumes this one shape. That is what lets
    // "just watch" and "press this when it appears" be the same system with a
    // different tail.
    class DetectionEvent
    {
        public string WatcherName;
        public string AccountName;     // null for a client we did not launch
        public string ClientLabel;     // "Client 3"
        public string PlaceId;
        public string JobId;
        public string Matched;         // the word, or the template's name
        public string Line;            // the whole chat line, for a chat watcher
        public double Score;           // for an image watcher
        public DateTime When = DateTime.Now;
        public Pixels Crop;            // what it saw, for the attachment

        public string JoinLink { get { return RobloxLog.JoinLink(PlaceId, JobId); } }

        // One line saying what was found and where. Names both the window and
        // the account: the number is how you find it on screen, the name is
        // how you know whose it is.
        public string Headline()
        {
            string who = string.IsNullOrEmpty(AccountName)
                ? ClientLabel
                : ClientLabel + " - " + AccountName;
            return WatcherName + " found on " + who;
        }
    }
}
