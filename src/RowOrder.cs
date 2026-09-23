using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RobloxKeeper
{
    enum RowMove { Top, Up, Down, Bottom }

    // The rules behind putting a list in order, apart from the grip that
    // drives them so they can be tested.
    static class RowOrder
    {
        // The gap between rows nearest the pointer: 0 above the first row,
        // count below the last.
        public static int SlotAt(int contentY, int rowHeight, int count)
        {
            if (rowHeight <= 0) return 0;
            int slot = (int)Math.Floor((contentY + rowHeight / 2.0) / rowHeight);
            return Math.Max(0, Math.Min(count, slot));
        }

        // Where a row dropped in a gap ends up. A gap below where it was is one
        // higher once the row has left its old place.
        public static int TargetIndex(int from, int slot)
        {
            return slot > from ? slot - 1 : slot;
        }

        public static bool Move<T>(IList<T> list, int from, int to)
        {
            if (from < 0 || from >= list.Count || to < 0 || to >= list.Count || from == to) return false;
            T item = list[from];
            list.RemoveAt(from);
            list.Insert(to, item);
            return true;
        }

        // Where a menu choice takes the row; -1 when it is already there.
        public static int MenuTarget(int from, int count, RowMove move)
        {
            int to;
            switch (move)
            {
                case RowMove.Top: to = 0; break;
                case RowMove.Up: to = from - 1; break;
                case RowMove.Down: to = from + 1; break;
                default: to = count - 1; break;
            }
            return to < 0 || to >= count || to == from ? -1 : to;
        }

        // "X copy", then "X copy 2" and on - macros and watchers are picked by
        // name, so a copy can't share one.
        public static string CopyName(string name, Func<string, bool> taken)
        {
            string first = (name ?? "").Trim() + " copy";
            if (!taken(first)) return first;
            for (int n = 2; ; n++)
            {
                string next = first + " " + n;
                if (!taken(next)) return next;
            }
        }
    }

    // The handle at the start of a row: drag it to move the row, right-click
    // it to move the row to the top, up, down or to the bottom - or to make a
    // copy, where the list has copies.
    //
    // It lives in the list's scroll panel beside the row's other controls.
    // Moving tells the list, which rebuilds - so what was asked is handed on
    // after this grip's own event has finished, never from inside it.
    class RowGrip : Control
    {
        public int Index, Count, RowHeight, TopOffset;
        public Action<int, int> Moved;
        public Action<int> Copied;           // null: the list has no copies

        bool pressed, dragging, hover;
        Point pressedAt;
        Panel line;
        int slot = -1;

        public RowGrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(16, 28);
            BackColor = Theme.Card;
            Cursor = Cursors.SizeAll;
            TabStop = false;
        }

        public static RowGrip For(int index, int count, int rowTop, int rowHeight, int topOffset,
                                  Action<int, int> moved, Action<int> copied)
        {
            RowGrip g = new RowGrip();
            g.Index = index;
            g.Count = count;
            g.RowHeight = rowHeight;
            g.TopOffset = topOffset;
            g.Moved = moved;
            g.Copied = copied;
            g.Location = new Point(0, rowTop);
            g.Size = new Size(16, rowHeight);
            g.tip = new ToolTip();
            g.tip.SetToolTip(g, "Drag to move it - or right-click to move it up, down, "
                + "to the top or bottom" + (copied != null ? ", or make a copy." : "."));
            return g;
        }

        // Its own tooltip goes with it: lists rebuild on every change, and
        // each tooltip left behind is a window left behind.
        ToolTip tip;

        protected override void Dispose(bool disposing)
        {
            if (disposing && tip != null) { tip.Dispose(); tip = null; }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            Color dot = hover || dragging ? Theme.Text : Theme.Muted;
            int cx = Width / 2, cy = Height / 2;
            using (SolidBrush b = new SolidBrush(dot))
                for (int col = 0; col < 2; col++)
                    for (int row = 0; row < 3; row++)
                        e.Graphics.FillRectangle(b, cx - 3 + col * 4, cy - 5 + row * 4, 2, 2);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { ShowMenu(e.Location); return; }
            if (e.Button != MouseButtons.Left || Count < 2) return;
            pressed = true;
            pressedAt = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!pressed) return;
            if (!dragging && Math.Abs(e.Y - pressedAt.Y) < 4) return;
            dragging = true;
            ScrollableControl list = Parent as ScrollableControl;
            if (list == null) return;

            int visibleY = Top + e.Y;           // the grip's own position in the list, plus the pointer's in the grip
            // Near an edge of a list taller than its box, scroll it along.
            if (visibleY < 0 || visibleY > list.ClientSize.Height)
            {
                int now = -list.AutoScrollPosition.Y;
                list.AutoScrollPosition = new Point(0, Math.Max(0, now + (visibleY < 0 ? -RowHeight : RowHeight)));
            }

            int contentY = visibleY - list.AutoScrollPosition.Y - TopOffset;
            slot = RowOrder.SlotAt(contentY, RowHeight, Count);
            if (line == null)
            {
                line = new Panel();
                line.BackColor = Theme.Accent;
                list.Controls.Add(line);
                line.BringToFront();
            }
            line.Bounds = new Rectangle(0, slot * RowHeight + list.AutoScrollPosition.Y + TopOffset - 1,
                                        list.ClientSize.Width, 2);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool wasDragging = dragging;
            pressed = dragging = false;
            if (line != null) { line.Parent.Controls.Remove(line); line.Dispose(); line = null; }
            Invalidate();
            if (!wasDragging || slot < 0) return;
            int to = RowOrder.TargetIndex(Index, slot);
            slot = -1;
            if (to != Index && to >= 0 && to < Count) Later(delegate { Moved(Index, to); });
        }

        void ShowMenu(Point at)
        {
            ContextMenuStrip menu = DarkMenu.Make();
            AddMove(menu, "Move to the top", RowMove.Top);
            AddMove(menu, "Move up", RowMove.Up);
            AddMove(menu, "Move down", RowMove.Down);
            AddMove(menu, "Move to the bottom", RowMove.Bottom);
            if (Copied != null)
            {
                menu.Items.Add(new ToolStripSeparator());
                int index = Index;
                menu.Items.Add("Make a copy", null, delegate { Later(delegate { Copied(index); }); });
            }
            menu.Closed += delegate { BeginInvoke((MethodInvoker)delegate { menu.Dispose(); }); };
            menu.Show(this, at);
        }

        void AddMove(ContextMenuStrip menu, string text, RowMove move)
        {
            int from = Index;
            int to = RowOrder.MenuTarget(from, Count, move);
            ToolStripItem item = menu.Items.Add(text, null, delegate { Later(delegate { Moved(from, to); }); });
            item.Enabled = to >= 0;
        }

        // After this event, on the window's own thread: the move rebuilds the
        // list, and this grip goes with it.
        void Later(MethodInvoker a)
        {
            Form f = FindForm();
            if (f != null && f.IsHandleCreated) f.BeginInvoke(a);
            else a();
        }
    }

    // The right-click menu in the app's colours, instead of Windows' white.
    static class DarkMenu
    {
        public static ContextMenuStrip Make()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new ToolStripProfessionalRenderer(new Colours());
            m.BackColor = Theme.Card;
            m.ForeColor = Theme.Text;
            m.ShowImageMargin = false;
            m.Font = new Font("Segoe UI", 9f);
            return m;
        }

        class Colours : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
            public override Color MenuBorder { get { return Color.FromArgb(58, 58, 80); } }
            public override Color MenuItemBorder { get { return Theme.Button; } }
            public override Color MenuItemSelected { get { return Theme.Button; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.Button; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.Button; } }
            public override Color SeparatorDark { get { return Color.FromArgb(58, 58, 80); } }
            public override Color SeparatorLight { get { return Theme.Card; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        }
    }
}
