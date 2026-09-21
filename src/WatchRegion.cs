using System;
using System.Drawing;

namespace RobloxKeeper
{
    // Which edge or corner of the window a region is pinned to.
    enum RegionAnchor
    {
        TopLeft, TopCentre, TopRight,
        MiddleLeft, Centre, MiddleRight,
        BottomLeft, BottomCentre, BottomRight
    }

    // How a region changes when the window does.
    enum RegionScaling
    {
        // Each axis scales on its own. Right for text: a wider box simply
        // contains more words.
        Stretch,

        // Both axes scale by the same factor, so the box keeps its shape.
        // Right for images: a squashed template will not match.
        Uniform
    }

    // A rectangle on a Roblox window, stored so it survives the window being
    // resized.
    //
    // Storing pixels would be useless the moment the window changed. Storing
    // plain fractions is not enough either, because Roblox mixes UI that
    // scales with the window and UI pinned at a fixed pixel size, and no
    // single formula is right for both. So a region records the edge it was
    // pinned to, its position and size as fractions, and the window it was
    // drawn on - and the caller widens the result afterwards to absorb what
    // arithmetic cannot predict.
    class WatchRegion
    {
        public string Name;
        public string PlaceId;
        public RegionAnchor Anchor;

        // Offset from the anchor point and size, both as fractions of the
        // window the box was drawn on.
        public double OffsetX, OffsetY, SizeW, SizeH;

        public int DrawnWidth, DrawnHeight;
        public RegionScaling Scaling;

        // Where an anchor sits in a window, as a fraction of it.
        public static PointF AnchorPoint(RegionAnchor a)
        {
            float x, y;
            switch (a)
            {
                case RegionAnchor.TopLeft: x = 0f; y = 0f; break;
                case RegionAnchor.TopCentre: x = 0.5f; y = 0f; break;
                case RegionAnchor.TopRight: x = 1f; y = 0f; break;
                case RegionAnchor.MiddleLeft: x = 0f; y = 0.5f; break;
                case RegionAnchor.Centre: x = 0.5f; y = 0.5f; break;
                case RegionAnchor.MiddleRight: x = 1f; y = 0.5f; break;
                case RegionAnchor.BottomLeft: x = 0f; y = 1f; break;
                case RegionAnchor.BottomCentre: x = 0.5f; y = 1f; break;
                default: x = 1f; y = 1f; break;   // BottomRight
            }
            return new PointF(x, y);
        }

        public static WatchRegion FromBox(Rectangle box, int winW, int winH,
                                          RegionAnchor anchor, RegionScaling scaling)
        {
            if (winW <= 0 || winH <= 0) throw new ArgumentException("window has no size");

            PointF a = AnchorPoint(anchor);
            WatchRegion r = new WatchRegion();
            r.Anchor = anchor;
            r.Scaling = scaling;
            r.DrawnWidth = winW;
            r.DrawnHeight = winH;
            r.OffsetX = (box.X - a.X * winW) / (double)winW;
            r.OffsetY = (box.Y - a.Y * winH) / (double)winH;
            r.SizeW = box.Width / (double)winW;
            r.SizeH = box.Height / (double)winH;
            return r;
        }

        public Rectangle Project(int winW, int winH)
        {
            if (winW <= 0 || winH <= 0) return Rectangle.Empty;

            PointF a = AnchorPoint(Anchor);
            double x, y, w, h;

            if (Scaling == RegionScaling.Stretch)
            {
                x = a.X * winW + OffsetX * winW;
                y = a.Y * winH + OffsetY * winH;
                w = SizeW * winW;
                h = SizeH * winH;
            }
            else
            {
                // One factor for both axes, taken from whichever dimension
                // shrank most - the same rule Roblox's own UI follows when it
                // has to fit a layout into a different shape.
                double scale = Math.Min(winW / (double)DrawnWidth, winH / (double)DrawnHeight);
                x = a.X * winW + OffsetX * DrawnWidth * scale;
                y = a.Y * winH + OffsetY * DrawnHeight * scale;
                w = SizeW * DrawnWidth * scale;
                h = SizeH * DrawnHeight * scale;
            }

            return Clamp(Round(x), Round(y), Round(w), Round(h), winW, winH);
        }

        // Grow a box around its centre, then keep it inside the window.
        //
        // A box slightly too big costs about a millisecond; a box slightly too
        // small misses the thing it was drawn for.
        public static Rectangle Widen(Rectangle r, double factor, int winW, int winH)
        {
            int gw = (int)Math.Round(r.Width * factor);
            int gh = (int)Math.Round(r.Height * factor);
            return Clamp(r.X - gw / 2, r.Y - gh / 2, r.Width + gw, r.Height + gh, winW, winH);
        }

        // Floor-plus-half rather than a cast, because C# truncates toward zero
        // and that rounds the wrong way for anything negative - which an
        // offset from a right or bottom anchor always is.
        static int Round(double v) { return (int)Math.Floor(v + 0.5); }

        static Rectangle Clamp(int x, int y, int w, int h, int winW, int winH)
        {
            // A zero-width crop throws rather than returning nothing, so a
            // region is never allowed to collapse.
            if (w < 1) w = 1;
            if (h < 1) h = 1;
            if (w > winW) w = winW;
            if (h > winH) h = winH;
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x + w > winW) x = winW - w;
            if (y + h > winH) y = winH - h;
            return new Rectangle(x, y, w, h);
        }
    }
}
