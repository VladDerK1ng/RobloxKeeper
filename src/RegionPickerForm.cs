using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RobloxKeeper
{
    enum RegionPickMode
    {
        // A named box for watchers to read, kept against the game.
        Region,

        // The picture an image watcher looks for, cut out of the capture.
        Picture
    }

    // The arithmetic of drawing a box, kept apart from the form so it can be
    // tested. This is where a box quietly ends up a few pixels off.
    static class RegionPick
    {
        // A box from wherever the drag started to wherever it is now, in any
        // direction, kept inside the picture.
        public static Rectangle FromDrag(Point start, Point now, int w, int h)
        {
            int x0 = Clamp(Math.Min(start.X, now.X), 0, w), x1 = Clamp(Math.Max(start.X, now.X), 0, w);
            int y0 = Clamp(Math.Min(start.Y, now.Y), 0, h), y1 = Clamp(Math.Max(start.Y, now.Y), 0, h);
            return Rectangle.FromLTRB(x0, y0, x1, y1);
        }

        // Which edge or corner a box belongs to: whichever third of the window
        // its centre is in, each way. A banner across the top is TopCentre, a
        // chat box down the left is MiddleLeft. Only a guess - the grid beside
        // the picture overrides it.
        public static RegionAnchor GuessAnchor(Rectangle box, int w, int h)
        {
            int col = Third(box.X + box.Width / 2.0, w);
            int row = Third(box.Y + box.Height / 2.0, h);
            return (RegionAnchor)(row * 3 + col);
        }

        static int Third(double centre, int size)
        {
            if (size <= 0) return 0;
            double f = centre / size;
            return f < 1 / 3.0 ? 0 : (f < 2 / 3.0 ? 1 : 2);
        }

        // Arrow keys move the bottom and right edges by one pixel; with Shift,
        // the top and left. A box never leaves the picture and never gets
        // narrower than a pixel.
        public static Rectangle Nudge(Rectangle box, Keys key, bool shift, int w, int h)
        {
            int dx = key == Keys.Left ? -1 : (key == Keys.Right ? 1 : 0);
            int dy = key == Keys.Up ? -1 : (key == Keys.Down ? 1 : 0);
            int l = box.Left, t = box.Top, r = box.Right, b = box.Bottom;

            if (shift)
            {
                l = Clamp(l + dx, 0, r - 1);
                t = Clamp(t + dy, 0, b - 1);
            }
            else
            {
                r = Clamp(r + dx, l + 1, w);
                b = Clamp(b + dy, t + 1, h);
            }
            return Rectangle.FromLTRB(l, t, r, b);
        }

        // The box in the two ways it is useful to know it: pixels, to see how
        // big it really is, and percent, to see how much of the window it is.
        public static string Describe(Rectangle box, int w, int h)
        {
            if (box.Width <= 0 || box.Height <= 0 || w <= 0 || h <= 0)
                return "Drag a box over the part of the window to watch.";
            return box.Width + " x " + box.Height + " pixels - "
                 + Percent(box.Width, w) + " x " + Percent(box.Height, h) + " of the window";
        }

        static string Percent(int part, int whole)
        {
            return ((int)Math.Floor(part * 100.0 / whole + 0.5)) + "%";
        }

        // What the test button shows. Seeing "5B" where you expected "58" here,
        // before saving, is the point of the button.
        public static string ReadBack(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
                return "It didn't read anything in that box.";
            return "It read:\r\n" + text.Trim().Replace("\n", "\r\n");
        }

        public static string NameProblem(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0)
                return "Give the box a name, so watchers can say which box they read.";
            return null;
        }

        // How much a capture is shrunk to fit on screen. Never enlarged: a
        // magnified picture makes a box look more precise than it is.
        public static double FitScale(int imgW, int imgH, int roomW, int roomH)
        {
            if (imgW <= 0 || imgH <= 0 || roomW <= 0 || roomH <= 0) return 1.0;
            return Math.Min(1.0, Math.Min(roomW / (double)imgW, roomH / (double)imgH));
        }

        public static Point ToImage(Point shown, double scale)
        {
            return new Point(Round(shown.X / scale), Round(shown.Y / scale));
        }

        public static Rectangle ToScreen(Rectangle image, double scale)
        {
            return new Rectangle(Round(image.X * scale), Round(image.Y * scale),
                                 Round(image.Width * scale), Round(image.Height * scale));
        }

        static int Round(double v) { return (int)Math.Floor(v + 0.5); }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }

    // Nine cells, one per anchor. The guessed one is lit; clicking another
    // pins the box there instead.
    class AnchorGrid : Control
    {
        const int CELL = 20, GAP = 4;

        RegionAnchor value;

        public event EventHandler Picked;

        public AnchorGrid()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(CELL * 3 + GAP * 2, CELL * 3 + GAP * 2);
            Cursor = Cursors.Hand;
            BackColor = Theme.Card;
            TabStop = false;
        }

        public RegionAnchor Value
        {
            get { return value; }
            set { this.value = value; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int col = Math.Min(2, e.X / (CELL + GAP));
            int row = Math.Min(2, e.Y / (CELL + GAP));
            Value = (RegionAnchor)(row * 3 + col);
            if (Picked != null) Picked(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (int i = 0; i < 9; i++)
            {
                Rectangle r = new Rectangle((i % 3) * (CELL + GAP), (i / 3) * (CELL + GAP), CELL - 1, CELL - 1);
                bool on = (int)value == i;
                using (GraphicsPath p = Draw.Rounded(r, 4))
                {
                    using (SolidBrush b = new SolidBrush(on ? Theme.Accent : Theme.Inset)) g.FillPath(b, p);
                    using (Pen pen = new Pen(on ? Theme.AccentHover : Color.FromArgb(58, 58, 80), 1f))
                        g.DrawPath(pen, p);
                }
            }
        }
    }

    // The still picture. Selectable, so it can take the arrow keys.
    class StillView : Control
    {
        public StillView()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Cursor = Cursors.Cross;
            TabStop = true;
        }
    }

    // Drawing a box on a still picture of one client's window.
    //
    // The picture is a capture taken the moment this opened, not the live
    // window: the game carries on underneath, unfocused and untouched, and the
    // box is drawn on something that holds still. Drag to draw; arrow keys
    // move the bottom and right edges a pixel at a time, Shift+arrows the top
    // and left.
    //
    // Region mode makes a named box that belongs to the game, with the anchor
    // guessed from where it was drawn and a test button that reads it the way
    // a watcher will. Picture mode cuts out the thing an image watcher looks
    // for.
    class RegionPickerForm : Form
    {
        const int TITLEBAR_H = 40;
        const int SIDE_W = 268;       // the card of controls right of the picture
        const int SIDE_H = 520;
        const int M = 12;             // outer margin
        const int INNER = SIDE_W - Ui.PAD * 2;

        readonly Pixels still;
        readonly RegionPickMode mode;
        readonly string placeId;
        readonly Func<Pixels, string> read;
        readonly Bitmap shown;
        readonly double scale;

        Rectangle box = Rectangle.Empty;    // in capture pixels
        RegionAnchor anchor = RegionAnchor.TopLeft;
        bool anchorChosen;                  // picked on the grid, so no longer guessed
        Point dragFrom;
        bool dragging;

        StillView view;
        Label lblSize;
        TextBox readBox;
        AnchorGrid grid;
        ThemedPicker cmbScaling;
        TextBox nameBox;
        Button btnSave;

        public WatchRegion PickedRegion { get; private set; }
        public Pixels PickedPicture { get; private set; }

        public RegionPickerForm(Pixels still, string clientLabel, RegionPickMode mode,
                                string placeId, RegionScaling scaling, Func<Pixels, string> read)
        {
            if (still == null || still.IsEmpty) throw new ArgumentException("There is no picture to draw on.");

            this.still = still;
            this.mode = mode;
            this.placeId = placeId;
            this.read = read;
            shown = still.ToBitmap();

            // Shrunk to fit the screen with room for the controls beside it.
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            int roomW = Math.Max(320, Math.Min(1280, work.Width - SIDE_W - M * 3 - 40));
            int roomH = Math.Max(240, Math.Min(800, work.Height - TITLEBAR_H - M - 60));
            scale = RegionPick.FitScale(still.Width, still.Height, roomW, roomH);
            Size viewSize = new Size(Math.Max(1, (int)Math.Round(still.Width * scale)),
                                     Math.Max(1, (int)Math.Round(still.Height * scale)));

            Text = mode == RegionPickMode.Region ? "Draw a box" : "Cut out a picture";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(M + viewSize.Width + M + SIDE_W + M,
                                  TITLEBAR_H + Math.Max(viewSize.Height, SIDE_H) + M);

            Build(clientLabel, viewSize, scaling);
            SetBox(Rectangle.Empty);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = Native.DWMWCP_ROUND;
            try { Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4); }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Color.FromArgb(52, 52, 74), 1f))
                e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) shown.Dispose();
            base.Dispose(disposing);
        }

        // ---------- layout ----------

        void Build(string clientLabel, Size viewSize, RegionScaling scaling)
        {
            Panel bar = new Panel();
            bar.Location = new Point(0, 0);
            bar.Size = new Size(ClientSize.Width, TITLEBAR_H);
            bar.BackColor = Theme.Bg;
            Controls.Add(bar);

            string heading = mode == RegionPickMode.Region
                ? "Draw a box on " + clientLabel
                : "Cut out the picture to look for, from " + clientLabel;
            Label title = Ui.RowLabel(heading, 16, 0, TITLEBAR_H, ClientSize.Width - 80, 10f, Theme.Text);
            title.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            title.BackColor = Theme.Bg;
            bar.Controls.Add(title);

            WindowButton close = new WindowButton(true);
            close.Location = new Point(ClientSize.Width - 44, 0);
            close.Size = new Size(44, 32);
            close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            bar.Controls.Add(close);

            bar.MouseDown += StartDrag;
            title.MouseDown += StartDrag;

            view = new StillView();
            view.Location = new Point(M, TITLEBAR_H);
            view.Size = viewSize;
            view.Paint += PaintView;
            view.MouseDown += OnViewDown;
            view.MouseMove += OnViewMove;
            view.MouseUp += delegate { dragging = false; };
            Controls.Add(view);

            Card card = new Card();
            card.Location = new Point(M + viewSize.Width + M, TITLEBAR_H);
            card.Size = new Size(SIDE_W, SIDE_H);
            Controls.Add(card);

            card.Controls.Add(Ui.SectionTitle(mode == RegionPickMode.Region ? "THE BOX" : "THE PICTURE"));

            lblSize = Ui.MutedLabel("", Ui.PAD, 36, 8.25f);
            lblSize.MaximumSize = new Size(INNER, 0);
            card.Controls.Add(lblSize);

            if (mode == RegionPickMode.Region) BuildRegionControls(card, scaling);
            else BuildPictureControls(card);

            Button cancel = Ui.AccentButton("Cancel", Ui.PAD, SIDE_H - 48, 96, 30);
            cancel.BackColor = Theme.Inset;
            cancel.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            cancel.FlatAppearance.MouseOverBackColor = Theme.Bg;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            card.Controls.Add(cancel);

            string saveText = mode == RegionPickMode.Region ? "Save box" : "Use this picture";
            btnSave = Ui.AccentButton(saveText, SIDE_W - Ui.PAD - 124, SIDE_H - 48, 124, 30);
            btnSave.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            btnSave.Click += delegate { Save(); };
            card.Controls.Add(btnSave);

            AcceptButton = btnSave;
            CancelButton = cancel;
        }

        void BuildRegionControls(Card card, RegionScaling scaling)
        {
            card.Controls.Add(Ui.CaptionLabel("PINNED TO", Ui.PAD, 86));
            grid = new AnchorGrid();
            grid.Location = new Point(Ui.PAD, 104);
            grid.Picked += delegate { ChooseAnchor(grid.Value); };
            card.Controls.Add(grid);

            Label gridHint = Ui.MutedLabel(
                "The edge or corner the box stays with when the window changes size. "
                + "Guessed from where you drew it - click another to change it.",
                Ui.PAD + grid.Width + 12, 102, 8.25f);
            gridHint.MaximumSize = new Size(INNER - grid.Width - 12, 0);
            card.Controls.Add(gridHint);

            card.Controls.Add(Ui.CaptionLabel("WHEN THE WINDOW CHANGES SIZE", Ui.PAD, 190));
            cmbScaling = Ui.DarkCombo(Ui.PAD, 208, INNER);
            cmbScaling.Items.Add("Keep the box's shape");
            cmbScaling.Items.Add("Stretch the box with it");
            cmbScaling.SelectedIndex = scaling == RegionScaling.Uniform ? 0 : 1;
            card.Controls.Add(cmbScaling);
            card.Controls.Add(Ui.MutedLabel("Keep the shape for pictures, stretch for text.", Ui.PAD, 238, 8.25f));

            card.Controls.Add(Ui.CaptionLabel("NAME", Ui.PAD, 272));
            nameBox = new TextBox();
            nameBox.Location = new Point(Ui.PAD, 290);
            nameBox.Size = new Size(INNER, 22);
            nameBox.BorderStyle = BorderStyle.FixedSingle;
            nameBox.BackColor = Theme.Inset;
            nameBox.ForeColor = Theme.Text;
            card.Controls.Add(nameBox);

            Button test = Ui.AccentButton("Test - read it now", Ui.PAD, 326, INNER, 28);
            test.BackColor = Theme.Inset;
            test.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            test.FlatAppearance.MouseOverBackColor = Theme.Bg;
            test.Click += delegate { TestRead(); };
            test.Visible = read != null;
            card.Controls.Add(test);

            // Scrolls, because a chat box reads many lines and the ones that
            // matter are usually the last.
            readBox = WatchUi.ReadOnlyBox(Ui.PAD, 360, INNER, SIDE_H - 48 - 12 - 360);
            card.Controls.Add(readBox);
        }

        void BuildPictureControls(Card card)
        {
            Label hint = Ui.MutedLabel(
                "Keep it tight around the thing itself. You can paint out whatever is "
                + "behind it next, so the scenery changing doesn't stop it matching.",
                Ui.PAD, 86, 8.25f);
            hint.MaximumSize = new Size(INNER, 0);
            card.Controls.Add(hint);

            readBox = WatchUi.ReadOnlyBox(Ui.PAD, 160, INNER, 60);
            card.Controls.Add(readBox);
        }

        void StartDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        // ---------- the box ----------

        bool HasBox { get { return box.Width > 0 && box.Height > 0; } }

        public Rectangle Box { get { return box; } }
        public RegionAnchor BoxAnchor { get { return anchor; } }

        public void SetBox(Rectangle b)
        {
            box = b;
            if (!anchorChosen && HasBox) anchor = RegionPick.GuessAnchor(box, still.Width, still.Height);
            if (grid != null) grid.Value = anchor;
            lblSize.Text = RegionPick.Describe(box, still.Width, still.Height);
            btnSave.Enabled = HasBox;
            view.Invalidate();
        }

        // As if clicked on the grid: from now on the box stays pinned there.
        public void ChooseAnchor(RegionAnchor a)
        {
            anchor = a;
            anchorChosen = true;
            if (grid != null) grid.Value = a;
        }

        public void SuggestName(string name)
        {
            if (nameBox != null) nameBox.Text = name ?? "";
        }

        public WatchRegion MakeRegion(string name)
        {
            RegionScaling scaling = cmbScaling != null && cmbScaling.SelectedIndex == 1
                ? RegionScaling.Stretch
                : RegionScaling.Uniform;
            WatchRegion r = WatchRegion.FromBox(box, still.Width, still.Height, anchor, scaling);
            r.Name = name;
            r.PlaceId = placeId;
            return r;
        }

        public Pixels MakePicture()
        {
            return still.Crop(box);
        }

        // Reads the box grown by the same quarter a watcher grows it by, so
        // what is shown here is exactly what a watcher would get.
        public bool ReadScrolls { get { return readBox.ScrollBars == ScrollBars.Vertical; } }
        public string ReadShown { get { return readBox.Text; } }

        public string TestRead()
        {
            if (read == null || !HasBox) return "";
            Rectangle widened = WatchRegion.Widen(box, WatchEngine.WIDEN, still.Width, still.Height);

            string text;
            try { text = read(still.Crop(widened)); }
            catch { text = null; }

            string said = RegionPick.ReadBack(text);
            readBox.Text = WatchUi.Lines(said);
            readBox.ForeColor = Theme.Muted;
            return said;
        }

        void Save()
        {
            if (!HasBox) return;

            if (mode == RegionPickMode.Region)
            {
                string problem = RegionPick.NameProblem(nameBox.Text);
                if (problem != null)
                {
                    readBox.Text = problem;
                    readBox.ForeColor = Theme.Amber;
                    nameBox.Focus();
                    return;
                }
                PickedRegion = MakeRegion(nameBox.Text.Trim());
            }
            else PickedPicture = MakePicture();

            DialogResult = DialogResult.OK;
            Close();
        }

        // ---------- mouse and keys ----------

        void OnViewDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            view.Focus();
            dragging = true;
            dragFrom = RegionPick.ToImage(e.Location, scale);
            SetBox(RegionPick.FromDrag(dragFrom, dragFrom, still.Width, still.Height));
        }

        void OnViewMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            SetBox(RegionPick.FromDrag(dragFrom, RegionPick.ToImage(e.Location, scale), still.Width, still.Height));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool arrow = key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down;

            // Not while typing a name - there the arrows move the caret.
            if (arrow && HasBox && !(ActiveControl is TextBox))
            {
                bool shift = (keyData & Keys.Shift) == Keys.Shift;
                SetBox(RegionPick.Nudge(box, key, shift, still.Width, still.Height));
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void PaintView(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(shown, new Rectangle(0, 0, view.Width, view.Height));
            if (!HasBox) return;

            Rectangle r = RegionPick.ToScreen(box, scale);

            // Everything outside the box is dimmed, so what is in and what is
            // out is obvious at a glance.
            using (System.Drawing.Region outside = new System.Drawing.Region(new Rectangle(0, 0, view.Width, view.Height)))
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
            {
                outside.Exclude(r);
                g.FillRegion(dim, outside);
            }

            g.PixelOffsetMode = PixelOffsetMode.Default;
            using (Pen p = new Pen(Theme.AccentHover, 2f))
                g.DrawRectangle(p, r.X, r.Y, Math.Max(1, r.Width - 1), Math.Max(1, r.Height - 1));
        }
    }
}
