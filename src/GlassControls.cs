using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // A slider: drag it, click along it, or use the arrow keys. What it is
    // set to is said beside it in words, so no number has to be typed or
    // understood.
    class GlassSlider : Control
    {
        const int THUMB = 14, SAYS_W = 70;

        int min, max = 10, value;
        bool dragging;

        public Func<int, string> Says = delegate(int v) { return v.ToString(); };
        public event EventHandler ValueChanged;

        public GlassSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(240, 26);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int Minimum { get { return min; } set { min = value; Value = this.value; } }
        public int Maximum { get { return max; } set { max = Math.Max(min, value); Value = this.value; } }

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v == this.value) { Invalidate(); return; }
                this.value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        int TrackLeft { get { return THUMB / 2; } }
        int TrackWidth { get { return Math.Max(1, Width - SAYS_W - THUMB); } }

        // The value a point along the track stands for.
        public int ValueAt(int x)
        {
            double share = (x - TrackLeft) / (double)TrackWidth;
            share = share < 0 ? 0 : share > 1 ? 1 : share;
            return min + (int)Math.Round(share * (max - min));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            dragging = true;
            Value = ValueAt(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) Value = ValueAt(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Left || k == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) Value = value - 1;
            if (e.KeyCode == Keys.Right) Value = value + 1;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int mid = Height / 2;
            double share = max == min ? 0 : (value - min) / (double)(max - min);
            int x = TrackLeft + (int)Math.Round(share * TrackWidth);
            using (SolidBrush rail = new SolidBrush(Theme.Button)) g.FillRectangle(rail, TrackLeft, mid - 2, TrackWidth, 4);
            using (SolidBrush done = new SolidBrush(Enabled ? Theme.Accent : Theme.Muted)) g.FillRectangle(done, TrackLeft, mid - 2, x - TrackLeft, 4);
            using (SolidBrush thumb = new SolidBrush(Enabled ? (Focused ? Theme.AccentHover : Theme.Text) : Theme.Muted))
                g.FillEllipse(thumb, x - THUMB / 2, mid - THUMB / 2, THUMB, THUMB);
            using (Font f = Ui.F("Segoe UI", 8.25f))
                TextRenderer.DrawText(g, Says(value), f, new Rectangle(Width - SAYS_W + 8, 0, SAYS_W - 8, Height), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // Colours to click. The one in use has a ring round it.
    class GlassSwatches : Control
    {
        const int BOX = 22, STEP = 28;

        Color[] colours = new Color[0];
        Color chosen;

        public event EventHandler Picked;

        public GlassSwatches()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            Height = BOX + 4;
        }

        public Color[] Colours
        {
            get { return colours; }
            set { colours = value ?? new Color[0]; Width = colours.Length * STEP; Invalidate(); }
        }

        public Color Chosen
        {
            get { return chosen; }
            set { chosen = value; Invalidate(); }
        }

        public Rectangle BoundsOf(int i) { return new Rectangle(i * STEP + 2, 2, BOX, BOX); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            for (int i = 0; i < colours.Length; i++)
            {
                if (!BoundsOf(i).Contains(e.Location)) continue;
                chosen = colours[i];
                Invalidate();
                if (Picked != null) Picked(this, EventArgs.Empty);
                return;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < colours.Length; i++)
            {
                Rectangle r = BoundsOf(i);
                using (SolidBrush b = new SolidBrush(colours[i])) g.FillEllipse(b, r);
                using (Pen edge = new Pen(Theme.Button, 1f)) g.DrawEllipse(edge, r);
                if (colours[i].ToArgb() == chosen.ToArgb())
                    using (Pen ring = new Pen(Theme.Accent, 2f)) g.DrawEllipse(ring, r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4);
            }
        }
    }

    // The crosshair on a picture of the game's own middle when a game is
    // open - on a dark and a light half when none is - shown two ways.
    //
    // At real size a crosshair is a few pixels, too small to judge, so the
    // whole box shows it several times bigger, every pixel a square block
    // (nothing smoothed, so what is seen is what the screen will get). A
    // small "Real size" window in the corner shows it as it will really be.
    //
    // Both are made from one picture at real size, the crosshair put on it
    // by the same rule that puts it on the game (CrossPlace.TopLeft), so
    // the enlarged one is exactly that picture, bigger.
    class CrossPreview : Control
    {
        const int MOST = 4;                     // times bigger, when it fits
        const int INSET_W = 64, INSET_H = 52;   // the real-size window; even, so its middle is the scene's
        const int CAPTION_H = 16, MARGIN = 6;

        CrossStyle style = new CrossStyle();
        Bitmap game;
        string problem;
        int enlarged = MOST;

        public CrossPreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public string Problem { get { return problem; } }
        public int Enlarged { get { return enlarged; } }

        public void Show(CrossStyle s)
        {
            style = s.Clone();
            enlarged = EnlargeFor(style, ClientSize);
            Invalidate();
        }

        public void SetGame(Bitmap picture)
        {
            if (game != null) game.Dispose();
            game = picture;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            enlarged = EnlargeFor(style, ClientSize);
        }

        // How far the crosshair reaches from its middle, its dark edge included.
        public static int Reach(CrossStyle s)
        {
            int edge = s.Outline ? 1 : 0;
            switch (s.Shape)
            {
                case CrossShape.Dot: return s.Size + edge;
                case CrossShape.Circle: return s.Size + (s.Thick + 1) / 2 + edge;
                case CrossShape.Picture: return (s.PictureSize + 1) / 2;
                default: return s.Gap + s.Size + (s.Thick + 1) / 2 + edge;
            }
        }

        // How many times bigger it can be shown in a box this size and still
        // be whole, moved off the middle as it is, and clear of the
        // real-size window in the corner: up to 4, never under 1.
        public static int EnlargeFor(CrossStyle s, Size box)
        {
            int r = Reach(s);
            Rectangle inset = InsetFrame(box);
            inset.Inflate(2, 2);
            for (int z = MOST; z > 1; z--)
            {
                int cx = box.Width / 2 + s.NudgeX * z, cy = box.Height / 2 + s.NudgeY * z, R = r * z;
                if (cx - R < MARGIN || cx + R > box.Width - MARGIN || cy - R < MARGIN || cy + R > box.Height - MARGIN) continue;
                bool clear;
                if (s.Shape == CrossShape.Cross || s.Shape == CrossShape.Tee)
                {
                    // Only the arms: two bars through the middle.
                    int a = z * ((s.Thick + 1) / 2 + (s.Outline ? 1 : 0));
                    clear = !inset.IntersectsWith(Rectangle.FromLTRB(cx - R, cy - a, cx + R, cy + a))
                         && !inset.IntersectsWith(Rectangle.FromLTRB(cx - a, cy - R, cx + a, cy + R));
                }
                else if (s.Shape == CrossShape.Picture) clear = !inset.IntersectsWith(Rectangle.FromLTRB(cx - R, cy - R, cx + R, cy + R));
                else
                {
                    // A round shape: its distance from the window's nearest point.
                    int nx = Math.Max(inset.Left, Math.Min(cx, inset.Right)), ny = Math.Max(inset.Top, Math.Min(cy, inset.Bottom));
                    long dx = cx - nx, dy = cy - ny;
                    clear = dx * dx + dy * dy > (long)R * R;
                }
                if (clear) return z;
            }
            return 1;
        }

        // The real-size window with its caption, in the bottom-right corner.
        static Rectangle InsetFrame(Size box)
        {
            return new Rectangle(box.Width - MARGIN - INSET_W - 1, box.Height - MARGIN - INSET_H - 1 - CAPTION_H, INSET_W + 2, INSET_H + 2 + CAPTION_H);
        }

        // The box's own picture at real size: the background, and the
        // crosshair on its middle as it would be on a game this size.
        Bitmap Scene(int w, int h)
        {
            Bitmap scene = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(scene))
            {
                if (game != null)
                {
                    g.Clear(Color.Black);
                    g.DrawImageUnscaled(game, (w - game.Width) / 2, (h - game.Height) / 2);
                }
                else
                {
                    using (SolidBrush dark = new SolidBrush(Color.FromArgb(34, 40, 52))) g.FillRectangle(dark, 0, 0, w / 2, h);
                    using (SolidBrush light = new SolidBrush(Color.FromArgb(214, 222, 232))) g.FillRectangle(light, w / 2, 0, w - w / 2, h);
                }
                bool evenWide = w % 2 == 0, evenHigh = h % 2 == 0;
                using (Bitmap b = CrossArt.Draw(style, evenWide, evenHigh, out problem))
                {
                    if (b != null)
                    {
                        Point at = CrossPlace.TopLeft(new Rectangle(0, 0, w, h), CrossArt.Middle2(style, evenWide, evenHigh), style.NudgeX, style.NudgeY);
                        System.Drawing.Imaging.ColorMatrix fade = new System.Drawing.Imaging.ColorMatrix();
                        fade.Matrix33 = style.Opacity / 100f;
                        using (System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes())
                        {
                            ia.SetColorMatrix(fade);
                            g.DrawImage(b, new Rectangle(at.X, at.Y, b.Width, b.Height), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
                        }
                    }
                }
            }
            return scene;
        }

        // Every pixel made a z-by-z block, about the middle: the middle of
        // the picture stays the middle. Done by hand, not by GDI+, which
        // shifts a scaled picture by half a source pixel.
        public static Bitmap Enlarge(Bitmap src, int z)
        {
            int w = src.Width, h = src.Height;
            Bitmap dst = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Rectangle all = new Rectangle(0, 0, w, h);
            int[] from = new int[w * h], to = new int[w * h];
            System.Drawing.Imaging.BitmapData d = src.LockBits(all, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try { System.Runtime.InteropServices.Marshal.Copy(d.Scan0, from, 0, from.Length); }
            finally { src.UnlockBits(d); }
            int[] col = new int[w];
            for (int x = 0; x < w; x++) col[x] = Source(x, w, z);
            for (int y = 0; y < h; y++)
            {
                int row = Source(y, h, z) * w, at = y * w;
                for (int x = 0; x < w; x++) to[at + x] = from[row + col[x]];
            }
            d = dst.LockBits(all, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try { System.Runtime.InteropServices.Marshal.Copy(to, 0, d.Scan0, to.Length); }
            finally { dst.UnlockBits(d); }
            return dst;
        }

        // The pixel of a line n long that pixel i of the enlarged one shows.
        // In half pixels, pixel i's middle is 2i + 1 and the line's middle n;
        // the distance between them shrinks z times.
        static int Source(int i, int n, int z)
        {
            int s2 = n + FloorDiv(2 * i + 1 - n, z);       // the source point, in half pixels
            int s = FloorDiv(s2, 2);
            return s < 0 ? 0 : s >= n ? n - 1 : s;
        }

        static int FloorDiv(int a, int b)
        {
            int q = a / b;
            return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = Width, h = Height;
            using (Bitmap scene = Scene(w, h))
            {
                int z = enlarged;
                if (z > 1)
                {
                    using (Bitmap big = Enlarge(scene, z)) g.DrawImageUnscaled(big, 0, 0);
                    Caption(g, new Rectangle(1, 1, 0, CAPTION_H), "Shown " + z + "x bigger");

                    // The real-size window, its middle the scene's middle.
                    Rectangle inset = new Rectangle(w - MARGIN - INSET_W, h - MARGIN - INSET_H, INSET_W, INSET_H);
                    using (Bitmap real = scene.Clone(new Rectangle(w / 2 - INSET_W / 2, h / 2 - INSET_H / 2, INSET_W, INSET_H), scene.PixelFormat))
                        g.DrawImageUnscaled(real, inset.X, inset.Y);
                    using (Pen frame = new Pen(Theme.Card, 1f)) g.DrawRectangle(frame, inset.X - 1, inset.Y - 1, inset.Width + 1, inset.Height + 1);
                    Caption(g, new Rectangle(inset.X - 1, inset.Y - 1 - CAPTION_H, inset.Width + 2, CAPTION_H), "Real size");
                }
                else
                {
                    g.DrawImageUnscaled(scene, 0, 0);
                    Caption(g, new Rectangle(1, 1, 0, CAPTION_H), "Real size");
                }
            }
            using (Pen edge = new Pen(Theme.Button, 1f)) g.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
        }

        // A label on a strip of the card's colour, so it reads on any game.
        // A width of 0 is as wide as the words.
        static void Caption(Graphics g, Rectangle r, string text)
        {
            using (Font f = Ui.F("Segoe UI", 7.5f))
            {
                if (r.Width == 0) r.Width = TextRenderer.MeasureText(text, f, Size.Empty, TextFormatFlags.NoPadding).Width + 12;
                using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillRectangle(b, r);
                TextRenderer.DrawText(g, text, f, r, Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && game != null) { game.Dispose(); game = null; }
            base.Dispose(disposing);
        }
    }

    // A button that is a key: press it, then press the key wanted.
    static class KeyPick
    {
        public static string NameOf(byte vk, string none)
        {
            return vk == 0 ? none : MacroKeys.Name(vk);
        }

        // The key pressed, or 0 when the window was closed without one.
        public static byte Ask(IWin32Window owner)
        {
            using (KeyCaptureDialog d = new KeyCaptureDialog(true))
                return d.ShowDialog(owner) == DialogResult.OK ? d.Captured : (byte)0;
        }
    }
}
