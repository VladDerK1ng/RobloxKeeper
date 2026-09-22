using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // What the watcher screens need from the rest of the app, in one place, so
    // they can be built - and tested - without the main window.
    class WatchKit
    {
        public WatchStore Store;
        public WatchEngine Engine;
        public ICapture Capture;
        public IReadText Reader;
        public Func<WatchedClient[]> Clients;      // running now, in list order
        public Func<IList<string>> Accounts;       // saved account names
        public Action<string> Log;
        public bool CanReadText = true;

        public WatchedClient[] RunningClients()
        {
            WatchedClient[] c = Clients == null ? null : Clients();
            return c ?? new WatchedClient[0];
        }

        public void Say(string line)
        {
            if (Log != null) Log(line);
        }
    }

    // The editor's rules, kept apart from the form so they can be tested.
    static class WatcherForm
    {
        // Edits are made on a copy. The watch thread is reading the original
        // the whole time the editor is open, and must never see it half-changed.
        public static Watcher Copy(Watcher w)
        {
            return WatchStore.DeserializeWatcher(WatchStore.SerializeWatcher(w));
        }

        public static string[] WordsFrom(string text)
        {
            List<string> words = new List<string>();
            if (string.IsNullOrEmpty(text)) return words.ToArray();
            foreach (string line in text.Replace("\r", "").Split('\n'))
                if (line.Trim().Length > 0) words.Add(line.Trim());
            return words.ToArray();
        }

        public static string WordsText(string[] words)
        {
            return words == null ? "" : string.Join("\r\n", words);
        }

        public static string KindName(WatchKind k)
        {
            switch (k)
            {
                case WatchKind.ChatLine: return "A new chat line";
                case WatchKind.ImageFound: return "A picture appears";
                default: return "A word appears";
            }
        }

        // Said next to the choice, because it is the one setting that quietly
        // slows everything else down.
        public static string CostNote(bool wholeWindow)
        {
            return wholeWindow
                ? "Reading the whole window takes about nine times as long as reading a box, "
                  + "so a client watched this way is checked about once a second instead of four times."
                : "";
        }

        // Null when the watcher can be saved; otherwise what to fix, in words.
        public static string Problem(Watcher w)
        {
            if (string.IsNullOrEmpty(w.Name) || w.Name.Trim().Length == 0)
                return "Give it a name - it's what the message you get is called.";
            if (w.Kind == WatchKind.TextAppears &&
                (w.Rule == null || WordsFrom(WordsText(w.Rule.Words)).Length == 0))
                return "Add at least one word to look for.";
            if (w.Kind == WatchKind.ImageFound && string.IsNullOrEmpty(w.TemplateFile))
                return "Cut out the picture to look for first.";
            if (!w.SendDiscord && !w.ShowTray && !w.PlaySound && !w.WriteLog)
                return "Tick at least one way to be told when it finds something.";
            if (!string.IsNullOrEmpty(w.WebhookUrl) && !WebhookPost.LooksLikeDiscordWebhook(w.WebhookUrl))
                return "That isn't a Discord webhook link. Leave it blank to use the one in the watchers list.";
            return null;
        }

        // What "Test against client now" says: matched or not, and why - what
        // was read, or how alike the picture was against how alike it has to be.
        public static string CheckSummary(Watcher w, WatchCheck k)
        {
            if (k == null) return "";
            if (k.Problem != null) return k.Problem;

            switch (w.Kind)
            {
                case WatchKind.ImageFound:
                    string need = Percent(w.Tolerance);
                    return k.Seen
                        ? "Found it - " + Percent(k.Score) + " alike, and it needs " + need + "."
                        : "Not found. The closest match was " + Percent(k.Score) + " alike, and it needs " + need + ".";

                case WatchKind.ChatLine:
                    if (string.IsNullOrEmpty(k.Text) || k.Text.Trim().Length == 0)
                        return "It didn't read any chat there. Is the chat open on that client?";
                    string filter = Blank(w.ChatContains);
                    if (filter == null) return "It read:\r\n" + Clip(k.Text);

                    // The lines that would count come first. In a whole-window
                    // read they are a few among the leaderboard and the buttons,
                    // and usually the last of them.
                    int lines = 0;
                    List<string> passing = new List<string>();
                    foreach (string line in k.Text.Split('\n'))
                    {
                        if (line.Trim().Length == 0) continue;
                        lines++;
                        if (line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) passing.Add(line.Trim());
                    }
                    string head = "It read " + lines + (lines == 1 ? " line" : " lines") + ". "
                                + passing.Count + " of them contain \"" + filter + "\""
                                + (passing.Count > 0 ? ":\r\n" + string.Join("\r\n", passing.ToArray()) : ".");
                    return head + "\r\n\r\nEverything it read:\r\n" + Clip(k.Text);

                default:
                    string read = string.IsNullOrEmpty(k.Text) || k.Text.Trim().Length == 0
                        ? "It didn't read anything there."
                        : "It read: " + Clip(k.Text.Replace("\n", " / "));
                    return (k.Seen ? "Matched \"" + k.Matched + "\". " : "No match. ") + read;
            }
        }

        static string Percent(double v)
        {
            return ((int)Math.Floor(v * 100 + 0.5)) + "%";
        }

        // Only a guard against something absurd: the result box scrolls, and a
        // whole-window chat read runs to several hundred characters with the
        // lines that matter at the end.
        static string Clip(string s)
        {
            s = s.Trim();
            return s.Length <= 2000 ? s : s.Substring(0, 1997) + "...";
        }

        public static string Blank(string s)
        {
            return string.IsNullOrEmpty(s) || s.Trim().Length == 0 ? null : s.Trim();
        }

        // Pictures and their painted-out copies are ordinary files named after
        // the watcher, so the folder makes sense when opened.
        public static string TemplateName(string watcherName, DateTime now)
        {
            StringBuilder sb = new StringBuilder();
            bool dash = false;
            foreach (char c in watcherName ?? "")
            {
                if (c < 128 && char.IsLetterOrDigit(c)) { sb.Append(c); dash = false; }
                else if (!dash && sb.Length > 0) { sb.Append('-'); dash = true; }
            }
            string stem = sb.ToString().Trim('-');
            if (stem.Length > 40) stem = stem.Substring(0, 40).Trim('-');
            if (stem.Length == 0) stem = "picture";
            return stem + "-" + Stamp(now) + ".png";
        }

        // Stamped with its own time, so painting again never overwrites the
        // copy a saved watcher is using - cancelling the editor has to leave
        // that watcher exactly as it was.
        public static string MaskNameFor(string templateFile, DateTime now)
        {
            return Path.GetFileNameWithoutExtension(templateFile) + "-mask-" + Stamp(now) + ".png";
        }

        static string Stamp(DateTime t)
        {
            return t.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        }

        public static void SavePicture(Pixels p, string file)
        {
            string path = WatchStore.TemplatePath(file);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            using (Bitmap b = p.ToBitmap()) b.Save(path, ImageFormat.Png);
        }

        // A box drawn again with the same name for the same game replaces the
        // old one - that is how a box that needs re-checking gets redrawn.
        public static void SaveRegion(WatchStore store, WatchRegion r)
        {
            for (int i = 0; i < store.Regions.Count; i++)
            {
                WatchRegion old = store.Regions[i];
                if (string.Equals(old.PlaceId, r.PlaceId, StringComparison.Ordinal) &&
                    string.Equals(old.Name, r.Name, StringComparison.OrdinalIgnoreCase))
                {
                    store.Regions[i] = r;
                    return;
                }
            }
            store.Regions.Add(r);
        }

        public static Pixels FullMask(int w, int h)
        {
            Pixels m = new Pixels(w, h);
            m.Fill(unchecked((int)0xFF000000));
            return m;
        }

        // Paints out - or back in - a round brush of pixels. A painted-out
        // pixel is transparent, and a watcher never compares it.
        public static void Paint(Pixels mask, int cx, int cy, int radius, bool erase)
        {
            int colour = erase ? 0 : unchecked((int)0xFF000000);
            for (int y = cy - radius; y <= cy + radius; y++)
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if (x < 0 || y < 0 || x >= mask.Width || y >= mask.Height) continue;
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= radius * radius) mask.Set(x, y, colour);
                }
        }

        // The picture as it will be compared: painted-out pixels shown as a
        // checkerboard, so it is obvious what counts and what does not.
        public static Bitmap Preview(Pixels template, Pixels mask)
        {
            Bitmap b = template.ToBitmap();
            if (mask == null || mask.Width != template.Width || mask.Height != template.Height) return b;
            bool[] keep = WatchEngine.MaskFrom(mask);
            for (int y = 0; y < template.Height; y++)
                for (int x = 0; x < template.Width; x++)
                    if (!keep[y * template.Width + x])
                        b.SetPixel(x, y, ((x / 3 + y / 3) % 2 == 0) ? Color.FromArgb(70, 70, 90) : Color.FromArgb(45, 45, 60));
            return b;
        }
    }

    // Shared by both watcher windows: the borderless frame and the input
    // styles every other dialog in the app uses.
    static class WatchUi
    {
        public const int TITLEBAR_H = 40;

        public static void Frame(Form f, string title, int w, int h)
        {
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.None;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(w, h);
            f.BackColor = Theme.Bg;
            f.ForeColor = Theme.Text;
            f.Font = new Font("Segoe UI", 9f);

            Panel bar = new Panel();
            bar.Location = new Point(0, 0);
            bar.Size = new Size(w, TITLEBAR_H);
            bar.BackColor = Theme.Bg;
            f.Controls.Add(bar);

            Label t = Ui.RowLabel(title, 16, 0, TITLEBAR_H, w - 80, 10f, Theme.Text);
            t.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            t.BackColor = Theme.Bg;
            bar.Controls.Add(t);

            WindowButton close = new WindowButton(true);
            close.Location = new Point(w - 44, 0);
            close.Size = new Size(44, 32);
            close.Click += delegate { f.DialogResult = DialogResult.Cancel; f.Close(); };
            bar.Controls.Add(close);

            MouseEventHandler drag = delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                Native.ReleaseCapture();
                Native.SendMessage(f.Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
            };
            bar.MouseDown += drag;
            t.MouseDown += drag;

            f.HandleCreated += delegate
            {
                int round = Native.DWMWCP_ROUND;
                try { Native.DwmSetWindowAttribute(f.Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4); }
                catch { }
            };
            f.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Color.FromArgb(52, 52, 74), 1f))
                    e.Graphics.DrawRectangle(p, 0, 0, f.ClientSize.Width - 1, f.ClientSize.Height - 1);
            };
        }

        public static TextBox Input(int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.Location = new Point(x, y);
            t.Size = new Size(w, 22);
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = Theme.Inset;
            t.ForeColor = Theme.Text;
            return t;
        }

        // A webhook URL is a password in all but name: whoever sees it can
        // post into the channel. It is typed into a box that never shows it.
        public static TextBox SecretInput(int x, int y, int w)
        {
            TextBox t = Input(x, y, w);
            t.UseSystemPasswordChar = true;
            return t;
        }

        // A box for text of any length - what a test read, say. It scrolls
        // rather than stopping at its bottom edge, which a label does.
        public static TextBox ReadOnlyBox(int x, int y, int w, int h)
        {
            TextBox t = new TextBox();
            t.Location = new Point(x, y);
            t.Size = new Size(w, h);
            t.Multiline = true;
            t.ReadOnly = true;
            t.WordWrap = true;
            t.ScrollBars = ScrollBars.Vertical;
            t.BorderStyle = BorderStyle.None;
            t.BackColor = Theme.Card;
            t.ForeColor = Theme.Muted;
            t.Font = new Font("Segoe UI", 8.25f);
            t.TabStop = false;
            t.HandleCreated += delegate { DarkScrollbars.Apply(t); };
            return t;
        }

        // A text box only breaks a line at "\r\n", and the recogniser hands
        // back "\n" - without this every line read runs into the next.
        public static string Lines(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        public static Button Secondary(string text, int x, int y, int w, int h)
        {
            Button b = Ui.AccentButton(text, x, y, w, h);
            b.BackColor = Theme.Inset;
            b.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            b.FlatAppearance.MouseOverBackColor = Theme.Bg;
            return b;
        }

        public static Button Primary(string text, int x, int y, int w, int h)
        {
            Button b = Ui.AccentButton(text, x, y, w, h);
            b.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            return b;
        }

        public static Card CardAt(Control parent, string title, int x, int y, int w, int h)
        {
            Card c = new Card();
            c.Location = new Point(x, y);
            c.Size = new Size(w, h);
            parent.Controls.Add(c);
            c.Controls.Add(Ui.SectionTitle(title));
            return c;
        }

        // What a running client is called in a picker.
        public static string ClientName(WatchedClient c)
        {
            string name = c.Label;
            if (!string.IsNullOrEmpty(c.AccountName)) name += " · " + c.AccountName;
            if (string.IsNullOrEmpty(c.PlaceId)) name += " (not in a game yet)";
            return name;
        }
    }

    // Painting out whatever is behind the picture, so only the thing itself is
    // compared. Left button paints out, right button paints back in.
    class MaskPainterForm : Form
    {
        const int SIDE_W = 220;
        const int M = 12;

        readonly Pixels template;
        readonly Pixels mask;
        readonly int zoom;
        StillView canvas;
        ThemedPicker cmbBrush;
        bool painting, erasing;
        Bitmap preview;

        public Pixels Mask { get { return mask; } }

        public MaskPainterForm(Pixels template, Pixels existingMask)
        {
            this.template = template;
            mask = WatcherForm.FullMask(template.Width, template.Height);
            if (existingMask != null && existingMask.Width == template.Width && existingMask.Height == template.Height)
                Array.Copy(existingMask.Argb, mask.Argb, mask.Argb.Length);

            // Blown up so single pixels can be hit with the mouse.
            zoom = Math.Max(1, Math.Min(12, 360 / Math.Max(1, Math.Max(template.Width, template.Height))));
            int cw = Math.Max(template.Width * zoom, 200), ch = Math.Max(template.Height * zoom, 200);

            WatchUi.Frame(this, "Paint out the background", M + cw + M + SIDE_W + M,
                          WatchUi.TITLEBAR_H + Math.Max(ch, 260) + M);

            canvas = new StillView();
            canvas.Location = new Point(M, WatchUi.TITLEBAR_H);
            canvas.Size = new Size(cw, ch);
            canvas.BackColor = Theme.Inset;
            canvas.Paint += PaintCanvas;
            canvas.MouseDown += delegate(object s, MouseEventArgs e)
            {
                painting = true;
                erasing = e.Button != MouseButtons.Right;
                PaintAt(ToImage(e.Location), erasing);
            };
            canvas.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (painting) PaintAt(ToImage(e.Location), erasing);
            };
            canvas.MouseUp += delegate { painting = false; };
            Controls.Add(canvas);

            Card card = WatchUi.CardAt(this, "BRUSH", M + cw + M, WatchUi.TITLEBAR_H, SIDE_W, Math.Max(ch, 260));
            cmbBrush = Ui.DarkCombo(Ui.PAD, 40, SIDE_W - Ui.PAD * 2);
            cmbBrush.Items.Add("Small");
            cmbBrush.Items.Add("Medium");
            cmbBrush.Items.Add("Large");
            cmbBrush.SelectedIndex = 1;
            card.Controls.Add(cmbBrush);

            Label hint = Ui.MutedLabel(
                "Paint over everything that isn't the thing itself. Left button paints it out, "
                + "right button paints it back. Painted-out parts are never compared.",
                Ui.PAD, 76, 8.25f);
            hint.MaximumSize = new Size(SIDE_W - Ui.PAD * 2, 0);
            card.Controls.Add(hint);

            LinkLabel reset = Ui.RowLink("Start again", Ui.PAD, 150, 22);
            reset.Click += delegate
            {
                Pixels full = WatcherForm.FullMask(mask.Width, mask.Height);
                Array.Copy(full.Argb, mask.Argb, mask.Argb.Length);
                RebuildPreview();
            };
            card.Controls.Add(reset);

            int footY = card.Height - 44;
            Button cancel = WatchUi.Secondary("Cancel", Ui.PAD, footY, 80, 28);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            card.Controls.Add(cancel);
            Button ok = WatchUi.Primary("Use this", SIDE_W - Ui.PAD - 90, footY, 90, 28);
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            card.Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = cancel;

            RebuildPreview();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && preview != null) preview.Dispose();
            base.Dispose(disposing);
        }

        int BrushRadius()
        {
            switch (cmbBrush.SelectedIndex)
            {
                case 0: return 1;
                case 2: return 6;
                default: return 3;
            }
        }

        Point ToImage(Point shown)
        {
            int ox = (canvas.Width - template.Width * zoom) / 2, oy = (canvas.Height - template.Height * zoom) / 2;
            return new Point((int)Math.Floor((shown.X - ox) / (double)zoom),
                             (int)Math.Floor((shown.Y - oy) / (double)zoom));
        }

        public void PaintAt(Point imagePoint, bool erase)
        {
            WatcherForm.Paint(mask, imagePoint.X, imagePoint.Y, BrushRadius(), erase);
            RebuildPreview();
        }

        void RebuildPreview()
        {
            if (preview != null) preview.Dispose();
            preview = WatcherForm.Preview(template, mask);
            canvas.Invalidate();
        }

        void PaintCanvas(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush bg = new SolidBrush(Theme.Inset)) g.FillRectangle(bg, canvas.ClientRectangle);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            int ox = (canvas.Width - template.Width * zoom) / 2, oy = (canvas.Height - template.Height * zoom) / 2;
            g.DrawImage(preview, new Rectangle(ox, oy, template.Width * zoom, template.Height * zoom));
        }
    }

    // Editing one watcher: what it looks for, where, on which clients, how
    // sure it has to be, and how it tells you - plus a button that runs it
    // against a client right now, exactly as typed, so it is tuned against
    // the real screen rather than guessed.
    class WatcherEditDialog : Form
    {
        const int W = 784;
        const int COL_W = 374;
        const int X1 = 12, X2 = 12 + COL_W + 12;
        const int TOP = WatchUi.TITLEBAR_H + 6;
        const int TOP_H = 380;
        const int LOW_Y = TOP + TOP_H + 12;
        const int LOW_H = 210;
        const int FOOT_Y = LOW_Y + LOW_H + 12;
        const int H = FOOT_Y + 32 + 12;
        const int ROW_H = 26;
        const int FIELD_X = 120;
        const int FIELD_W = COL_W - FIELD_X - Ui.PAD;
        const int INNER = COL_W - Ui.PAD * 2;

        readonly Watcher original;
        readonly WatchKit kit;

        // Picture files made while the editor was open. Any not used by the
        // saved watcher are deleted on the way out.
        readonly List<string> created = new List<string>();
        string templateFile, maskFile;

        TextBox nameBox, wordsBox, chatBox, hookBox, resultBox;
        ThemedPicker cmbKind, cmbMode, cmbWho, cmbWhere, cmbClient;
        ThemedCheckBox chkCase, chkWhole, chkDiscord, chkTray, chkSound, chkLog;
        ThemedNumeric numTolerance, numConfirm, numCooldown;
        Panel textPanel, chatPanel, picturePanel, firingPanel;
        ScrollPanel accountsList;
        PictureBox thumb;
        Label lblPicture, lblCost, lblProblem, lblChatOnly;
        Button btnTest;
        readonly List<string> regionNames = new List<string>();
        readonly List<ThemedCheckBox> accountChecks = new List<ThemedCheckBox>();
        WatchedClient[] clients = new WatchedClient[0];

        public Watcher Result { get; private set; }

        public WatcherEditDialog(Watcher w, WatchKit kit)
        {
            original = w;
            this.kit = kit;
            templateFile = w.TemplateFile;
            maskFile = w.MaskFile;

            WatchUi.Frame(this, string.IsNullOrEmpty(w.Name) ? "New watcher" : "Edit " + w.Name, W, H);
            Build();
            Fill(WatcherForm.Copy(w));
            FormClosed += delegate { TidyPictures(); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && thumb != null && thumb.Image != null) thumb.Image.Dispose();
            base.Dispose(disposing);
        }

        // ---------- layout ----------

        void Build()
        {
            BuildWhat(WatchUi.CardAt(this, "WHAT TO WATCH FOR", X1, TOP, COL_W, TOP_H));
            BuildWhere(WatchUi.CardAt(this, "WHERE AND WHEN", X2, TOP, COL_W, TOP_H));
            BuildTell(WatchUi.CardAt(this, "TELL ME BY", X1, LOW_Y, COL_W, LOW_H));
            BuildTry(WatchUi.CardAt(this, "TRY IT ON A CLIENT", X2, LOW_Y, COL_W, LOW_H));

            lblProblem = Ui.RowLabel("", X1 + 4, FOOT_Y, 32, 520, 8.25f, Theme.Amber);
            lblProblem.BackColor = Theme.Bg;
            Controls.Add(lblProblem);

            Button cancel = WatchUi.Secondary("Cancel", W - 12 - 116 - 8 - 96, FOOT_Y, 96, 32);
            cancel.BackColor = Theme.Card;
            cancel.FlatAppearance.MouseOverBackColor = Theme.Inset;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            Button save = WatchUi.Primary("Save", W - 12 - 116, FOOT_Y, 116, 32);
            save.Click += delegate { Save(); };
            Controls.Add(save);

            CancelButton = cancel;
        }

        void BuildWhat(Card card)
        {
            card.Controls.Add(Ui.RowLabel("Name", Ui.PAD, 44, ROW_H, 90, 9f, Theme.Muted));
            nameBox = WatchUi.Input(FIELD_X, 46, FIELD_W);
            card.Controls.Add(nameBox);

            card.Controls.Add(Ui.RowLabel("Watch for", Ui.PAD, 78, ROW_H, 90, 9f, Theme.Muted));
            cmbKind = Ui.DarkCombo(FIELD_X, 78, FIELD_W);
            foreach (WatchKind k in (WatchKind[])Enum.GetValues(typeof(WatchKind)))
                cmbKind.Items.Add(WatcherForm.KindName(k));
            cmbKind.SelectedIndexChanged += delegate { ShowKind(); };
            card.Controls.Add(cmbKind);

            textPanel = KindPanel(card);
            textPanel.Controls.Add(Ui.CaptionLabel("WORDS - ONE PER LINE", Ui.PAD, 0));
            wordsBox = WatchUi.Input(Ui.PAD, 18, INNER);
            wordsBox.Multiline = true;
            wordsBox.AcceptsReturn = true;
            wordsBox.ScrollBars = ScrollBars.Vertical;
            wordsBox.Height = 84;
            textPanel.Controls.Add(wordsBox);
            textPanel.Controls.Add(Ui.RowLabel("Match", Ui.PAD, 112, ROW_H, 90, 9f, Theme.Muted));
            cmbMode = Ui.DarkCombo(FIELD_X, 112, FIELD_W);
            cmbMode.Items.Add("Any of these words");
            cmbMode.Items.Add("All of these words");
            textPanel.Controls.Add(cmbMode);
            chkCase = Ui.DarkCheck("Ignore capitals", Ui.PAD, 148, 9f);
            textPanel.Controls.Add(chkCase);
            chkWhole = Ui.DarkCheck("Whole words only - so \"rat\" doesn't match \"grateful\"", Ui.PAD, 174, 9f);
            textPanel.Controls.Add(chkWhole);
            AddNoTextNote(textPanel, 206);

            chatPanel = KindPanel(card);
            chatPanel.Controls.Add(Ui.CaptionLabel("ONLY LINES CONTAINING", Ui.PAD, 0));
            chatBox = WatchUi.Input(Ui.PAD, 18, INNER);
            chatPanel.Controls.Add(chatBox);
            Label chatHint = Ui.MutedLabel(
                "Leave it blank to be told about every new line. You get the whole line, not just "
                + "the word - and only while the chat is open on that client.", Ui.PAD, 50, 8.25f);
            chatHint.MaximumSize = new Size(INNER, 0);
            chatPanel.Controls.Add(chatHint);
            AddNoTextNote(chatPanel, 112);

            picturePanel = KindPanel(card);
            picturePanel.Controls.Add(Ui.CaptionLabel("THE PICTURE", Ui.PAD, 0));
            thumb = new PictureBox();
            thumb.Location = new Point(Ui.PAD, 18);
            thumb.Size = new Size(72, 72);
            thumb.BackColor = Theme.Inset;
            thumb.SizeMode = PictureBoxSizeMode.Zoom;
            picturePanel.Controls.Add(thumb);
            Button cut = WatchUi.Primary("Cut out from a client", Ui.PAD + 86, 18, INNER - 86, 28);
            cut.Click += delegate { CutPicture(); };
            picturePanel.Controls.Add(cut);
            Button paint = WatchUi.Secondary("Paint out the background", Ui.PAD + 86, 52, INNER - 86, 28);
            paint.Click += delegate { PaintMask(); };
            picturePanel.Controls.Add(paint);
            lblPicture = Ui.MutedLabel("", Ui.PAD, 98, 8.25f);
            lblPicture.MaximumSize = new Size(INNER, 0);
            picturePanel.Controls.Add(lblPicture);
            picturePanel.Controls.Add(Ui.RowLabel("How alike", Ui.PAD, 128, ROW_H, 90, 9f, Theme.Muted));
            numTolerance = Ui.DarkNumeric(FIELD_X, 128, 56, 50, 99, 82);
            picturePanel.Controls.Add(numTolerance);
            picturePanel.Controls.Add(Ui.RowLabel("%", FIELD_X + 60, 128, ROW_H, 30, 9f, Theme.Muted));
            Label tolHint = Ui.MutedLabel(
                "Lower finds it more often but can match the wrong thing. Test it below to see "
                + "how alike it really is.", Ui.PAD, 162, 8.25f);
            tolHint.MaximumSize = new Size(INNER, 0);
            picturePanel.Controls.Add(tolHint);
        }

        Panel KindPanel(Card card)
        {
            Panel p = new Panel();
            p.Location = new Point(0, 116);
            p.Size = new Size(COL_W, TOP_H - 124);
            p.BackColor = Theme.Card;
            card.Controls.Add(p);
            return p;
        }

        void AddNoTextNote(Panel p, int y)
        {
            if (kit.CanReadText) return;
            Label l = Ui.MutedLabel(
                "Windows can't read text on this PC yet - install it from the watchers list. "
                + "Picture watchers work without it.", Ui.PAD, y, 8.25f);
            l.ForeColor = Theme.Amber;
            l.MaximumSize = new Size(INNER, 0);
            p.Controls.Add(l);
        }

        void BuildWhere(Card card)
        {
            card.Controls.Add(Ui.RowLabel("Clients", Ui.PAD, 44, ROW_H, 90, 9f, Theme.Muted));
            cmbWho = Ui.DarkCombo(FIELD_X, 44, FIELD_W);
            cmbWho.Items.Add("All clients");
            cmbWho.Items.Add("Only these accounts");
            // Hidden rather than disabled: a themed tick box looks the same
            // either way, and a live-looking list that does nothing misleads.
            cmbWho.SelectedIndexChanged += delegate { accountsList.Visible = cmbWho.SelectedIndex == 1; };
            card.Controls.Add(cmbWho);

            accountsList = new ScrollPanel();
            accountsList.Location = new Point(Ui.PAD, 76);
            accountsList.Size = new Size(INNER, 70);
            accountsList.BackColor = Theme.Card;
            accountsList.AutoScroll = true;
            card.Controls.Add(accountsList);

            Label who = Ui.MutedLabel(
                "A client opened outside the account manager has no name, so only All clients covers it.",
                Ui.PAD, 150, 8.25f);
            who.MaximumSize = new Size(INNER, 0);
            card.Controls.Add(who);

            card.Controls.Add(Ui.RowLabel("Where", Ui.PAD, 188, ROW_H, 90, 9f, Theme.Muted));
            cmbWhere = Ui.DarkCombo(FIELD_X, 188, FIELD_W);
            cmbWhere.SelectedIndexChanged += delegate { lblCost.Text = WatcherForm.CostNote(cmbWhere.SelectedIndex <= 0); };
            card.Controls.Add(cmbWhere);

            LinkLabel draw = Ui.RowLink("Draw a new box", FIELD_X - 3, 216, 22, 9f);
            draw.Click += delegate { DrawBox(); };
            card.Controls.Add(draw);

            lblCost = Ui.MutedLabel("", Ui.PAD, 242, 8.25f);
            lblCost.MaximumSize = new Size(INNER, 0);
            card.Controls.Add(lblCost);

            firingPanel = new Panel();
            firingPanel.Location = new Point(0, 298);
            firingPanel.Size = new Size(COL_W, 70);
            firingPanel.BackColor = Theme.Card;
            card.Controls.Add(firingPanel);

            firingPanel.Controls.Add(Ui.RowLabel("Seen", Ui.PAD, 0, ROW_H, 90, 9f, Theme.Muted));
            numConfirm = Ui.DarkNumeric(FIELD_X, 0, 50, 1, 10, 2);
            firingPanel.Controls.Add(numConfirm);
            firingPanel.Controls.Add(Ui.RowLabel("scans in a row before it counts", FIELD_X + 58, 0, ROW_H, 200, 8.25f, Theme.Muted));

            firingPanel.Controls.Add(Ui.RowLabel("Then wait", Ui.PAD, 34, ROW_H, 90, 9f, Theme.Muted));
            numCooldown = Ui.DarkNumeric(FIELD_X, 34, 64, 0, 3600, 30);
            firingPanel.Controls.Add(numCooldown);
            firingPanel.Controls.Add(Ui.RowLabel("seconds before saying it again", FIELD_X + 72, 34, ROW_H, 180, 8.25f, Theme.Muted));

            lblChatOnly = Ui.MutedLabel("Every new chat line is its own message, so there is nothing to wait for.",
                Ui.PAD, 304, 8.25f);
            lblChatOnly.MaximumSize = new Size(INNER, 0);
            card.Controls.Add(lblChatOnly);
        }

        void BuildTell(Card card)
        {
            chkDiscord = Ui.DarkCheck("Discord", Ui.PAD, 42, 9f);
            chkTray = Ui.DarkCheck("A pop-up by the clock", Ui.PAD + 150, 42, 9f);
            chkSound = Ui.DarkCheck("A sound", Ui.PAD, 68, 9f);
            chkLog = Ui.DarkCheck("The activity list", Ui.PAD + 150, 68, 9f);
            card.Controls.Add(chkDiscord);
            card.Controls.Add(chkTray);
            card.Controls.Add(chkSound);
            card.Controls.Add(chkLog);

            card.Controls.Add(Ui.CaptionLabel("A DIFFERENT DISCORD WEBHOOK FOR THIS ONE", Ui.PAD, 100));
            hookBox = WatchUi.SecretInput(Ui.PAD, 118, INNER);
            card.Controls.Add(hookBox);
            card.Controls.Add(Ui.MutedLabel("Optional. Blank uses the one in the watchers list.", Ui.PAD, 144, 8.25f));
        }

        void BuildTry(Card card)
        {
            card.Controls.Add(Ui.RowLabel("Client", Ui.PAD, 42, ROW_H, 90, 9f, Theme.Muted));
            cmbClient = Ui.DarkCombo(FIELD_X, 42, FIELD_W);
            card.Controls.Add(cmbClient);

            btnTest = WatchUi.Secondary("Test against this client now", Ui.PAD, 76, INNER, 28);
            btnTest.Click += delegate { RunTest(SelectedClient()); };
            card.Controls.Add(btnTest);

            resultBox = WatchUi.ReadOnlyBox(Ui.PAD, 110, INNER, LOW_H - 120);
            card.Controls.Add(resultBox);
        }

        // ---------- loading and collecting ----------

        void Fill(Watcher w)
        {
            nameBox.Text = w.Name ?? "";
            cmbKind.SelectedIndex = (int)w.Kind;

            MatchRule rule = w.Rule ?? new MatchRule();
            wordsBox.Text = WatcherForm.WordsText(rule.Words);
            cmbMode.SelectedIndex = rule.Mode == MatchMode.All ? 1 : 0;
            chkCase.Checked = rule.IgnoreCase;
            chkWhole.Checked = rule.WholeWordsOnly;

            chatBox.Text = w.ChatContains ?? "";
            numTolerance.Value = (int)Math.Floor(w.Tolerance * 100 + 0.5);
            ShowPicture();

            FillAccounts(w.Accounts);
            cmbWho.SelectedIndex = w.Accounts == null || w.Accounts.Length == 0 ? 0 : 1;
            accountsList.Visible = cmbWho.SelectedIndex == 1;

            FillWhere(w.RegionName);

            numConfirm.Value = w.ConfirmScans;
            numCooldown.Value = w.CooldownSeconds;

            chkDiscord.Checked = w.SendDiscord;
            chkTray.Checked = w.ShowTray;
            chkSound.Checked = w.PlaySound;
            chkLog.Checked = w.WriteLog;
            hookBox.Text = w.WebhookUrl ?? "";

            FillClients();
            ShowKind();
        }

        // The watcher as the form stands now. Starts from a copy of the one
        // being edited, so anything the form does not show is kept as it was.
        public Watcher Collect()
        {
            Watcher w = WatcherForm.Copy(original);
            w.Name = nameBox.Text.Trim();
            w.Kind = (WatchKind)Math.Max(0, cmbKind.SelectedIndex);

            w.Rule = new MatchRule();
            w.Rule.Words = WatcherForm.WordsFrom(wordsBox.Text);
            w.Rule.Mode = cmbMode.SelectedIndex == 1 ? MatchMode.All : MatchMode.Any;
            w.Rule.IgnoreCase = chkCase.Checked;
            w.Rule.WholeWordsOnly = chkWhole.Checked;

            w.ChatContains = WatcherForm.Blank(chatBox.Text);
            w.TemplateFile = templateFile;
            w.MaskFile = maskFile;
            w.Tolerance = numTolerance.Value / 100.0;

            List<string> picked = new List<string>();
            if (cmbWho.SelectedIndex == 1)
                foreach (ThemedCheckBox c in accountChecks)
                    if (c.Checked) picked.Add((string)c.Tag);
            w.Accounts = picked.ToArray();

            w.RegionName = cmbWhere.SelectedIndex <= 0 ? null : regionNames[cmbWhere.SelectedIndex - 1];
            w.ConfirmScans = numConfirm.Value;
            w.CooldownSeconds = numCooldown.Value;

            w.SendDiscord = chkDiscord.Checked;
            w.ShowTray = chkTray.Checked;
            w.PlaySound = chkSound.Checked;
            w.WriteLog = chkLog.Checked;
            w.WebhookUrl = WatcherForm.Blank(hookBox.Text);
            return w;
        }

        void FillAccounts(string[] chosen)
        {
            // Saved accounts, then any running client's, then any this watcher
            // names that are no longer saved - so nothing it was assigned to
            // quietly drops off the list.
            List<string> names = new List<string>();
            IList<string> saved = kit.Accounts == null ? null : kit.Accounts();
            if (saved != null) foreach (string n in saved) AddName(names, n);
            foreach (WatchedClient c in kit.RunningClients()) AddName(names, c.AccountName);
            if (chosen != null) foreach (string n in chosen) AddName(names, n);

            accountsList.Controls.Clear();
            accountChecks.Clear();
            int y = 0;
            foreach (string n in names)
            {
                ThemedCheckBox c = Ui.DarkCheck(n, 2, y, 9f);
                c.ForeColor = Theme.Text;
                c.Tag = n;
                c.Checked = Contains(chosen, n);
                accountsList.Controls.Add(c);
                accountChecks.Add(c);
                y += 22;
            }
            if (names.Count == 0)
                accountsList.Controls.Add(Ui.MutedLabel("No accounts saved yet.", 2, 2, 8.25f));
        }

        static void AddName(List<string> names, string n)
        {
            if (string.IsNullOrEmpty(n)) return;
            foreach (string have in names)
                if (string.Equals(have, n, StringComparison.OrdinalIgnoreCase)) return;
            names.Add(n);
        }

        static bool Contains(string[] list, string n)
        {
            if (list == null) return false;
            foreach (string s in list)
                if (string.Equals(s, n, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Every box name drawn for any game, since a watcher names a box and
        // each game has its own box of that name.
        void FillWhere(string select)
        {
            regionNames.Clear();
            foreach (WatchRegion r in kit.Store.Regions) AddName(regionNames, r.Name);
            AddName(regionNames, select);

            cmbWhere.Items.Clear();
            cmbWhere.Items.Add("The whole window");
            foreach (string n in regionNames) cmbWhere.Items.Add("The box called \"" + n + "\"");

            int at = 0;
            for (int i = 0; i < regionNames.Count; i++)
                if (string.Equals(regionNames[i], select, StringComparison.OrdinalIgnoreCase)) at = i + 1;
            cmbWhere.SelectedIndex = at;
            lblCost.Text = WatcherForm.CostNote(at == 0);
        }

        void FillClients()
        {
            clients = kit.RunningClients();
            cmbClient.Items.Clear();
            foreach (WatchedClient c in clients) cmbClient.Items.Add(WatchUi.ClientName(c));
            if (clients.Length > 0) cmbClient.SelectedIndex = 0;
            btnTest.Enabled = clients.Length > 0;
            if (clients.Length == 0)
                ShowResult("Open a Roblox client to draw boxes, cut out pictures or test this.");
        }

        WatchedClient SelectedClient()
        {
            int i = cmbClient.SelectedIndex;
            return i >= 0 && i < clients.Length ? clients[i] : null;
        }

        void ShowKind()
        {
            WatchKind k = (WatchKind)Math.Max(0, cmbKind.SelectedIndex);
            textPanel.Visible = k == WatchKind.TextAppears;
            chatPanel.Visible = k == WatchKind.ChatLine;
            picturePanel.Visible = k == WatchKind.ImageFound;
            firingPanel.Visible = k != WatchKind.ChatLine;
            lblChatOnly.Visible = k == WatchKind.ChatLine;
        }

        // ---------- pictures ----------

        void ShowPicture()
        {
            if (thumb.Image != null) { thumb.Image.Dispose(); thumb.Image = null; }
            if (string.IsNullOrEmpty(templateFile))
            {
                lblPicture.Text = "No picture yet. Cut one out of a client that is showing it.";
                return;
            }

            Pixels t = WatchEngine.LoadPicture(templateFile);
            if (t == null)
            {
                lblPicture.Text = "Couldn't open " + templateFile + ". Cut it out again.";
                return;
            }
            Pixels m = maskFile == null ? null : WatchEngine.LoadPicture(maskFile);
            thumb.Image = WatcherForm.Preview(t, m);
            lblPicture.Text = t.Width + " x " + t.Height + " pixels"
                + (m != null ? ", background painted out." : ". Paint out its background so scenery can't spoil it.");
        }

        void CutPicture()
        {
            WatchedClient c = ClientToUse();
            if (c == null) return;
            Pixels still = Grab(c);
            if (still == null) return;

            using (RegionPickerForm f = new RegionPickerForm(still, WatchUi.ClientName(c),
                       RegionPickMode.Picture, c.PlaceId, RegionScaling.Uniform, null))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.PickedPicture == null) return;
                string file = WatcherForm.TemplateName(nameBox.Text, DateTime.Now);
                try { WatcherForm.SavePicture(f.PickedPicture, file); }
                catch (Exception ex) { ShowResult("Couldn't save the picture: " + ex.Message); return; }
                created.Add(file);
                templateFile = file;
                maskFile = null;
                ShowPicture();
            }
        }

        void PaintMask()
        {
            if (string.IsNullOrEmpty(templateFile)) { ShowResult("Cut out a picture first."); return; }
            Pixels t = WatchEngine.LoadPicture(templateFile);
            if (t == null) { ShowResult("Couldn't open the picture. Cut it out again."); return; }

            Pixels existing = maskFile == null ? null : WatchEngine.LoadPicture(maskFile);
            using (MaskPainterForm f = new MaskPainterForm(t, existing))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                string file = WatcherForm.MaskNameFor(templateFile, DateTime.Now);
                try { WatcherForm.SavePicture(f.Mask, file); }
                catch (Exception ex) { ShowResult("Couldn't save it: " + ex.Message); return; }
                created.Add(file);
                maskFile = file;
                ShowPicture();
            }
        }

        // Files made here that the saved watcher does not use are removed, so
        // cancelling leaves nothing behind.
        void TidyPictures()
        {
            bool saved = Result != null;
            foreach (string file in created)
            {
                if (saved && (file == Result.TemplateFile || file == Result.MaskFile)) continue;
                try { File.Delete(WatchStore.TemplatePath(file)); } catch { }
            }
            created.Clear();
        }

        // ---------- boxes ----------

        void DrawBox()
        {
            WatchedClient c = ClientToUse();
            if (c == null) return;
            if (string.IsNullOrEmpty(c.PlaceId))
            {
                ShowResult("That client hasn't joined a game yet, and a box belongs to a game. "
                         + "Try again once it's in one.");
                return;
            }
            Pixels still = Grab(c);
            if (still == null) return;

            RegionScaling scaling = cmbKind.SelectedIndex == (int)WatchKind.ImageFound
                ? RegionScaling.Uniform
                : RegionScaling.Stretch;
            Func<Pixels, string> read = kit.CanReadText && kit.Reader != null
                ? new Func<Pixels, string>(kit.Reader.Read)
                : null;

            using (RegionPickerForm f = new RegionPickerForm(still, WatchUi.ClientName(c),
                       RegionPickMode.Region, c.PlaceId, scaling, read))
            {
                if (cmbWhere.SelectedIndex > 0) f.SuggestName(regionNames[cmbWhere.SelectedIndex - 1]);
                if (f.ShowDialog(this) != DialogResult.OK || f.PickedRegion == null) return;

                WatcherForm.SaveRegion(kit.Store, f.PickedRegion);
                kit.Store.Save();
                kit.Say("Box \"" + f.PickedRegion.Name + "\" saved for this game.");
                FillWhere(f.PickedRegion.Name);
            }
        }

        WatchedClient ClientToUse()
        {
            WatchedClient c = SelectedClient();
            if (c == null)
                ShowResult("Open a Roblox client first - boxes, pictures and tests all need one to look at.");
            return c;
        }

        Pixels Grab(WatchedClient c)
        {
            WatchState state = WatchState.NotInAGame;
            Pixels p = null;
            try { p = kit.Capture.Grab(c.Pid, out state); }
            catch { }
            if (p != null && !p.IsEmpty) return p;

            ShowResult(state == WatchState.Watchable
                ? "Couldn't take a picture of that client just now."
                : WindowCapture.Explain(state));
            return null;
        }

        // ---------- test and save ----------

        // Runs the watcher as it stands in the form - not as last saved -
        // against one client right now. Nothing is counted and nothing is sent.
        // What the result box shows, and that it scrolls - a chat test reads
        // every line in the box, and the lines that matter come last.
        public bool ResultScrolls { get { return resultBox.ScrollBars == ScrollBars.Vertical; } }
        public string ResultShown { get { return resultBox.Text; } }

        void ShowResult(string text)
        {
            resultBox.Text = WatchUi.Lines(text);
        }

        public string RunTest(WatchedClient c)
        {
            if (c == null) return resultBox.Text;
            Watcher w = Collect();
            WatchCheck k = kit.Engine.Check(w, c, kit.Store.Regions);
            string said = WatcherForm.CheckSummary(w, k);
            ShowResult(said);
            return said;
        }

        void Save()
        {
            Watcher w = Collect();
            string problem = WatcherForm.Problem(w);
            if (problem != null)
            {
                lblProblem.Text = problem;
                return;
            }
            Result = w;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
