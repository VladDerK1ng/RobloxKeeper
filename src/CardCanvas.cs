using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace RobloxKeeper
{
    // One thing on a page: a game, a client, a friend, a screenshot.
    class CardItem
    {
        public string Title, Line;
        public string ImageUrl;         // fetched and kept by Thumbs
        public Bitmap Image;            // or a picture the page already has
        public string Badge;            // a word in the picture's corner: "playing"
        public bool Good;               // the badge, or a row's dot, in green
        public object Tag;              // whatever the page needs back when it is clicked
    }

    // A titled group of them.
    class CardSection
    {
        public string Title, Note;      // the note sits beside the title, muted
        public string Empty;            // said when there is nothing in it
        public string Link;             // an action at the title's right: "See all"
        public bool Rows;               // one to a line, small picture - friends - rather than cards
        public int MaxRows;             // of cards; 0 for all of them
        public readonly List<CardItem> Items = new List<CardItem>();
    }

    // Where each thing is drawn, worked out apart from the drawing so it can
    // be tested: so many cards across, wrapped, section under section.
    static class CardLayout
    {
        public const int PAD = 20, GAP = 14;
        public const int CARD_W = 158, PICTURE = 158, CARD_H = PICTURE + 50;
        public const int ROW_H = 52;
        public const int TITLE_H = 34, SECTION_GAP = 18;

        public class Place
        {
            public CardSection Section;
            public CardItem Item;       // null for a section's title, or its empty line
            public Rectangle Bounds;
            public bool IsLink, IsEmpty;
        }

        public static int Across(int width)
        {
            return Math.Max(1, (width - PAD * 2 + GAP) / (CARD_W + GAP));
        }

        // Everything's place for a page this wide. height comes back as how
        // far down it all goes.
        public static List<Place> Lay(IList<CardSection> sections, int width, out int height)
        {
            List<Place> places = new List<Place>();
            int y = PAD;
            int across = Across(width);
            foreach (CardSection s in sections)
            {
                Place title = new Place();
                title.Section = s;
                title.Bounds = new Rectangle(PAD, y, width - PAD * 2, TITLE_H);
                places.Add(title);
                if (!string.IsNullOrEmpty(s.Link))
                {
                    Place link = new Place();
                    link.Section = s;
                    link.IsLink = true;
                    link.Bounds = new Rectangle(width - PAD - 120, y, 120, TITLE_H);
                    places.Add(link);
                }
                y += TITLE_H;

                if (s.Items.Count == 0)
                {
                    Place none = new Place();
                    none.Section = s;
                    none.IsEmpty = true;
                    none.Bounds = new Rectangle(PAD, y, width - PAD * 2, 36);
                    places.Add(none);
                    y += 36 + SECTION_GAP;
                    continue;
                }

                if (s.Rows)
                {
                    foreach (CardItem item in s.Items)
                    {
                        Place p = new Place();
                        p.Section = s;
                        p.Item = item;
                        p.Bounds = new Rectangle(PAD, y, width - PAD * 2, ROW_H);
                        places.Add(p);
                        y += ROW_H + 4;
                    }
                    y += SECTION_GAP;
                    continue;
                }

                int shown = s.MaxRows > 0 ? Math.Min(s.Items.Count, s.MaxRows * across) : s.Items.Count;
                for (int i = 0; i < shown; i++)
                {
                    Place p = new Place();
                    p.Section = s;
                    p.Item = s.Items[i];
                    p.Bounds = new Rectangle(PAD + (i % across) * (CARD_W + GAP), y + (i / across) * (CARD_H + GAP), CARD_W, CARD_H);
                    places.Add(p);
                }
                int rows = (shown + across - 1) / across;
                y += rows * (CARD_H + GAP) - GAP + SECTION_GAP;
            }
            height = y;
            return places;
        }

        // How far a page may be scrolled, and no further.
        public static int ClampScroll(int scroll, int contentHeight, int viewHeight)
        {
            int most = Math.Max(0, contentHeight - viewHeight);
            return scroll < 0 ? 0 : scroll > most ? most : scroll;
        }
    }

    // Pictures already scaled to the size they are drawn at, so a frame of
    // scrolling is a plain copy of each instead of a smooth resampling of it.
    //
    // Resampling a 150-pixel icon (or a 768-pixel screenshot) with the
    // high-quality filter, through a rounded clip, for every card on every
    // frame of a scroll was most of the time a frame took: a scroll glide
    // needs a frame in about 16 ms and a page of cards took 10 to 40.
    //
    // Kept by which Bitmap it came from and the size wanted: a different
    // picture is a different Bitmap, and a different size (a row became a
    // card, say) is made again. What decides to keep, rebuild or let go is
    // here, free of any drawing, so it can be tested.
    class PictureCache
    {
        class Entry
        {
            public Bitmap Picture;
            public Size Size;
            public long Used;           // the paint that last asked for it
        }

        readonly Dictionary<Bitmap, Entry> kept = new Dictionary<Bitmap, Entry>();
        readonly Func<Bitmap, Size, Bitmap> make;
        readonly int limit;
        long paint;

        public PictureCache(int limit, Func<Bitmap, Size, Bitmap> make)
        {
            this.limit = limit;
            this.make = make;
        }

        public int Count { get { return kept.Count; } }

        // A new frame is about to be drawn.
        public void BeginPaint()
        {
            paint++;
        }

        // The source scaled to this size: the kept one if it still fits,
        // otherwise made again and the old one let go.
        public Bitmap Get(Bitmap source, Size size)
        {
            Entry e;
            if (kept.TryGetValue(source, out e))
            {
                if (e.Size == size) { e.Used = paint; return e.Picture; }
                e.Picture.Dispose();
                kept.Remove(source);
            }
            e = new Entry();
            e.Picture = make(source, size);
            e.Size = size;
            e.Used = paint;
            kept[source] = e;
            return e.Picture;
        }

        // After a frame: if more are held than the limit, let go the ones
        // not used for longest - never one this frame drew, however many
        // that is, or the next frame would only make it again.
        public void Trim()
        {
            if (kept.Count <= limit) return;
            List<KeyValuePair<Bitmap, Entry>> idle = new List<KeyValuePair<Bitmap, Entry>>();
            foreach (KeyValuePair<Bitmap, Entry> kv in kept)
                if (kv.Value.Used != paint) idle.Add(kv);
            idle.Sort(delegate(KeyValuePair<Bitmap, Entry> a, KeyValuePair<Bitmap, Entry> b)
            {
                return a.Value.Used.CompareTo(b.Value.Used);
            });
            for (int i = 0; i < idle.Count && kept.Count > limit; i++)
            {
                idle[i].Value.Picture.Dispose();
                kept.Remove(idle[i].Key);
            }
        }

        // Nothing is showing, or the canvas is gone: this app sits beside
        // Roblox clients and holds no pictures it is not drawing.
        public void Clear()
        {
            foreach (Entry e in kept.Values) e.Picture.Dispose();
            kept.Clear();
        }
    }

    // A page of cards, drawn by the app itself: sections of games, clients,
    // friends or pictures, scrolled with the wheel, lit under the pointer.
    //
    // One control paints all of it. A window of a hundred games as a hundred
    // picture boxes and two hundred labels is slow to build and slower to
    // scroll; this is one surface, and it only paints what is in view. Nothing
    // ticks while nothing moves.
    class CardCanvas : Control
    {
        readonly List<CardSection> sections = new List<CardSection>();
        List<CardLayout.Place> places = new List<CardLayout.Place>();
        int contentHeight;
        int scroll;
        CardLayout.Place hot;
        readonly Anim glide = new Anim(0);          // 0..1 towards scrollTo
        int scrollFrom, scrollTo;
        bool draggingBar;
        int dragOffset;

        readonly Font titleFont = Ui.F("Segoe UI Semibold", 11f);
        readonly Font nameFont = Ui.F("Segoe UI Semibold", 9f);
        readonly Font lineFont = Ui.F("Segoe UI", 8.25f);
        readonly Font badgeFont = Ui.F("Segoe UI Semibold", 7.5f);

        // A page shows about twenty cards; a few screens either side are
        // kept so scrolling back is as quick as scrolling on.
        readonly PictureCache cardPictures = new PictureCache(48, MakeCardPicture);
        readonly PictureCache faces = new PictureCache(64, MakeFace);

        // An item was clicked; the point is on the screen, for a menu.
        public event Action<CardItem, Point> ItemClicked;
        public event Action<CardSection> LinkClicked;

        public CardCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Bg;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Animator.Remove(this);
                titleFont.Dispose(); nameFont.Dispose(); lineFont.Dispose(); badgeFont.Dispose();
                cardPictures.Clear(); faces.Clear();
            }
            base.Dispose(disposing);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // Thumbs lets go of its pictures when the window goes to the tray;
            // the scaled copies here would only be the same memory again.
            if (!Visible) { cardPictures.Clear(); faces.Clear(); }
        }

        public IList<CardSection> Sections { get { return sections; } }
        public int ContentHeight { get { return contentHeight; } }
        public int ScrollTop { get { return scroll; } }

        // What the page shows now. Where it was scrolled to is kept if it
        // still makes sense - a list that refreshes shouldn't jump to the top.
        public void Show(IList<CardSection> now)
        {
            sections.Clear();
            sections.AddRange(now);
            Relay();
        }

        void Relay()
        {
            places = CardLayout.Lay(sections, Math.Max(1, Width - BAR_W), out contentHeight);
            scroll = CardLayout.ClampScroll(scroll, contentHeight, Height);
            hot = null;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relay();
        }

        // ---------- scrolling ----------

        const int BAR_W = 10;
        const int WHEEL_STEP = 110;

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            // The wheel goes to whatever has the keyboard - but not out from
            // under someone typing in the search box.
            Form form = FindForm();
            if (form != null && form.ActiveControl is TextBox) return;
            if (CanFocus && !Focused) Focus();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int target = glide.Running ? scrollTo : scroll;
            ScrollTo(target - Math.Sign(e.Delta) * WHEEL_STEP, true);
        }

        public void ScrollTo(int where, bool smooth)
        {
            where = CardLayout.ClampScroll(where, contentHeight, Height);
            if (!smooth || !Animator.MotionWanted)
            {
                scroll = where;
                Invalidate();
                return;
            }
            scrollFrom = scroll;
            scrollTo = where;
            // Started again from nought each time: a second notch of the wheel
            // carries on from where the first had got to.
            glide.To(0, TimeSpan.Zero, DateTime.Now);
            glide.To(1, Animator.Time(Animator.Quick), DateTime.Now);
            Animator.Run(this, glide, delegate
            {
                scroll = (int)Math.Round(scrollFrom + (scrollTo - scrollFrom) * glide.Value);
                Invalidate();
            });
        }

        Rectangle BarThumb()
        {
            if (contentHeight <= Height) return Rectangle.Empty;
            int h = Math.Max(30, Height * Height / contentHeight);
            int y = (int)((long)(Height - h) * scroll / (contentHeight - Height));
            return new Rectangle(Width - BAR_W + 2, y, BAR_W - 4, h);
        }

        // ---------- the pointer ----------

        CardLayout.Place At(Point p)
        {
            Point onPage = new Point(p.X, p.Y + scroll);
            // The last one laid down is on top where two overlap: a link sits
            // on its title.
            for (int i = places.Count - 1; i >= 0; i--)
                if ((places[i].Item != null || places[i].IsLink) && places[i].Bounds.Contains(onPage)) return places[i];
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (draggingBar)
            {
                Rectangle thumb = BarThumb();
                int room = Height - thumb.Height;
                if (room > 0) ScrollTo((int)((long)(e.Y - dragOffset) * (contentHeight - Height) / room), false);
                return;
            }
            CardLayout.Place now = At(e.Location);
            if (ReferenceEquals(now, hot)) return;
            hot = now;
            Cursor = now == null ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hot == null) return;
            hot = null;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Rectangle thumb = BarThumb();
            if (e.Button == MouseButtons.Left && !thumb.IsEmpty && e.X >= Width - BAR_W)
            {
                draggingBar = true;
                dragOffset = thumb.Contains(e.Location) ? e.Y - thumb.Y : thumb.Height / 2;
                OnMouseMove(e);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (draggingBar) { draggingBar = false; return; }
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            CardLayout.Place p = At(e.Location);
            if (p == null) return;
            if (p.IsLink) { if (LinkClicked != null) LinkClicked(p.Section); }
            else if (ItemClicked != null) ItemClicked(p.Item, PointToScreen(e.Location));
        }

        // For tests: as if this item had been clicked.
        public void ClickItem(CardItem item)
        {
            if (ItemClicked != null) ItemClicked(item, Point.Empty);
        }

        // ---------- drawing ----------

        static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void Arrived()
        {
            // From a worker thread: a picture is here to be drawn.
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke((MethodInvoker)Invalidate); }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            cardPictures.BeginPaint();
            faces.BeginPaint();

            foreach (CardLayout.Place p in places)
            {
                Rectangle r = p.Bounds;
                r.Y -= scroll;
                if (r.Bottom < 0 || r.Top > Height) continue;      // out of view: not drawn, and its picture not asked for

                if (p.IsLink)
                    TextRenderer.DrawText(g, p.Section.Link, lineFont, r, ReferenceEquals(p, hot) ? Theme.AccentHover : Theme.Accent,
                                          TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                else if (p.IsEmpty)
                    TextRenderer.DrawText(g, p.Section.Empty ?? "Nothing here yet.", lineFont, r, Theme.Muted,
                                          TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
                else if (p.Item == null) DrawTitle(g, p.Section, r);
                else if (p.Section.Rows) DrawRow(g, p, r);
                else DrawCard(g, p, r);
            }
            cardPictures.Trim();
            faces.Trim();

            Rectangle thumb = BarThumb();
            if (!thumb.IsEmpty)
                using (SolidBrush b = new SolidBrush(draggingBar ? Theme.ButtonHover : Theme.Button))
                using (GraphicsPath path = Round(thumb, 3))
                    g.FillPath(b, path);
        }

        void DrawTitle(Graphics g, CardSection s, Rectangle r)
        {
            Size size = TextRenderer.MeasureText(g, s.Title ?? "", titleFont, r.Size, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, s.Title ?? "", titleFont, r, Theme.Text,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (string.IsNullOrEmpty(s.Note)) return;
            Rectangle note = new Rectangle(r.X + size.Width + 10, r.Y + 2, r.Width - size.Width - 140, r.Height);
            TextRenderer.DrawText(g, s.Note, lineFont, note, Theme.Muted,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        void DrawCard(Graphics g, CardLayout.Place p, Rectangle r)
        {
            bool lit = ReferenceEquals(p, hot);
            using (GraphicsPath card = Round(r, 10))
            {
                using (SolidBrush back = new SolidBrush(lit ? Theme.Button : Theme.Card)) g.FillPath(back, card);

                Rectangle picture = new Rectangle(r.X, r.Y, r.Width, CardLayout.PICTURE);
                // Asked for on every paint, kept or not: this is what starts a
                // picture being fetched the first time it comes into view.
                Bitmap image = p.Item.Image ?? Thumbs.Get(p.Item.ImageUrl, Arrived);
                if (image != null) Blit(g, cardPictures.Get(image, picture.Size), picture.Location);
                else
                {
                    Region was = g.Clip;
                    g.SetClip(card, CombineMode.Intersect);
                    using (SolidBrush none = new SolidBrush(Theme.Inset)) g.FillRectangle(none, picture);
                    g.Clip = was;
                }

                if (!string.IsNullOrEmpty(p.Item.Badge)) DrawBadge(g, p.Item, picture);
                if (lit) using (Pen edge = new Pen(Theme.Accent, 1.6f)) g.DrawPath(edge, card);
            }

            Rectangle name = new Rectangle(r.X + 8, r.Y + CardLayout.PICTURE + 6, r.Width - 16, 18);
            TextRenderer.DrawText(g, p.Item.Title ?? "", nameFont, name, Theme.Text,
                                  TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            Rectangle line = new Rectangle(r.X + 8, r.Y + CardLayout.PICTURE + 25, r.Width - 16, 16);
            TextRenderer.DrawText(g, p.Item.Line ?? "", lineFont, line, Theme.Muted,
                                  TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        void DrawBadge(Graphics g, CardItem item, Rectangle picture)
        {
            Size size = TextRenderer.MeasureText(g, item.Badge, badgeFont, Size.Empty, TextFormatFlags.NoPadding);
            Rectangle pill = new Rectangle(picture.X + 8, picture.Y + 8, size.Width + 14, 18);
            using (GraphicsPath path = Round(pill, 9))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(215, 17, 17, 27)))
                g.FillPath(b, path);
            TextRenderer.DrawText(g, item.Badge, badgeFont, pill, item.Good ? Theme.Green : Theme.Text,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        void DrawRow(Graphics g, CardLayout.Place p, Rectangle r)
        {
            bool lit = ReferenceEquals(p, hot);
            using (GraphicsPath row = Round(r, 8))
            using (SolidBrush back = new SolidBrush(lit ? Theme.Button : Theme.Card))
                g.FillPath(back, row);

            // A row with a face - a friend - has one drawn, and a dot saying
            // whether they are on. A row with none is words only.
            bool faced = p.Item.Image != null || !string.IsNullOrEmpty(p.Item.ImageUrl);
            int textX = r.X + 16;
            if (faced)
            {
                Rectangle face = new Rectangle(r.X + 10, r.Y + (r.Height - 36) / 2, 36, 36);
                Bitmap image = p.Item.Image ?? Thumbs.Get(p.Item.ImageUrl, Arrived);
                using (GraphicsPath circle = new GraphicsPath())
                {
                    circle.AddEllipse(face);
                    using (SolidBrush none = new SolidBrush(Theme.Inset)) g.FillPath(none, circle);
                    if (image != null) Blit(g, faces.Get(image, face.Size), face.Location);
                }
                using (SolidBrush dot = new SolidBrush(p.Item.Good ? Theme.Green : Theme.Muted))
                    g.FillEllipse(dot, face.Right - 9, face.Bottom - 9, 10, 10);
                textX = r.X + 56;
            }

            int textW = r.Right - 150 - textX;
            TextRenderer.DrawText(g, p.Item.Title ?? "", nameFont, new Rectangle(textX, r.Y + 8, textW, 18), Theme.Text,
                                  TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, p.Item.Line ?? "", lineFont, new Rectangle(textX, r.Y + 27, textW, 16), Theme.Muted,
                                  TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            if (!string.IsNullOrEmpty(p.Item.Badge))
                TextRenderer.DrawText(g, p.Item.Badge, nameFont, new Rectangle(r.Right - 140, r.Y, 126, r.Height),
                                      lit ? Theme.AccentHover : Theme.Accent, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        // A kept picture copied as it is, pixel for pixel. Said in pixels and
        // with no filtering on purpose: a new Bitmap is 96 dpi, and on a
        // display scaled to 125% GDI+ would otherwise stretch it every frame -
        // the very work being avoided.
        static void Blit(Graphics g, Bitmap picture, Point at)
        {
            InterpolationMode mode = g.InterpolationMode;
            PixelOffsetMode offset = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(picture, new Rectangle(at.X, at.Y, picture.Width, picture.Height),
                        0, 0, picture.Width, picture.Height, GraphicsUnit.Pixel);
            g.InterpolationMode = mode;
            g.PixelOffsetMode = offset;
        }

        // A card's picture as it is drawn: cropped to fill, top corners
        // rounded to the card's. Nothing behind it is baked in - the card
        // lights up under the pointer, and the picture is the same either way.
        // The corners are cut with the same hard-edged clip the card's picture
        // always had, so they come out the same.
        static Bitmap MakeCardPicture(Bitmap source, Size size)
        {
            Bitmap made = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(made))
            using (GraphicsPath card = Round(new Rectangle(0, 0, size.Width, CardLayout.CARD_H), 10))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.SetClip(card, CombineMode.Intersect);
                g.DrawImage(source, Cover(source.Size, new Rectangle(0, 0, size.Width, size.Height)));
            }
            return made;
        }

        // A friend's face, cut round.
        static Bitmap MakeFace(Bitmap source, Size size)
        {
            Bitmap made = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(made))
            using (GraphicsPath circle = new GraphicsPath())
            {
                circle.AddEllipse(new Rectangle(0, 0, size.Width, size.Height));
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.SetClip(circle, CombineMode.Intersect);
                g.DrawImage(source, new Rectangle(0, 0, size.Width, size.Height));
            }
            return made;
        }

        // The picture filling the box, cropped rather than squashed.
        public static Rectangle Cover(Size image, Rectangle box)
        {
            if (image.Width <= 0 || image.Height <= 0) return box;
            double scale = Math.Max(box.Width / (double)image.Width, box.Height / (double)image.Height);
            int w = (int)Math.Ceiling(image.Width * scale), h = (int)Math.Ceiling(image.Height * scale);
            return new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
        }
    }
}
