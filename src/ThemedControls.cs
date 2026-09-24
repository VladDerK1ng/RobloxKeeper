using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // WinForms draws checkboxes, spinners and combo buttons with the system
    // theme, which on a dark card means white boxes and grey chrome sitting on
    // near-black panels. These are owner-drawn replacements so every widget in
    // the window belongs to the same palette.
    static class Draw
    {
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Chevron(Graphics g, int cx, int cy, int size, Color color, bool up)
        {
            Chevron(g, cx, cy, size, color, up ? 1.0 : 0.0);
        }

        // Part way through turning over. 0 points down, 1 points up, and
        // anything between is a chevron mid-flip - which is what a dropdown
        // arrow does while its list is opening.
        public static void Chevron(Graphics g, int cx, int cy, int size, Color color, double turned)
        {
            if (turned < 0) turned = 0;
            if (turned > 1) turned = 1;
            float h = (float)(size * (1.0 - 2.0 * turned));
            using (Pen pen = new Pen(color, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new PointF[]
                {
                    new PointF(cx - size, cy - h / 2f),
                    new PointF(cx, cy + h / 2f),
                    new PointF(cx + size, cy - h / 2f)
                });
            }
        }

    }

    class ThemedCheckBox : CheckBox
    {
        const int BOX = 16;
        const int GAP = 9;

        readonly Anim hover = new Anim(0);
        readonly Anim ticked;

        public ThemedCheckBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            TabStop = false;
            AutoSize = true;
            ticked = new Anim(Checked ? 1 : 0);
        }

        void Slide(Anim a, double to, TimeSpan how)
        {
            a.To(to, Animator.Time(how), DateTime.Now);
            Animator.Follow(this, a);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            Slide(hover, 1, Animator.Quick);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            Slide(hover, 0, Animator.Quick);
            base.OnMouseLeave(e);
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            Slide(ticked, Checked ? 1 : 0, Animator.Quick);
            base.OnCheckedChanged(e);
        }

        public override Size GetPreferredSize(Size proposed)
        {
            Size t = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding);
            int w = BOX + (Text.Length > 0 ? GAP + t.Width : 0);
            return new Size(w + 2, Math.Max(BOX + 4, t.Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            double on = ticked.Value;
            double lit = hover.Value;

            Rectangle box = new Rectangle(0, (Height - BOX) / 2, BOX, BOX);
            using (GraphicsPath p = Draw.Rounded(box, 4))
            {
                // The box fills with colour as the tick arrives, rather than
                // swapping between two states.
                Color empty = Theme.Inset;
                Color full = Anim.Blend(Theme.Accent, Theme.AccentHover, lit);
                using (SolidBrush b = new SolidBrush(Anim.Blend(empty, full, on)))
                    g.FillPath(b, p);

                // The outline belongs to the empty box, so it fades out as the
                // box fills - otherwise it darkens the edge of a ticked one.
                if (on < 1)
                {
                    Color edge = Anim.Blend(Theme.Muted, Theme.AccentHover, lit);
                    using (Pen pen = new Pen(Color.FromArgb((int)(255 * (1 - on)), edge), 1.3f))
                        g.DrawPath(pen, p);
                }
            }

            // The tick draws itself on: the short stroke first, then the long
            // one, which is the order a person would draw it.
            if (on > 0.01)
            {
                Point a = new Point(box.Left + 4, box.Top + 8);
                Point b = new Point(box.Left + 7, box.Top + 11);
                Point c = new Point(box.Left + 12, box.Top + 5);

                using (Pen pen = new Pen(Color.FromArgb((int)(255 * Math.Min(1, on * 2)), Color.White), 2f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;

                    // Two thirds of the stroke length is the long arm, so the
                    // first third draws the short one.
                    if (on <= 0.34)
                        g.DrawLine(pen, a, Along(a, b, on / 0.34));
                    else
                    {
                        g.DrawLine(pen, a, b);
                        g.DrawLine(pen, b, Along(b, c, (on - 0.34) / 0.66));
                    }
                }
            }

            if (Text.Length > 0)
            {
                Rectangle t = new Rectangle(BOX + GAP, 0, Width - BOX - GAP, Height);
                TextRenderer.DrawText(g, Text, Font, t, Anim.Blend(ForeColor, Theme.Text, lit),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                    TextFormatFlags.NoPrefix);
            }
        }

        static Point Along(Point from, Point to, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return new Point((int)Math.Round(from.X + (to.X - from.X) * t),
                             (int)Math.Round(from.Y + (to.Y - from.Y) * t));
        }
    }

    // The card on/off switches. A pill reads as "this whole section is running"
    // far more clearly than another tick box among the small ones.
    class ThemedToggle : CheckBox
    {
        const int TRACK_W = 38;
        const int TRACK_H = 20;
        const int KNOB = 14;
        const int GAP = 9;

        readonly Anim hover = new Anim(0);
        readonly Anim on;

        public ThemedToggle()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            TabStop = false;
            AutoSize = true;
            on = new Anim(Checked ? 1 : 0);
        }

        void Slide(Anim a, double to, TimeSpan how)
        {
            a.To(to, Animator.Time(how), DateTime.Now);
            Animator.Follow(this, a);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            Slide(hover, 1, Animator.Quick);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            Slide(hover, 0, Animator.Quick);
            base.OnMouseLeave(e);
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            Slide(on, Checked ? 1 : 0, Animator.Slide);
            base.OnCheckedChanged(e);
        }

        public override Size GetPreferredSize(Size proposed)
        {
            Size t = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding);
            return new Size(t.Width + GAP + TRACK_W + 2, Math.Max(TRACK_H + 2, t.Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            double lit = hover.Value;
            double set = on.Value;

            Size t = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, t.Width + 2, Height),
                Anim.Blend(Theme.Muted, Theme.Text, set),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix);

            Rectangle track = new Rectangle(Width - TRACK_W - 1, (Height - TRACK_H) / 2, TRACK_W, TRACK_H);
            using (GraphicsPath p = Draw.Rounded(track, TRACK_H / 2))
            {
                Color lightUp = Anim.Blend(Theme.Accent, Theme.AccentHover, lit);
                using (SolidBrush b = new SolidBrush(Anim.Blend(Theme.Inset, lightUp, set)))
                    g.FillPath(b, p);

                // The border belongs to the off state and fades as it fills.
                if (set < 1)
                {
                    Color edge = Anim.Blend(Color.FromArgb(60, 60, 82), Theme.Muted, lit);
                    using (Pen pen = new Pen(Color.FromArgb((int)(255 * (1 - set)), edge), 1.3f))
                        g.DrawPath(pen, p);
                }
            }

            // The knob slides between the two ends rather than appearing at
            // one of them. This is the whole reason a pill reads as a switch.
            int left = track.Left + 3;
            int right = track.Right - KNOB - 3;
            int knobX = (int)Math.Round(left + (right - left) * set);

            Rectangle knob = new Rectangle(knobX, track.Top + (TRACK_H - KNOB) / 2, KNOB, KNOB);
            using (SolidBrush b = new SolidBrush(Anim.Blend(Theme.Muted, Color.White, set)))
                g.FillEllipse(b, knob);
        }
    }

    // NumericUpDown paints a system-themed spinner that cannot be recoloured,
    // so this is a small purpose-built stepper instead.
    class ThemedNumeric : Control
    {
        const int SPIN_W = 16;

        int minimum = 1, maximum = 99, value = 1;
        int hotZone;   // 0 none, 1 up, 2 down

        public event EventHandler ValueChanged;

        // Height, corner radius and border deliberately mirror ThemedPicker so a
        // number box and a dropdown read as the same family of control.
        public ThemedNumeric()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            TabStop = false;
            Height = 26;
            Width = 46;
            ForeColor = Theme.Text;
            BackColor = Theme.Card;
        }

        public int Minimum
        {
            get { return minimum; }
            set { minimum = value; if (this.value < minimum) Value = minimum; }
        }

        public int Maximum
        {
            get { return maximum; }
            set { maximum = value; if (this.value > maximum) Value = maximum; }
        }

        public int Value
        {
            get { return value; }
            set
            {
                int v = value < minimum ? minimum : (value > maximum ? maximum : value);
                if (v == this.value) return;
                this.value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        void Step(int delta) { Value = value + delta; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.X >= Width - SPIN_W) Step(e.Y < Height / 2 ? 1 : -1);
        }

        // No mouse-wheel stepping. Windows hands the wheel to whatever is under
        // the pointer, focused or not, so scrolling the window past this box
        // used to change the setting it holds and save it. Left unhandled, the
        // wheel goes on to the window behind it, which scrolls as expected.

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int z = e.X >= Width - SPIN_W ? (e.Y < Height / 2 ? 1 : 2) : 0;
            if (z == hotZone) return;
            hotZone = z;
            // Each arrow lights on its own, so it is obvious which one a
            // click is about to hit. Started here, where the pointer moves,
            // and never from the paint: asking for a paint from inside one
            // repaints the control for as long as it is on screen.
            Glow(upLit, hotZone == 1 ? 1 : 0);
            Glow(downLit, hotZone == 2 ? 1 : 0);
        }

        readonly Anim hover = new Anim(0);
        readonly Anim upLit = new Anim(0);
        readonly Anim downLit = new Anim(0);

        void Glow(Anim a, double to)
        {
            a.To(to, Animator.Time(Animator.Quick), DateTime.Now);
            Animator.Follow(this, a);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            Glow(hover, 1);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hotZone = 0;
            Glow(hover, 0);
            Glow(upLit, 0);
            Glow(downLit, 0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Draw.Rounded(box, 5))
            {
                using (SolidBrush b = new SolidBrush(Theme.Inset)) g.FillPath(b, p);
                using (Pen pen = new Pen(
                    Anim.Blend(Color.FromArgb(58, 58, 80), Theme.Muted, hover.Value), 1f))
                    g.DrawPath(pen, p);
            }

            Rectangle text = new Rectangle(0, 0, Width - SPIN_W, Height);
            TextRenderer.DrawText(g, value.ToString(), Font, text, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);

            int cx = Width - SPIN_W / 2 - 3;
            Draw.Chevron(g, cx, Height / 2 - 5, 3,
                Anim.Blend(Theme.Muted, Theme.AccentHover, upLit.Value), true);
            Draw.Chevron(g, cx, Height / 2 + 5, 3,
                Anim.Blend(Theme.Muted, Theme.AccentHover, downLit.Value), false);
        }
    }

    // A DropDownList ComboBox always lets Windows paint its own border and drop
    // button, and neither can be recoloured - overpainting them just flickers.
    // This is a picker built from scratch: the closed control is drawn like the
    // stepper, and the open list is a borderless popup we own completely.
    class ThemedPicker : Control
    {
        const int BUTTON_W = 22;

        public readonly System.Collections.Generic.List<string> Items =
            new System.Collections.Generic.List<string>();

        int selected = -1;

        readonly Anim hover = new Anim(0);
        readonly Anim opened = new Anim(0);

        // The list currently on screen, if any. It is modeless: a modal one
        // disables the owner window, which means a click on this control never
        // arrives and the list can only be dismissed by picking a row.
        PickerPopup openList;

        // When a list last closed, so the click that closed it is not mistaken
        // for a click asking to open a new one.
        DateTime closedAt = DateTime.MinValue;

        static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(250);

        public event EventHandler SelectedIndexChanged;

        public ThemedPicker()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            TabStop = false;
            Height = 26;
            ForeColor = Theme.Text;
            BackColor = Theme.Card;
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                int v = value < 0 || value >= Items.Count ? -1 : value;
                if (v == selected) return;
                selected = v;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public override string Text
        {
            get { return selected >= 0 && selected < Items.Count ? Items[selected] : string.Empty; }
            set { base.Text = value; }
        }

        protected override void OnMouseEnter(EventArgs e) { Glow(hover, 1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Glow(hover, 0); base.OnMouseLeave(e); }

        void Glow(Anim a, double to)
        {
            a.To(to, Animator.Time(Animator.Quick), DateTime.Now);
            Animator.Follow(this, a);
            Invalidate();
        }

        // Should this click open the list?
        //
        // No if one is already open - that click should close it. And no if a
        // list closed a moment ago, because that is this same click arriving
        // after the list dismissed itself on losing focus; acting on it would
        // reopen the list instantly and it would never appear to close.
        public static bool ShouldOpenOnClick(bool listOpen, TimeSpan sinceClosed, TimeSpan guard)
        {
            if (listOpen) return false;
            return sinceClosed >= guard;
        }

        public bool IsListOpen { get { return openList != null; } }

        void CloseList()
        {
            if (openList == null) return;
            PickerPopup p = openList;
            openList = null;
            closedAt = DateTime.Now;
            try { p.Close(); } catch { }
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Items.Count == 0) return;

            if (openList != null) { CloseList(); return; }
            if (!ShouldOpenOnClick(false, DateTime.Now - closedAt, ReopenGuard)) return;

            PickerPopup pop = new PickerPopup(Items, selected, Width);
            pop.Location = PointToScreen(new Point(0, Height + 2));

            // Flip above the control if the list would run off the screen.
            Rectangle screen = Screen.FromControl(this).WorkingArea;
            if (pop.Bottom > screen.Bottom)
                pop.Location = PointToScreen(new Point(0, -pop.Height - 2));

            // Chosen is the authority: a row was either clicked or it was not,
            // and nothing about how the window closes can undo that.
            pop.FormClosed += delegate
            {
                if (pop.Chosen >= 0) SelectedIndex = pop.Chosen;
                openList = null;
                closedAt = DateTime.Now;
                Glow(opened, 0);
                pop.Dispose();
            };

            openList = pop;
            pop.Show(FindForm());
            pop.FadeIn();
            Glow(opened, 1);
        }

        // No mouse-wheel stepping, for the same reason as ThemedNumeric: a
        // scroll passing over a closed dropdown is not a choice, and it used to
        // change and save one - the Performance default among them. Choosing
        // means opening the list.

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Draw.Rounded(box, 5))
            {
                using (SolidBrush b = new SolidBrush(Theme.Inset)) g.FillPath(b, p);
                using (Pen pen = new Pen(
                    Anim.Blend(Color.FromArgb(58, 58, 80), Theme.Muted, hover.Value), 1f))
                    g.DrawPath(pen, p);
            }

            Rectangle text = new Rectangle(9, 0, Width - BUTTON_W - 9, Height);
            TextRenderer.DrawText(g, Text, Font, text, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                TextFormatFlags.EndEllipsis);

            // The chevron turns over as the list opens, so the control says
            // which way it is about to go. Started where the list opens and
            // closes - never from here, or the control repaints for ever.
            Draw.Chevron(g, Width - BUTTON_W / 2 - 4, Height / 2, 4,
                Anim.Blend(Theme.Muted, Theme.Text, hover.Value), opened.Value);
        }
    }

    // The open list. Closing on Deactivate is what makes a click anywhere else
    // dismiss it, the way a real dropdown behaves.
    class PickerPopup : Form
    {
        const int ROW = 26;
        const int PAD = 4;

        readonly System.Collections.Generic.List<string> items;
        readonly int current;
        int hover = -1;

        public int Chosen = -1;

        public PickerPopup(System.Collections.Generic.List<string> items, int current, int width)
        {
            this.items = items;
            this.current = current;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Inset;
            ClientSize = new Size(width, items.Count * ROW + PAD * 2);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            // Dismiss the list when the user clicks elsewhere - but NOT when the
            // deactivation is simply this form closing because a row was picked.
            // Setting DialogResult starts the close, closing deactivates, and
            // this handler used to overwrite the OK with a Cancel, throwing the
            // selection away. It depended on activation timing, so a dropdown
            // accepted roughly half the clicks made in it.
            // Clicking anywhere else dismisses the list. Modeless, so there
            // is no DialogResult to set - Chosen already records whether a row
            // was picked, and the owner reads it when the window closes.
            Deactivate += delegate { Close(); };
        }

        // The list arrives rather than appearing. Opacity and a few pixels of
        // travel - enough to show where it came from, short enough that
        // nobody waits for it.
        //
        // Called after Show, because a form has no window handle before that
        // and setting Opacity on one that does not exist has no effect.
        public void FadeIn()
        {
            if (!Animator.MotionWanted) return;

            Point resting = Location;
            int from = resting.Y - 6;

            Opacity = 0;
            Location = new Point(resting.X, from);

            Anim slide = new Anim(0);
            slide.To(1, Animator.Time(Animator.Quick), DateTime.Now);
            Animator.Run(this, slide, delegate
            {
                if (IsDisposed) return;
                double t = slide.Value;
                Opacity = t;
                Location = new Point(resting.X, (int)Math.Round(from + (resting.Y - from) * t));
            });
        }

        // A choice has been recorded, so losing activation is the close we asked
        // for rather than the user clicking away. Index 0 is a real choice,
        // which is why this tests against -1 and not against zero.
        internal bool ShouldCancelOnDeactivate { get { return Chosen < 0; } }

        protected override bool ShowWithoutActivation { get { return false; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;   // CS_DROPSHADOW
                cp.ExStyle |= 0x00000080;      // WS_EX_TOOLWINDOW - keep it off Alt+Tab
                return cp;
            }
        }

        int IndexAt(int y)
        {
            int i = (y - PAD) / ROW;
            return i >= 0 && i < items.Count ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.Y);
            if (i != hover) { hover = i; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != -1) { hover = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = IndexAt(e.Y);
            if (i < 0) return;
            Chosen = i;
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Theme.Inset)) g.FillRectangle(b, ClientRectangle);

            for (int i = 0; i < items.Count; i++)
            {
                Rectangle row = new Rectangle(PAD, PAD + i * ROW, ClientSize.Width - PAD * 2, ROW);
                Color fg = Theme.Text;
                if (i == hover)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath p = Draw.Rounded(row, 4))
                    using (SolidBrush b = new SolidBrush(Theme.Accent))
                        g.FillPath(b, p);
                    fg = Color.White;
                }
                else if (i == current) fg = Theme.AccentHover;

                TextRenderer.DrawText(g, items[i], Font,
                    new Rectangle(row.X + 6, row.Y, row.Width - 6, row.Height), fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                    TextFormatFlags.EndEllipsis);
            }

            using (Pen pen = new Pen(Color.FromArgb(58, 58, 80), 1f))
                g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    // Minimise / close for the custom title bar. Close goes red on hover the way
    // Windows' own does, so the muscle memory still works.
    class WindowButton : Control
    {
        public bool IsClose;
        readonly Anim hover = new Anim(0);

        public WindowButton(bool isClose)
        {
            IsClose = isClose;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            TabStop = false;
            Size = new Size(44, 30);
            BackColor = Theme.Bg;
        }

        protected override void OnMouseEnter(EventArgs e) { Glow(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Glow(0); base.OnMouseLeave(e); }

        void Glow(double to)
        {
            hover.To(to, Animator.Time(Animator.Quick), DateTime.Now);
            Animator.Follow(this, hover);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            double lit = hover.Value;

            Color highlight = IsClose ? Color.FromArgb(232, 72, 85) : Color.FromArgb(52, 52, 74);
            using (SolidBrush b = new SolidBrush(Anim.Blend(BackColor, highlight, lit)))
                g.FillRectangle(b, ClientRectangle);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fg = Anim.Blend(Theme.Muted, Color.White, lit);
            int cx = Width / 2, cy = Height / 2, r = 5;
            using (Pen pen = new Pen(fg, 1.4f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (IsClose)
                {
                    g.DrawLine(pen, cx - r, cy - r, cx + r, cy + r);
                    g.DrawLine(pen, cx + r, cy - r, cx - r, cy + r);
                }
                else g.DrawLine(pen, cx - r, cy, cx + r, cy);
            }
        }
    }

    // A status light. Drawn rather than typed as "●", because the glyph sits
    // above its own line box and never quite centres against the text beside it.
    class Dot : Control
    {
        // A dot that breathes while a client is being worked on, and sits
        // still otherwise. Stillness is the point: if every dot pulsed all the
        // time the list would be noise, and the one that matters would not
        // stand out.
        bool busy;
        readonly Anim breath = new Anim(0);

        public bool Breathing { get { return breath.Running; } }

        public bool Busy
        {
            get { return busy; }
            set
            {
                if (busy == value) return;
                busy = value;
                if (busy) Breathe();
                else
                {
                    breath.To(0, Animator.Time(Animator.Quick), DateTime.Now);
                    Animator.Follow(this, breath);
                }
                Invalidate();
            }
        }

        void Breathe()
        {
            if (!busy) return;
            if (!Animator.MotionWanted) return;
            // Towards whichever end it is not at. A flag of its own for which
            // way it was going started out of step - its first breath asked to
            // go where it already was, which is nothing, so it never moved.
            breath.To(breath.FarEndFrom(0, 1), TimeSpan.FromMilliseconds(900), DateTime.Now);
            Animator.Run(this, breath, OnBreathFrame);
        }

        void OnBreathFrame()
        {
            Invalidate();
            // One breath ends, the next begins - for as long as there is
            // something to be busy about.
            if (!breath.Running && busy) Breathe();
        }

        public Dot()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            TabStop = false;
            Size = new Size(12, 19);
            BackColor = Theme.Card;
            ForeColor = Theme.Muted;
        }

        protected override void OnForeColorChanged(EventArgs e) { Invalidate(); base.OnForeColorChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            const int D = 9;
            float cx = Width / 2f, cy = Height / 2f;
            double pulse = breath.Value;

            // A halo that swells and fades, drawn under the dot so the dot
            // itself never moves or changes size - a list of jiggling dots is
            // hard to read.
            if (pulse > 0.01)
            {
                float halo = (float)(D + 5 * pulse);
                int alpha = (int)(70 * (1 - pulse));
                if (alpha > 0)
                    using (SolidBrush glow = new SolidBrush(Color.FromArgb(alpha, ForeColor)))
                        g.FillEllipse(glow, cx - halo / 2f, cy - halo / 2f, halo, halo);
            }

            using (SolidBrush b = new SolidBrush(ForeColor))
                g.FillEllipse(b, cx - D / 2f, cy - D / 2f, D, D);
        }
    }

    // Rounded inset well, used to visually contain the countdown.
    class InsetPanel : Panel
    {
        public InsetPanel()
        {
            BackColor = Theme.Inset;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (GraphicsPath p = Draw.Rounded(new Rectangle(0, 0, Width, Height), 8))
                Region = new Region(p);
        }
    }

    static class DarkScrollbars
    {
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);

        // Windows paints scrollbars light unless the control opts into the dark
        // explorer theme, which leaves a bright bar down the side of the log.
        public static void Apply(Control c)
        {
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); }
            catch { }
        }
    }
}
