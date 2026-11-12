using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace RobloxKeeper
{
    // A mod pack that comes with the app.
    class PremadePack
    {
        public string Name, Says;
        public Action<Graphics, int> Draw;      // the pointer, on a square this many pixels across, its middle the hot spot
        public Action<Graphics, int> DrawLock;  // the one shown while the camera is locked to the mouse; null to leave Roblox's

        public PremadePack(string name, string says, Action<Graphics, int> draw, Action<Graphics, int> drawLock)
        {
            Name = name; Says = says; Draw = draw; DrawLock = drawLock;
        }
    }

    // Mod packs made by the app itself: mouse pointers.
    //
    // They are drawn here rather than shipped as files. Nothing of anyone
    // else's is copied - not Roblox's art, not a font, not a sound - and the
    // exe carries no pictures. A pack made here is an ordinary pack: it goes
    // into the library and through the same checks, backup and undo as one
    // somebody downloaded.
    //
    // Roblox draws its pointer from a 64-pixel picture whose middle is the
    // point that clicks, so everything here is drawn about the middle.
    static class PremadeMods
    {
        public const string POINTER = "content\\textures\\Cursors\\KeyboardMouse\\ArrowCursor.png";
        public const string POINTER_FAR = "content\\textures\\Cursors\\KeyboardMouse\\ArrowFarCursor.png";
        public const string POINTER_LOCKED = "content\\textures\\MouseLockedCursor.png";
        public const int POINTER_SIZE = 64, LOCKED_SIZE = 32;

        public static readonly PremadePack[] All =
        {
            new PremadePack("Crosshair pointer", "A thin white cross with a gap in the middle - for aiming.",
                delegate(Graphics g, int s) { Cross(g, s, Color.White, s * 0.30f, s * 0.07f, 2.2f); },
                delegate(Graphics g, int s) { Cross(g, s, Color.White, s * 0.34f, s * 0.09f, 1.8f); }),
            new PremadePack("Green crosshair pointer", "The same cross in bright green. Easier to see on pale maps.",
                delegate(Graphics g, int s) { Cross(g, s, Color.FromArgb(70, 255, 90), s * 0.30f, s * 0.07f, 2.2f); },
                delegate(Graphics g, int s) { Cross(g, s, Color.FromArgb(70, 255, 90), s * 0.34f, s * 0.09f, 1.8f); }),
            new PremadePack("Dot pointer", "One small bright dot that stays out of the way.",
                delegate(Graphics g, int s) { Dot(g, s, Color.White, s * 0.07f); },
                delegate(Graphics g, int s) { Dot(g, s, Color.White, s * 0.10f); }),
            new PremadePack("Red dot pointer", "The same dot, in red.",
                delegate(Graphics g, int s) { Dot(g, s, Color.FromArgb(255, 60, 50), s * 0.07f); },
                delegate(Graphics g, int s) { Dot(g, s, Color.FromArgb(255, 60, 50), s * 0.10f); }),
            new PremadePack("Ring pointer", "A ring with a dot in the middle. Easy to spot.",
                delegate(Graphics g, int s) { Ring(g, s, Color.White, s * 0.20f); Dot(g, s, Color.White, s * 0.04f); },
                delegate(Graphics g, int s) { Ring(g, s, Color.White, s * 0.26f); Dot(g, s, Color.White, s * 0.06f); }),
            new PremadePack("Scope pointer", "A ring with a cross and a red dot, like a scope.",
                delegate(Graphics g, int s) { Ring(g, s, Color.White, s * 0.22f); Cross(g, s, Color.White, s * 0.36f, s * 0.12f, 1.8f); Dot(g, s, Color.FromArgb(255, 60, 50), s * 0.035f); },
                delegate(Graphics g, int s) { Ring(g, s, Color.White, s * 0.26f); Cross(g, s, Color.White, s * 0.42f, s * 0.14f, 1.4f); Dot(g, s, Color.FromArgb(255, 60, 50), s * 0.05f); }),
            new PremadePack("Big bright pointer", "A big yellow arrow, for when you keep losing your pointer.",
                delegate(Graphics g, int s) { Arrow(g, s, Color.FromArgb(255, 221, 51)); }, null)
        };

        public static PremadePack Find(string name)
        {
            foreach (PremadePack p in All)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        // ---------- drawing ----------

        static Graphics Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            return g;
        }

        // Every shape is drawn twice: dark and a little wider first, so it
        // shows on snow as well as it does on night.
        static readonly Color Edge = Color.FromArgb(220, 0, 0, 0);

        static void Cross(Graphics g, int s, Color colour, float arm, float gap, float thick)
        {
            float c = s / 2f;
            float[][] lines =
            {
                new float[] { c - arm, c, c - gap, c }, new float[] { c + gap, c, c + arm, c },
                new float[] { c, c - arm, c, c - gap }, new float[] { c, c + gap, c, c + arm }
            };
            using (Pen under = new Pen(Edge, thick + 2.4f))
            using (Pen over = new Pen(colour, thick))
            {
                under.StartCap = under.EndCap = LineCap.Round;
                foreach (float[] l in lines) g.DrawLine(under, l[0], l[1], l[2], l[3]);
                foreach (float[] l in lines) g.DrawLine(over, l[0], l[1], l[2], l[3]);
            }
        }

        static void Dot(Graphics g, int s, Color colour, float radius)
        {
            float c = s / 2f;
            using (SolidBrush under = new SolidBrush(Edge)) g.FillEllipse(under, c - radius - 1.5f, c - radius - 1.5f, radius * 2 + 3, radius * 2 + 3);
            using (SolidBrush over = new SolidBrush(colour)) g.FillEllipse(over, c - radius, c - radius, radius * 2, radius * 2);
        }

        static void Ring(Graphics g, int s, Color colour, float radius)
        {
            float c = s / 2f;
            using (Pen under = new Pen(Edge, 4.6f)) g.DrawEllipse(under, c - radius, c - radius, radius * 2, radius * 2);
            using (Pen over = new Pen(colour, 2.2f)) g.DrawEllipse(over, c - radius, c - radius, radius * 2, radius * 2);
        }

        // An arrow whose tip is the middle of the picture - the point that
        // clicks - pointing up and to the left as a pointer does.
        static void Arrow(Graphics g, int s, Color colour)
        {
            float c = s / 2f, k = s / 64f;
            PointF[] shape =
            {
                new PointF(c, c), new PointF(c, c + 26 * k), new PointF(c + 7 * k, c + 20 * k),
                new PointF(c + 12 * k, c + 30 * k), new PointF(c + 17 * k, c + 28 * k),
                new PointF(c + 12 * k, c + 18 * k), new PointF(c + 20 * k, c + 18 * k)
            };
            using (SolidBrush fill = new SolidBrush(colour)) g.FillPolygon(fill, shape);
            using (Pen edge = new Pen(Color.FromArgb(240, 20, 20, 20), 2.6f * k))
            {
                edge.LineJoin = LineJoin.Round;
                g.DrawPolygon(edge, shape);
            }
        }

        public static Bitmap Picture(Action<Graphics, int> draw, int size)
        {
            Bitmap b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Smooth(Graphics.FromImage(b)))
            {
                g.Clear(Color.Transparent);
                draw(g, size);
            }
            return b;
        }

        // ---------- as a pack ----------

        // The pack's files, written under a folder laid out as Roblox's is.
        public static void Build(PremadePack p, string folder)
        {
            Save(p.Draw, POINTER_SIZE, Path.Combine(folder, POINTER));
            Save(p.Draw, POINTER_SIZE, Path.Combine(folder, POINTER_FAR));
            if (p.DrawLock != null) Save(p.DrawLock, LOCKED_SIZE, Path.Combine(folder, POINTER_LOCKED));
        }

        static void Save(Action<Graphics, int> draw, int size, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (Bitmap b = Picture(draw, size)) b.Save(path, ImageFormat.Png);
        }

        // Into the library, as any pack is added, and ticked. Null when it
        // is; otherwise why not. Nothing in Roblox changes until packs are
        // applied.
        //
        // Every pack here replaces the same pictures, so only one can show.
        // The others that are ticked are unticked, and named in switchedOff,
        // rather than left ticked and losing to whichever is lower in the list.
        public static string Use(LooksMods mods, PremadePack p, List<string> switchedOff)
        {
            if (mods.Find(p.Name) != null)
            {
                mods.SetEnabled(p.Name, true);
                // Last in the list is the one that shows, where another pack
                // - one of these or somebody's own - replaces the pointer too.
                mods.Move(p.Name, mods.Packs.Count);
            }
            else
            {
                string temp = Path.Combine(Path.GetTempPath(), "rk-premade-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Build(p, temp);
                    string why = mods.Import(temp, p.Name);
                    if (why != null) return why;
                }
                catch (Exception ex) { return ex.Message; }
                finally { try { Directory.Delete(temp, true); } catch { } }
            }
            OnlyThis(mods, p.Name, switchedOff);
            mods.Save();
            return null;
        }

        // A ready-made pointer has just been ticked - from the menu or in
        // the list: the other ready-made ones that are ticked are unticked,
        // and named. Nothing happens for a pack that is not one of these.
        public static void OnlyThis(LooksMods mods, string name, List<string> switchedOff)
        {
            if (Find(name) == null) return;
            foreach (PremadePack other in All)
            {
                if (string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                LooksModPack there = mods.Find(other.Name);
                if (there == null || !there.Enabled) continue;
                mods.SetEnabled(other.Name, false);
                if (switchedOff != null) switchedOff.Add(other.Name);
            }
        }
    }
}
