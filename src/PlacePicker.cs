using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // Choosing a place on a client by clicking it on a picture of the client,
    // rather than typing how far across and down it is.
    static class PlacePick
    {
        // A point on a picture of the whole window, as a place in the game's
        // part of it - which is what a click step is kept as. The picture has
        // the title bar and frame in it; the game's part does not, and on a
        // scaled display the picture is not the size the window is on screen.
        // False when the point is on the frame, outside the game.
        public static bool FromPicture(Size picture, Rectangle window, Rectangle client, Point at,
                                       out double fx, out double fy)
        {
            fx = fy = 0;
            if (picture.Width <= 1 || picture.Height <= 1 || client.Width <= 1 || client.Height <= 1) return false;
            double sx = window.Left + at.X * (window.Width - 1) / (double)(picture.Width - 1);
            double sy = window.Top + at.Y * (window.Height - 1) / (double)(picture.Height - 1);
            fx = (sx - client.Left) / (client.Width - 1);
            fy = (sy - client.Top) / (client.Height - 1);
            bool inside = fx >= 0 && fx <= 1 && fy >= 0 && fy <= 1;
            if (fx < 0) fx = 0; if (fx > 1) fx = 1;
            if (fy < 0) fy = 0; if (fy > 1) fy = 1;
            return inside;
        }

        // The other way: where a place in the game is on the picture, to mark it.
        public static Point ToPicture(Size picture, Rectangle window, Rectangle client, double fx, double fy)
        {
            if (window.Width <= 1 || window.Height <= 1) return Point.Empty;
            double sx = client.Left + fx * (client.Width - 1);
            double sy = client.Top + fy * (client.Height - 1);
            return new Point((int)Math.Round((sx - window.Left) * (picture.Width - 1) / (double)(window.Width - 1)),
                             (int)Math.Round((sy - window.Top) * (picture.Height - 1) / (double)(window.Height - 1)));
        }

        public static string Describe(double fx, double fy)
        {
            return ((int)Math.Round(fx * 100)) + "% across, " + ((int)Math.Round(fy * 100)) + "% down";
        }

        // Asks which client when there are several, then for the place on it.
        // done gets across and down - and for two places, the second's too.
        public static void Ask(Form owner, WatchKit kit, Control under, bool twoPlaces, double[] now,
                               Action<double[]> done, Action<string> say)
        {
            WatchedClient[] clients = kit == null ? new WatchedClient[0] : kit.RunningClients();
            if (clients.Length == 0) { say("Open a Roblox window first. You pick the spot on a picture of it."); return; }
            if (clients.Length == 1) { Show(owner, clients[0], twoPlaces, now, done, say); return; }

            ContextMenuStrip menu = DarkMenu.Make();
            foreach (WatchedClient c in clients)
            {
                WatchedClient client = c;
                menu.Items.Add("On " + WatchUi.ClientName(c), null, delegate { Show(owner, client, twoPlaces, now, done, say); });
            }
            menu.Show(under, new Point(0, under.Height));
        }

        static void Show(Form owner, WatchedClient c, bool twoPlaces, double[] now, Action<double[]> done, Action<string> say)
        {
            IntPtr hwnd = WindowCapture.GameWindow(c.Pid);
            Pixels still = Native.IsIconic(hwnd) ? null : WindowCapture.Grab(hwnd);
            if (still == null || still.IsEmpty)
            {
                say("Couldn't see " + c.Label + ". If it's minimized, bring it back up first.");
                return;
            }
            Native.RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) { say("That Roblox window has closed."); return; }
            Rectangle window = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            Native.RECT cr;
            Native.GetClientRect(hwnd, out cr);
            Native.POINT origin = new Native.POINT();
            Native.ClientToScreen(hwnd, ref origin);
            Rectangle client = new Rectangle(origin.X, origin.Y, cr.Right - cr.Left, cr.Bottom - cr.Top);

            using (PlacePickerForm f = new PlacePickerForm(still, window, client, WatchUi.ClientName(c), twoPlaces, now))
                if (f.ShowDialog(owner) == DialogResult.OK && f.Picked != null) done(f.Picked);
        }
    }

    // A picture of the client to click on. One click chooses a place; for a
    // drag, the first click is where it starts and the second where it ends.
    class PlacePickerForm : Form
    {
        const int M = 12;
        const int TOP = WatchUi.TITLEBAR_H;
        const int FOOT_H = 56;

        readonly Size picture;
        readonly Rectangle window, client;
        readonly bool two;
        readonly Bitmap shown;
        readonly double scale;
        readonly List<double[]> places = new List<double[]>();     // across, down
        View view;
        Label lblSays;
        Button btnUse;

        // Across and down - and for two places, across and down of the second.
        public double[] Picked { get; private set; }

        public PlacePickerForm(Pixels still, Rectangle window, Rectangle client, string clientLabel,
                               bool twoPlaces, double[] now)
        {
            picture = new Size(still.Width, still.Height);
            this.window = window;
            this.client = client;
            two = twoPlaces;
            shown = still.ToBitmap();

            // Shrunk to fit the screen with room for the buttons under it.
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            int roomW = Math.Max(320, Math.Min(1400, work.Width - M * 2 - 60));
            int roomH = Math.Max(240, Math.Min(860, work.Height - TOP - FOOT_H - 80));
            scale = Math.Min(1.0, Math.Min(roomW / (double)still.Width, roomH / (double)still.Height));
            Size viewSize = new Size(Math.Max(1, (int)Math.Round(still.Width * scale)),
                                     Math.Max(1, (int)Math.Round(still.Height * scale)));

            WatchUi.Frame(this, (two ? "Click where the drag starts, then where it ends - " : "Click the spot - ") + clientLabel,
                          Math.Max(520, viewSize.Width + M * 2), TOP + viewSize.Height + FOOT_H);
            Build(viewSize);

            if (now != null && now.Length >= 2) places.Add(new double[] { now[0], now[1] });
            if (two && now != null && now.Length >= 4) places.Add(new double[] { now[2], now[3] });
            // What is there already is only shown, not taken as chosen: the
            // window was opened to choose again.
            ShowState(false);
        }

        public int PlacesChosen { get { return places.Count; } }
        public Size ViewSize { get { return view.Size; } }
        public string Says { get { return lblSays.Text; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing) shown.Dispose();
            base.Dispose(disposing);
        }

        void Build(Size viewSize)
        {
            view = new View(this);
            view.Location = new Point((ClientSize.Width - viewSize.Width) / 2, TOP);
            view.Size = viewSize;
            view.Cursor = Cursors.Cross;
            view.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ClickAt(e.Location);
            };
            Controls.Add(view);

            int y = TOP + viewSize.Height + 12;
            lblSays = Ui.RowLabel("", M + 4, y, 32, ClientSize.Width - M * 2 - 230, 9f, Theme.Muted);
            lblSays.BackColor = Theme.Bg;
            Controls.Add(lblSays);

            Button cancel = WatchUi.Secondary("Cancel", ClientSize.Width - M - 120 - 8 - 90, y, 90, 32);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            btnUse = WatchUi.Primary(two ? "Use these" : "Use this spot", ClientSize.Width - M - 120, y, 120, 32);
            btnUse.Click += delegate { if (Accept()) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(btnUse);
            CancelButton = cancel;
            AcceptButton = btnUse;
        }

        bool fresh;     // places were clicked in this window, not carried in

        // A click on the picture, in the picture's own shrunken pixels.
        public void ClickAt(Point onView)
        {
            Point at = new Point((int)Math.Round(onView.X / scale), (int)Math.Round(onView.Y / scale));
            double fx, fy;
            if (!PlacePick.FromPicture(picture, window, client, at, out fx, out fy))
            {
                lblSays.Text = "That's the window's frame. Click inside the game.";
                lblSays.ForeColor = Theme.Amber;
                return;
            }
            // A click after everything is chosen starts again.
            if (!fresh || places.Count >= (two ? 2 : 1)) places.Clear();
            fresh = true;
            places.Add(new double[] { fx, fy });
            ShowState(true);
        }

        void ShowState(bool chosen)
        {
            int need = two ? 2 : 1;
            bool ready = chosen && places.Count >= need;
            btnUse.Enabled = ready;
            lblSays.ForeColor = Theme.Muted;
            if (!chosen)
                lblSays.Text = two ? "Click where the drag starts." : "Click the spot on the picture.";
            else if (!ready)
                lblSays.Text = "Now click where the drag ends.";
            else if (two)
                lblSays.Text = "From " + PlacePick.Describe(places[0][0], places[0][1]) + " to "
                             + PlacePick.Describe(places[1][0], places[1][1]) + ". Click again to start over.";
            else
                lblSays.Text = PlacePick.Describe(places[0][0], places[0][1]) + ". Click somewhere else to change it.";
            view.Invalidate();
        }

        public bool Accept()
        {
            int need = two ? 2 : 1;
            if (!fresh || places.Count < need) return false;
            Picked = two ? new double[] { places[0][0], places[0][1], places[1][0], places[1][1] }
                         : new double[] { places[0][0], places[0][1] };
            return true;
        }

        // The picture, with a ring on each chosen place and a line between two.
        class View : Panel
        {
            readonly PlacePickerForm form;

            public View(PlacePickerForm form)
            {
                this.form = form;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                       | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(form.shown, new Rectangle(0, 0, Width, Height));
                g.SmoothingMode = SmoothingMode.AntiAlias;

                List<Point> at = new List<Point>();
                foreach (double[] p in form.places)
                {
                    Point onPicture = PlacePick.ToPicture(form.picture, form.window, form.client, p[0], p[1]);
                    at.Add(new Point((int)Math.Round(onPicture.X * form.scale), (int)Math.Round(onPicture.Y * form.scale)));
                }
                if (at.Count == 2)
                    using (Pen line = new Pen(Theme.AccentHover, 2f))
                    {
                        line.DashStyle = DashStyle.Dash;
                        g.DrawLine(line, at[0], at[1]);
                    }
                for (int i = 0; i < at.Count; i++)
                {
                    // Dark under light, so it shows on any scenery.
                    using (Pen under = new Pen(Color.FromArgb(200, 0, 0, 0), 5f))
                        g.DrawEllipse(under, at[i].X - 11, at[i].Y - 11, 22, 22);
                    using (Pen ring = new Pen(i == 0 ? Theme.Accent : Theme.Green, 3f))
                        g.DrawEllipse(ring, at[i].X - 11, at[i].Y - 11, 22, 22);
                    using (SolidBrush dot = new SolidBrush(Color.White))
                        g.FillEllipse(dot, at[i].X - 2, at[i].Y - 2, 5, 5);
                }
            }
        }
    }
}
