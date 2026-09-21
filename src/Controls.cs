using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RobloxKeeper
{
    class Card : Panel
    {
        public Card() { BackColor = Theme.Card; }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            GraphicsPath p = new GraphicsPath();
            Rectangle r = ClientRectangle;
            int d = 20;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            Region = new Region(p);
        }
    }

    // Focusable panel so the mouse wheel scrolls the client list on hover.
    class ScrollPanel : Panel
    {
        public ScrollPanel()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (CanFocus && !ContainsFocus) Focus();
        }
    }

    // Shared builders for the dark-theme widgets used across the main window and
    // the per-client tuning dialog, so both look like the same application.
    static class Ui
    {
        // Cards use a 20px gutter on both sides and sit their heading close to the
        // top edge, so the heading groups with the content under it rather than
        // floating midway between.
        public const int PAD = 20;
        public const int TITLE_Y = 14;
        public const int SUBTITLE_Y = 32;

        public static Label SectionTitle(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(PAD, TITLE_Y);
            l.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            l.ForeColor = Theme.Muted;
            l.BackColor = Theme.Card;
            return l;
        }

        // The explanatory line under a heading. Previously these sat beside the
        // heading, which crowded it and broke the left alignment of the card.
        public static Label Subtitle(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.MaximumSize = new Size(388, 0);
            l.Location = new Point(PAD, SUBTITLE_Y);
            l.Font = new Font("Segoe UI", 8.25f);
            l.ForeColor = Color.FromArgb(104, 108, 132);
            l.BackColor = Theme.Card;
            return l;
        }

        // A label that fills a row's full height and centres its text inside it.
        // AutoSize labels sit wherever their font's ascent puts them, which is
        // what makes a row of labels and input boxes look a pixel jagged.
        public static Label RowLabel(string text, int x, int rowTop, int rowHeight, int width,
                                     float size, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Location = new Point(x, rowTop);
            l.Size = new Size(width, rowHeight);
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Font = new Font("Segoe UI", size);
            l.ForeColor = color;
            l.BackColor = Theme.Card;
            return l;
        }

        // Same, right-aligned - for the memory column in the client list, so the
        // GB and MB stack instead of drifting with the number's width.
        public static Label RowValue(string text, int x, int rowTop, int rowHeight, int width, float size)
        {
            Label l = RowLabel(text, x, rowTop, rowHeight, width, size, Theme.Muted);
            l.TextAlign = ContentAlignment.MiddleRight;
            return l;
        }

        // Vertically centres a control inside a row.
        //
        // Two things here were quietly wrong and put most of the window a
        // couple of pixels out of true.
        //
        // Height is not final on an auto-sized control at the moment this is
        // called - it settles when the control is laid out - so the offset was
        // computed from whatever Height happened to be at construction.
        //
        // And the division truncated toward zero. A fixed-height RowLabel sits
        // at rowTop with the full row height, so its centre is rowTop +
        // rowHeight/2; a control whose height differs from the row by an odd
        // number landed a pixel off that every time, and one TALLER than its
        // row was pushed the wrong way entirely.
        //
        // Prefer a row participant that needs no measuring at all - RowLabel
        // and the four-argument RowLink both fill the row and centre their own
        // text. This is for the ones that size themselves, like a TextBox.
        public static void CenterIn(Control c, int rowTop, int rowHeight)
        {
            // An auto-sized control still reports its pre-layout Height here,
            // so ask it what size it wants instead. GetPreferredSize is a
            // measurement; PerformLayout is not - calling that from here
            // recurses during construction and the window never appears at all.
            int h = c.Height;
            if (c.AutoSize)
            {
                Size want = c.GetPreferredSize(Size.Empty);
                if (want.Height > 0) h = want.Height;
            }

            // Floor(diff/2 + 0.5) rather than integer division: C# truncates
            // toward zero, which rounds the wrong way for the rows where the
            // control is TALLER than the row it sits in - the status dot is
            // 19px in a 17px line of text.
            int offset = (int)Math.Floor((rowHeight - h) / 2.0 + 0.5);
            c.Location = new Point(c.Location.X, rowTop + offset);
        }

        // The height a SectionTitle actually renders at, which the control
        // beside it has to centre against.
        public const int TITLE_H = 15;

        public static Label CaptionLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            l.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            l.ForeColor = Theme.Muted;
            l.BackColor = Theme.Card;
            return l;
        }

        public static Label MutedLabel(string text, int x, int y, float size)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.MaximumSize = new Size(388, 0);
            l.Location = new Point(x, y);
            l.Font = new Font("Segoe UI", size);
            l.ForeColor = Theme.Muted;
            l.BackColor = Theme.Card;
            return l;
        }

        public static Button AccentButton(string text, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Theme.Accent;
            b.ForeColor = Color.White;
            b.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
            b.Cursor = Cursors.Hand;
            b.Font = new Font("Segoe UI", 9.75f, FontStyle.Bold);
            b.TabStop = false;
            return b;
        }

        public static ThemedPicker DarkCombo(int x, int y, int w)
        {
            ThemedPicker c = new ThemedPicker();
            c.Location = new Point(x, y);
            c.Width = w;
            return c;
        }

        public static ThemedCheckBox DarkCheck(string text, int x, int y, float size)
        {
            ThemedCheckBox c = new ThemedCheckBox();
            c.Text = text;
            c.Location = new Point(x, y);
            c.Font = new Font("Segoe UI", size);
            c.ForeColor = Theme.Muted;
            c.BackColor = Theme.Card;
            return c;
        }

        public static ThemedNumeric DarkNumeric(int x, int y, int w, int min, int max, int value)
        {
            ThemedNumeric n = new ThemedNumeric();
            n.Minimum = min;
            n.Maximum = max;
            n.Value = value;
            n.Width = w;
            n.Location = new Point(x, y);
            return n;
        }

        // ---------- Performance pickers ----------

        // Core counts worth offering on this machine, coarse at the top end so a
        // 32-thread CPU doesn't produce a 32-entry dropdown.
        public static int[] CoreChoices()
        {
            int total = Environment.ProcessorCount;
            int[] candidates = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64 };
            List<int> list = new List<int>();
            foreach (int c in candidates)
                if (c < total) list.Add(c);
            return list.ToArray();
        }

        public static void FillCoreCombo(ThemedPicker c)
        {
            c.Items.Clear();
            c.Items.Add("All cores");
            foreach (int n in CoreChoices())
                c.Items.Add(n + (n == 1 ? " core" : " cores"));
            c.SelectedIndex = 0;
        }

        public static int SelectedCoreCount(ThemedPicker c)
        {
            int i = c.SelectedIndex;
            if (i <= 0) return 0;
            int[] choices = CoreChoices();
            return i - 1 < choices.Length ? choices[i - 1] : 0;
        }

        public static void SelectCoreCount(ThemedPicker c, int count)
        {
            if (count <= 0) { c.SelectedIndex = 0; return; }
            int[] choices = CoreChoices();
            for (int i = 0; i < choices.Length; i++)
                if (choices[i] == count) { c.SelectedIndex = i + 1; return; }
            c.SelectedIndex = 0;
        }

        public static void FillPriorityCombo(ThemedPicker c)
        {
            c.Items.Clear();
            foreach (string n in PerformanceManager.AllPriorityNames()) c.Items.Add(n);
            c.SelectedIndex = PerformanceManager.PRIORITY_NORMAL;
        }

        // A link centred in a row, instead of at a guessed offset. Every call
        // site used to add its own "+8" or "+9", which is where the one-pixel
        // drift between links and the labels beside them came from.
        public static LinkLabel RowLink(string text, int x, int rowTop, int rowHeight)
        {
            LinkLabel l = RowLink(text, x, rowTop);

            // Given the row's full height with its text centred inside, rather
            // than auto-sized and then nudged. Deterministic: there is nothing
            // to measure and nothing to get wrong, which is how ColumnLink has
            // always done it.
            Size t = TextRenderer.MeasureText(text, l.Font);
            l.AutoSize = false;
            l.Size = new Size(t.Width + 4, rowHeight);
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        public static LinkLabel RowLink(string text, int x, int y)
        {
            LinkLabel l = new LinkLabel();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            l.LinkColor = Theme.Accent;
            l.ActiveLinkColor = Theme.AccentHover;
            l.LinkBehavior = LinkBehavior.HoverUnderline;
            l.BackColor = Theme.Card;
            l.TabStop = false;
            return l;
        }

        // Fixed-width, centred version for the client list, so Tune and Show sit
        // in columns rather than wherever their text happens to end.
        public static LinkLabel ColumnLink(string text, int x, int rowTop, int rowHeight, int width)
        {
            LinkLabel l = RowLink(text, x, rowTop);
            l.AutoSize = false;
            l.Size = new Size(width, rowHeight);
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.Font = new Font("Segoe UI", 8.75f);
            return l;
        }
    }
}
