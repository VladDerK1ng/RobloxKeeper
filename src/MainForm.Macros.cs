using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Sessions and the autoclicker, wired into the main window: the key that
    // stops everything, the key that switches the autoclicker, and what the
    // Macros window asks for. Also every macro's own key, macros played
    // anywhere on the screen, and the one check that keeps keys apart.
    partial class MainForm : IMacroHost, IMacroPlay
    {
        // From the Macros window: this macro, now, on these clients - one
        // after another, through the same queue a rule's macros wait in, so
        // each client is brought to the front for its turn and the stop key
        // stops them all.
        public int PlayNow(Macro m, IList<WatchedClient> on)
        {
            int sent = 0;
            if (m == null || on == null) return 0;
            // One made for the whole screen plays once on the screen, however
            // many clients were asked for: QueueMacro sends it there, and the
            // second and later find it already playing under the same tag.
            foreach (WatchedClient c in on)
            {
                if (c == null || c.Pid <= 0) continue;
                if (QueueMacro(m, c.Pid, WatchUi.ClientName(c), 1, "play:" + m.Name, false, MacroRunner.DEFAULT_TURN_MS) != null) sent++;
            }
            return sent;
        }

        // The stop key's own id with Windows. The autoclicker's key, the
        // screenshot key and the rules' keys are not Windows' shortcut keys
        // any more - see GlobalKeys - so only this one has an id.
        const int STOP_KEY_ID = 0x50F0;
        bool stopKeyHeld;
        byte stopKeyAsked;
        byte stopKeyRefused;        // said once, not once a second
        bool macroKeysListening;

        // What GlobalKeys was last asked to listen for, so the once-a-second
        // refresh asks again only when that changes - or, every so often,
        // when Windows refused the hook.
        List<int> globalKeysAsked;
        int globalKeysTriedTick;
        const int GLOBAL_KEYS_RETRY_MS = 10000;

        // The last thing said in a tray balloon about the autoclicker's key,
        // and when: pressing it again and again in Roblox says it once. A
        // macro's own key says its warnings the same way.
        string clickWarned;
        int clickWarnedTick;
        const int CLICK_WARN_EVERY_MS = 10000;

        public AutoClick Clicker { get { return settings.Clicker; } }

        public byte StopKey
        {
            get { return settings.StopKey; }
            set
            {
                if (value == 0 || value == settings.StopKey) return;
                settings.StopKey = value;
                settings.Save();
                RefreshStopKey();
                RefreshGlobalKeys();
            }
        }

        // Why a key can't be the one that stops macros, or null.
        public string StopKeyProblem(byte vk)
        {
            return KeyUseProblem(vk, KeyUse.STOP, null);
        }

        // Every key the app answers to as things stand, for the one check
        // that keeps two features off the same key.
        KeyOwners KeysNow()
        {
            KeyOwners k = new KeyOwners();
            k.StopKey = settings.StopKey;
            k.ClickerKey = settings.Clicker.Enabled ? settings.Clicker.ToggleVk : (byte)0;
            k.ShotKey = settings.ShotKey;
            foreach (Rule r in RulesNow())
                if (r != null && r.When == RuleWhen.Hotkey && r.HotkeyVk != 0 && !r.Ctrl && !r.Alt && !r.Shift)
                    k.RuleKeys.Add(new KeyValuePair<string, byte>(r.Name, r.HotkeyVk));
            if (watchStore != null)
                foreach (Macro m in watchStore.Macros)
                    if (m != null && m.Key != 0) k.Macros.Add(new KeyValuePair<string, byte>(m.Name, m.Key));
            if (started) k.Scripts.AddRange(Store.Keys());
            return k;
        }

        // Null when vk is free for usedFor (one of KeyUse's); otherwise why
        // not. editing: the macro whose own key is being chosen.
        public string KeyUseProblem(byte vk, string usedFor, string editing)
        {
            return KeyUse.Problem(vk, usedFor, editing, KeysNow());
        }

        // The Macros window saved: a macro's key may have come, gone or
        // changed, and is listened for from now - not at the next tick.
        public void MacrosSaved()
        {
            RefreshGlobalKeys();
        }

        public void SaveClicker()
        {
            settings.Clicker.Clamp();
            settings.Save();
            RefreshGlobalKeys();
        }

        public bool ClickerOn { get { return AnySession(AutoClick.TAG); } }

        // A rule may not sit on any of these keys - see RuleKeysWanted. The
        // stop key, the autoclicker's, the screenshot key, F8 and every
        // macro's own key: whatever the one check says another job has.
        bool IsMacroKey(int ruleKey)
        {
            if ((ruleKey >> 8) != 0) return false;       // with Ctrl, Alt or Shift it is a different key
            byte vk = (byte)(ruleKey & 0xFF);
            return KeyUse.Problem(vk, KeyUse.RULE, null, KeysNow()) != null;
        }

        // The macro whose own key this is, or null.
        Macro MacroOnKey(byte vk)
        {
            if (watchStore == null || vk == 0) return null;
            foreach (Macro m in watchStore.Macros)
                if (m != null && m.Key == vk) return m;
            return null;
        }

        // The stop key belongs to this app only while there is something to
        // stop, so F10 is every other program's the rest of the time.
        void RefreshStopKey()
        {
            if (!IsHandleCreated) return;
            if (!macroKeysListening)
            {
                macroKeysListening = true;
                MacroPlayer.PlayingChanged += delegate { OnUi(RefreshStopKey); };
            }

            bool want = MacroPlayer.Playing || SessionsRunning > 0 || macroQueue.Waiting > 0;
            if (stopKeyHeld && (!want || stopKeyAsked != settings.StopKey))
            {
                Native.UnregisterHotKey(Handle, STOP_KEY_ID);
                stopKeyHeld = false;
            }
            if (want && !stopKeyHeld)
            {
                stopKeyAsked = settings.StopKey;
                stopKeyHeld = Native.RegisterHotKey(Handle, STOP_KEY_ID, Native.MOD_NOREPEAT, stopKeyAsked);
                if (stopKeyHeld) stopKeyRefused = 0;
                else if (stopKeyRefused != stopKeyAsked)
                {
                    // The line that said "press F10" would otherwise be a lie.
                    stopKeyRefused = stopKeyAsked;
                    Log(MacroKeys.Name(stopKeyAsked) + " is already used by another program, so it can't stop macros. "
                        + "Stop them from the tray icon, or choose another stop key in Macros.");
                }
            }
        }

        // The autoclicker's key is listened for all the time it has one: it
        // is what switches it on. With the screenshot key and the rules'
        // keys, it is heard by a hook that lets every key go on to the game -
        // see GlobalKeys. Only while the app really runs, and only while
        // there is at least one such key.
        void RefreshGlobalKeys()
        {
            if (!started || !IsHandleCreated) return;
            List<int> want = new List<int>();
            if (settings.Clicker.Enabled && settings.Clicker.ToggleVk != 0) want.Add(settings.Clicker.ToggleVk);
            if (settings.ShotKey != 0 && !want.Contains(settings.ShotKey)) want.Add(settings.ShotKey);
            // Every macro's own key, which plays it from anywhere.
            if (watchStore != null)
                foreach (Macro m in watchStore.Macros)
                    if (m != null && m.Key != 0 && !want.Contains(m.Key)) want.Add(m.Key);
            // And every script's, which starts it and stops it again.
            foreach (KeyValuePair<string, byte> s in Store.Keys()) if (!want.Contains(s.Value)) want.Add(s.Value);
            foreach (int k in RuleKeysWanted()) if (!want.Contains(k)) want.Add(k);

            bool same = globalKeysAsked != null && globalKeysAsked.Count == want.Count;
            if (same) foreach (int k in want) if (!globalKeysAsked.Contains(k)) { same = false; break; }
            if (same && (want.Count == 0 || GlobalKeys.Active)) return;
            // Refused by Windows: asked again now and then, not every second.
            if (same && unchecked((uint)(Environment.TickCount - globalKeysTriedTick)) < GLOBAL_KEYS_RETRY_MS) return;

            globalKeysAsked = want;
            globalKeysTriedTick = Environment.TickCount;
            GlobalKeys.Listen(want, this, OnGlobalKey);
        }

        // On the window's thread: one of those keys went down or came up.
        void OnGlobalKey(HeardKey h)
        {
            if (h == null || IsDisposed) return;
            // Nothing plays only while a key is held yet; when something
            // does, its key's up arrives here too. A macro whose key plays it
            // while held is the one thing that does: its up stops it.
            if (!h.Down) { MacroKeyHeard(h.Key, false); return; }
            bool plain = (h.Key >> 8) == 0;
            byte vk = (byte)(h.Key & 0xFF);
            // While recording, what is pressed is being recorded, not meant:
            // nothing starts. What is going already can still be stopped.
            bool recording = MacroRecorder.Recording;
            if (plain && settings.Clicker.Enabled && vk == settings.Clicker.ToggleVk)
            {
                if (!recording || ClickerOn) ClickerKeyPressed();
                return;
            }
            if (plain && settings.ShotKey != 0 && vk == settings.ShotKey) { if (!recording) TakeScreenshots(); return; }
            if (MacroKeyHeard(h.Key, true)) return;
            if (ScriptKeyHeard(h.Key)) return;
            if (!recording) RuleKeyPressed(h.Key);
        }

        void ClickerKeyPressed()
        {
            string problem = ToggleClicker();
            if (problem == null) return;
            WarnOverGame("Autoclicker", problem);
        }

        // In the activity list, and in a tray balloon: the activity list
        // can't be seen from inside Roblox, where the key was pressed, so it
        // is said over the game too. Pressing it again and again says it once.
        void WarnOverGame(string who, string problem)
        {
            Log(who + ": " + problem);
            int now = Environment.TickCount;
            if (problem == clickWarned && unchecked((uint)(now - clickWarnedTick)) < CLICK_WARN_EVERY_MS) return;
            clickWarned = problem;
            clickWarnedTick = now;
            try
            {
                tray.BalloonTipTitle = who;
                tray.BalloonTipText = char.ToUpper(problem[0]) + problem.Substring(1);
                tray.BalloonTipIcon = ToolTipIcon.Warning;
                tray.ShowBalloonTip(5000);
            }
            catch { }
        }

        // ---------- a macro's own key ----------

        // A key went down or up. True when it is a macro's own key, whatever
        // that then did.
        bool MacroKeyHeard(int key, bool down)
        {
            if ((key >> 8) != 0) return false;       // a macro's key is a plain key
            Macro m = MacroOnKey((byte)(key & 0xFF));
            if (m == null) return false;

            string tag = MacroPlay.KeyTag(m.Name);
            MacroKeyAction act = MacroPlay.Decide(m.KeyMode, down, IsPlaying(tag));
            if (act == MacroKeyAction.Stop) StopTag(tag);
            else if (act == MacroKeyAction.Start && !MacroRecorder.Recording)
            {
                string problem = StartByKey(m, tag);
                if (problem != null) WarnOverGame(m.Name, problem);
            }
            return true;
        }

        // Null when it started, or was already waiting to.
        string StartByKey(Macro m, string tag)
        {
            string problem = m.Problem();
            if (problem != null) return problem;
            int times = MacroPlay.TimesFor(m);
            Macro play = MacroPlay.Prepare(m, times, m.SpeedPct);
            if (m.Anywhere) { StartOnScreen(play, tag); return null; }

            // On the Roblox window you are in, as the autoclicker's key does:
            // you pressed the key in the game you meant.
            int front = PerformanceManager.ForegroundPid();
            WatchedClient on = null;
            foreach (WatchedClient c in watchWork.Clients)
                if (c != null && c.Pid == front) { on = c; break; }
            if (on == null) return "click into the Roblox window first, then press " + MacroKeys.Name(m.Key) + ".";
            string label = string.IsNullOrEmpty(on.AccountName) ? on.Label : on.Label + " - " + on.AccountName;
            // The minute it is kept to is for one play at its own speed. Ten
            // times over, or at a quarter speed, it is far longer, and takes
            // turns with the keyboard so the nudge still gets in.
            QueueMacro(play, on.Pid, label, 1, tag, true, MacroRunner.DEFAULT_TURN_MS,
                       MacroPlay.StepsAside(m, times, m.SpeedPct));
            return null;
        }

        // ---------- playing anywhere on the screen ----------

        // Plays a macro made for the whole screen, on a thread of its own,
        // under a tag: it is listed with the sessions, so the stop key is
        // held while it plays and stops it, and StopTag stops it alone. Null
        // when something under that tag is playing already.
        MacroJob StartOnScreen(Macro play, string tag)
        {
            if (AnySession(tag)) return null;
            MacroJob job = new MacroJob();
            job.Macro = play;
            job.Pid = 0;                // no client: it never meets one in the queue
            job.Label = "the screen";
            job.Tag = tag;
            lock (sessions) sessions.Add(job);
            SessionsChanged();
            Log(play.Name + " is playing - press " + MacroKeys.Name(settings.StopKey) + " to stop it.");

            Thread t = new Thread(delegate() { PlayOnScreen(job); });
            t.IsBackground = true;
            t.Name = "Macro on the screen";
            t.Start();
            return job;
        }

        void PlayOnScreen(MacroJob job)
        {
            string problem;
            try
            {
                PlayOptions o = new PlayOptions();
                o.Stop = delegate { return job.Cancelled; };
                o.OthersWaiting = delegate { return macroQueue.Waiting > 0; };
                problem = MacroPlayer.PlayOnScreen(job.Macro, o);
            }
            catch (Exception ex) { problem = ex.Message; }
            finally { lock (sessions) sessions.Remove(job); }

            string said = problem == null ? "Played " + job.Macro.Name + "."
                        : problem == MacroPlayer.CANCELLED ? job.Macro.Name + " stopped."
                        : job.Macro.Name + " didn't finish: " + problem + ".";
            OnUi(delegate
            {
                Log(said);
                SessionsChanged();
            });
        }

        // From one of the app's windows: a macro made for the whole screen,
        // played as many times and as fast as it is set to (its Times and
        // SpeedPct - this prepares it), under MacroPlay.PlayTag(its name).
        // Null when it started; otherwise why not.
        public string PlayAnywhere(Macro m)
        {
            if (m == null || m.Steps.Count == 0) return "There is nothing to play yet.";
            if (!m.Anywhere) return "That macro plays on a Roblox window.";
            string problem = m.Problem();
            if (problem != null) return problem;
            string tag = MacroPlay.PlayTag(m.Name);
            if (IsPlaying(tag)) return m.Name + " is already playing.";
            StartOnScreen(MacroPlay.Prepare(m, m.Times, m.SpeedPct), tag);
            return null;
        }

        // ---------- what is playing, by who started it ----------

        // Played under this tag right now, or waiting its turn to.
        public bool IsPlaying(string tag)
        {
            if (AnySession(tag) || ScriptsTagged(tag)) return true;
            lock (sessions)
                foreach (MacroJob j in tagged) if (!j.Cancelled && j.Tag == tag) return true;
            return false;
        }

        // Stops whatever was started under this tag - and only that. True
        // when there was something to stop. Each says it stopped as it ends.
        public bool StopTag(string tag)
        {
            int n = StopSessions(tag) + StopScriptsTagged(tag);
            lock (sessions)
                foreach (MacroJob j in tagged)
                    if (!j.Cancelled && j.Tag == tag) { j.Cancelled = true; n++; }
            return n > 0;
        }

        // ---------- Quick Record ----------

        Macro quick;
        bool quickRead;

        // The last Quick Record recording, kept across restarts; null for
        // none. Handed out as a copy: change it, then set it, to keep it.
        public Macro QuickRecording
        {
            get
            {
                if (!quickRead) { quick = MacroPlay.LoadQuick(MacroPlay.QuickPath); quickRead = true; }
                return quick == null ? null : quick.Copy();
            }
            set
            {
                Macro m = value == null ? null : value.Copy();
                if (m != null)
                {
                    m.Anywhere = true;
                    if (string.IsNullOrEmpty(m.Name)) m.Name = MacroPlay.QUICK_NAME;
                }
                quick = m;
                quickRead = true;
                if (!MacroPlay.SaveQuick(MacroPlay.QuickPath, m)) Log("Couldn't keep the Quick Record recording for next time.");
            }
        }

        void ReleaseMacroKeys()
        {
            if (!IsHandleCreated) return;
            if (stopKeyHeld) Native.UnregisterHotKey(Handle, STOP_KEY_ID);
            stopKeyHeld = false;
            // Closing: the overlays go with the app.
            shaders.StopAll();
            scriptPill.Stop();
            RealInput.Stop();
        }

        // True when the key was the stop key - the one key still taken from
        // Windows as a shortcut.
        bool OnMacroKey(int id)
        {
            if (id != STOP_KEY_ID) return false;
            StopEverything();
            return true;
        }

        // From the tray: the two windows people come back to, without going
        // through Watchers to find them.
        void OpenMacros()
        {
            if (watchKit == null) return;
            RestoreFromTray();
            using (MacrosDialog d = new MacrosDialog(watchKit)) d.ShowDialog(this);
        }

        void OpenClicker()
        {
            if (watchKit == null) return;
            RestoreFromTray();
            using (AutoClickDialog d = new AutoClickDialog(watchKit)) d.ShowDialog(this);
        }

        // On, or off if it is on. Null when it switched.
        public string ToggleClicker()
        {
            if (StopSessions(AutoClick.TAG) > 0) return null;      // each says it stopped as it ends

            AutoClick c = settings.Clicker;
            string problem = c.Problem();
            if (problem != null) return problem;

            List<WatchedClient> targets = c.Targets(watchWork.Clients, PerformanceManager.ForegroundPid());
            if (targets.Count == 0)
                return c.On == ClickOn.InFront
                    ? "click into the Roblox window you want it on, then press " + MacroKeys.Name(c.ToggleVk) + "."
                    : "none of the Roblox windows it is set to click on is open.";

            Macro m = c.ToMacro();
            int started = 0;
            foreach (WatchedClient t in targets)
            {
                string label = string.IsNullOrEmpty(t.AccountName) ? t.Label : t.Label + " - " + t.AccountName;
                if (QueueMacro(m, t.Pid, label, 1, AutoClick.TAG, c.On == ClickOn.InFront, c.TurnSeconds * 1000) != null)
                    started++;
            }
            return started > 0 ? null : "it couldn't start - too much is already waiting to play.";
        }
    }
}
