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
        public static Label SectionTitle(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(20, 16);
            l.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            l.ForeColor = Theme.Muted;
            l.BackColor = Theme.Card;
            return l;
        }

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
    }
}
