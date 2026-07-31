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
            using (Pen pen = new Pen(color, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                int h = up ? -size : size;
                g.DrawLines(pen, new Point[]
                {
                    new Point(cx - size, cy - h / 2),
                    new Point(cx, cy + h / 2),
                    new Point(cx + size, cy - h / 2)
                });
            }
        }
    }

    class ThemedCheckBox : CheckBox
    {
        const int BOX = 16;
        const int GAP = 9;
        bool hot;

        public ThemedCheckBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            TabStop = false;
            AutoSize = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

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

            Rectangle box = new Rectangle(0, (Height - BOX) / 2, BOX, BOX);
            using (GraphicsPath p = Draw.Rounded(box, 4))
            {
                if (Checked)
                {
                    using (SolidBrush b = new SolidBrush(hot ? Theme.AccentHover : Theme.Accent))
                        g.FillPath(b, p);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(Theme.Inset)) g.FillPath(b, p);
                    using (Pen pen = new Pen(hot ? Theme.AccentHover : Theme.Muted, 1.3f))
                        g.DrawPath(pen, p);
                }
            }

            if (Checked)
            {
                using (Pen pen = new Pen(Color.White, 2f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    g.DrawLines(pen, new Point[]
                    {
                        new Point(box.Left + 4, box.Top + 8),
                        new Point(box.Left + 7, box.Top + 11),
                        new Point(box.Left + 12, box.Top + 5)
                    });
                }
            }

            if (Text.Length > 0)
            {
                Rectangle t = new Rectangle(BOX + GAP, 0, Width - BOX - GAP, Height);
                TextRenderer.DrawText(g, Text, Font, t, hot ? Theme.Text : ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                    TextFormatFlags.NoPrefix);
            }
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
        bool hot;

        public ThemedToggle()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            TabStop = false;
            AutoSize = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

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

            Size t = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, t.Width + 2, Height),
                Checked ? Theme.Text : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix);

            Rectangle track = new Rectangle(Width - TRACK_W - 1, (Height - TRACK_H) / 2, TRACK_W, TRACK_H);
            using (GraphicsPath p = Draw.Rounded(track, TRACK_H / 2))
            {
                Color fill = Checked ? (hot ? Theme.AccentHover : Theme.Accent) : Theme.Inset;
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                if (!Checked)
                    using (Pen pen = new Pen(hot ? Theme.Muted : Color.FromArgb(60, 60, 82), 1.3f))
                        g.DrawPath(pen, p);
            }

            int knobX = Checked ? track.Right - KNOB - 3 : track.Left + 3;
            Rectangle knob = new Rectangle(knobX, track.Top + (TRACK_H - KNOB) / 2, KNOB, KNOB);
            using (SolidBrush b = new SolidBrush(Checked ? Color.White : Theme.Muted))
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

        public ThemedNumeric()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            TabStop = false;
            Height = 24;
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

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Step(e.Delta > 0 ? 1 : -1);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int z = e.X >= Width - SPIN_W ? (e.Y < Height / 2 ? 1 : 2) : 0;
            if (z != hotZone) { hotZone = z; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hotZone != 0) { hotZone = 0; Invalidate(); }
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
                using (Pen pen = new Pen(Color.FromArgb(58, 58, 80), 1f)) g.DrawPath(pen, p);
            }

            Rectangle text = new Rectangle(0, 0, Width - SPIN_W, Height);
            TextRenderer.DrawText(g, value.ToString(), Font, text, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);

            int cx = Width - SPIN_W / 2 - 3;
            Draw.Chevron(g, cx, Height / 2 - 5, 3,
                hotZone == 1 ? Theme.AccentHover : Theme.Muted, true);
            Draw.Chevron(g, cx, Height / 2 + 5, 3,
                hotZone == 2 ? Theme.AccentHover : Theme.Muted, false);
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
        bool hot;

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

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Items.Count == 0) return;
            using (PickerPopup pop = new PickerPopup(Items, selected, Width))
            {
                pop.Location = PointToScreen(new Point(0, Height + 2));
                // Flip above the control if the list would run off the screen.
                Rectangle screen = Screen.FromControl(this).WorkingArea;
                if (pop.Bottom > screen.Bottom)
                    pop.Location = PointToScreen(new Point(0, -pop.Height - 2));
                if (pop.ShowDialog(FindForm()) == DialogResult.OK && pop.Chosen >= 0)
                    SelectedIndex = pop.Chosen;
            }
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Items.Count == 0) return;
            int next = selected + (e.Delta > 0 ? -1 : 1);
            if (next >= 0 && next < Items.Count) SelectedIndex = next;
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
                using (Pen pen = new Pen(hot ? Theme.Muted : Color.FromArgb(58, 58, 80), 1f))
                    g.DrawPath(pen, p);
            }

            Rectangle text = new Rectangle(9, 0, Width - BUTTON_W - 9, Height);
            TextRenderer.DrawText(g, Text, Font, text, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                TextFormatFlags.EndEllipsis);

            Draw.Chevron(g, Width - BUTTON_W / 2 - 4, Height / 2, 4,
                hot ? Theme.Text : Theme.Muted, false);
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
            Deactivate += delegate { DialogResult = DialogResult.Cancel; Close(); };
        }

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
            DialogResult = DialogResult.OK;
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
