using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RobloxKeeper
{
    // An image as a plain array of colours.
    //
    // Bitmap.GetPixel takes a lock on every call, which makes searching a
    // region hundreds of times slower than it needs to be. It also drags GDI
    // into anything that wants to compare two images, which means the
    // comparison cannot be tested without building real bitmaps - so it does
    // not get tested.
    //
    // FromBitmap is the only place in the image code that touches GDI at all.
    class Pixels
    {
        public readonly int[] Argb;
        public readonly int Width;
        public readonly int Height;

        public Pixels(int width, int height)
        {
            Width = width < 0 ? 0 : width;
            Height = height < 0 ? 0 : height;
            Argb = new int[Width * Height];
        }

        public bool IsEmpty { get { return Width <= 0 || Height <= 0; } }

        public int At(int x, int y) { return Argb[y * Width + x]; }

        public void Set(int x, int y, int argb) { Argb[y * Width + x] = argb; }

        public void Fill(int argb)
        {
            for (int i = 0; i < Argb.Length; i++) Argb[i] = argb;
        }

        // Clamped rather than thrown: a projected region can land partly off a
        // window that shrank between the projection and the crop, and that is
        // a normal thing to happen rather than a fault.
        public Pixels Crop(Rectangle r)
        {
            int x0 = Math.Max(0, r.X), y0 = Math.Max(0, r.Y);
            int x1 = Math.Min(Width, r.Right), y1 = Math.Min(Height, r.Bottom);
            int w = Math.Max(0, x1 - x0), h = Math.Max(0, y1 - y0);

            Pixels p = new Pixels(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    p.Set(x, y, At(x0 + x, y0 + y));
            return p;
        }

        public static Pixels FromBitmap(Bitmap bmp)
        {
            if (bmp == null) return new Pixels(0, 0);

            Pixels p = new Pixels(bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                           ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                // Row by row, because Stride can be wider than the image when
                // GDI pads rows out to a four-byte boundary. Copying the whole
                // buffer in one go silently skews the picture when it does.
                for (int y = 0; y < bmp.Height; y++)
                    Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride),
                                 p.Argb, y * bmp.Width, bmp.Width);
            }
            finally { bmp.UnlockBits(data); }
            return p;
        }

        public Bitmap ToBitmap()
        {
            Bitmap bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, Width, Height),
                                           ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < Height; y++)
                    Marshal.Copy(Argb, y * Width,
                                 new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), Width);
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }
    }
}
