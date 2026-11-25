using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Wakudori
{
    // 1つのモニターの, 止めた絵. 撮ったままの絵と, 範囲の外用に暗くした絵を持つ.
    internal sealed class FrozenScreen : IDisposable
    {
        public readonly Rectangle Bounds; // 仮想スクリーン座標, 物理ピクセル
        private Bitmap image;             // 撮ったまま(不透明)
        private Bitmap dimmed;            // 範囲の外用に暗くしたもの(不透明)

        public FrozenScreen(Rectangle bounds)
        {
            Bounds = bounds;
            image = CaptureOutput.CaptureScreen(bounds);
        }

        // 暗くした絵を作り, 撮った絵のアルファを255にそろえる. 画面を撮る時点では時間をかけず, 全モニターを撮ったあとで呼ぶ.
        public void Prepare(Color dim)
        {
            byte[] r = DimTable(dim.R, dim.A);
            byte[] g = DimTable(dim.G, dim.A);
            byte[] b = DimTable(dim.B, dim.A);
            int w = Bounds.Width, h = Bounds.Height;
            dimmed = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            Rectangle all = new Rectangle(0, 0, w, h);
            BitmapData src = image.LockBits(all, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                BitmapData dst = dimmed.LockBits(all, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int[] row = new int[w];
                    for (int y = 0; y < h; y++)
                    {
                        IntPtr srcRow = IntPtr.Add(src.Scan0, y * src.Stride);
                        Marshal.Copy(srcRow, row, 0, w);
                        for (int x = 0; x < w; x++) row[x] = unchecked(row[x] | (int)0xFF000000);
                        Marshal.Copy(row, 0, srcRow, w);
                        for (int x = 0; x < w; x++)
                        {
                            int p = row[x];
                            row[x] = unchecked((int)0xFF000000) | (r[(p >> 16) & 255] << 16) | (g[(p >> 8) & 255] << 8) | b[p & 255];
                        }
                        Marshal.Copy(row, 0, IntPtr.Add(dst.Scan0, y * dst.Stride), w);
                    }
                }
                finally
                {
                    dimmed.UnlockBits(dst);
                }
            }
            finally
            {
                image.UnlockBits(src);
            }
        }

        // 1色(dim)を濃さalphaで重ねたときの, 元の値ごとの結果
        private static byte[] DimTable(int dim, int alpha)
        {
            byte[] t = new byte[256];
            for (int i = 0; i < 256; i++) t[i] = (byte)((dim * alpha + i * (255 - alpha) + 127) / 255);
            return t;
        }

        // 範囲の外は暗い絵, 範囲(local. モニター左上からの位置)の中は撮ったままの絵を, 描画面(32ビット, 上から下)に写す.
        public void CopyTo(IntPtr bits, int stride, Rectangle local)
        {
            CopyRect(dimmed, bits, stride, new Rectangle(0, 0, Bounds.Width, Bounds.Height));
            Rectangle inside = Rectangle.Intersect(local, new Rectangle(0, 0, Bounds.Width, Bounds.Height));
            if (inside.Width > 0 && inside.Height > 0) CopyRect(image, bits, stride, inside);
        }

        private static void CopyRect(Bitmap bmp, IntPtr bits, int stride, Rectangle rc)
        {
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                UIntPtr bytes = new UIntPtr((uint)(rc.Width * 4));
                for (int y = rc.Top; y < rc.Bottom; y++)
                {
                    NativeMethods.CopyMemory(IntPtr.Add(bits, y * stride + rc.X * 4), IntPtr.Add(d.Scan0, y * d.Stride + rc.X * 4), bytes);
                }
            }
            finally
            {
                bmp.UnlockBits(d);
            }
        }

        // 仮想スクリーン座標rの部分を切り出す
        public Bitmap Crop(Rectangle r)
        {
            Rectangle local = new Rectangle(r.X - Bounds.X, r.Y - Bounds.Y, r.Width, r.Height);
            return image.Clone(local, PixelFormat.Format32bppArgb);
        }

        public void Dispose()
        {
            if (image != null) { image.Dispose(); image = null; }
            if (dimmed != null) { dimmed.Dispose(); dimmed = null; }
        }
    }

    // 選択画面を開くときに撮った, 全モニターの止めた絵.
    // 選択画面を出す(前面が変わる)と, 開いていたメニューなどは閉じるので, その前に撮る.
    internal sealed class FrozenScreens : IDisposable
    {
        private readonly List<FrozenScreen> screens = new List<FrozenScreen>();
        public readonly DateTime Time = DateTime.Now;

        public static FrozenScreens Capture(Rectangle[] monitors)
        {
            FrozenScreens s = new FrozenScreens();
            try
            {
                foreach (Rectangle m in monitors) s.screens.Add(new FrozenScreen(m));
            }
            catch
            {
                s.Dispose();
                throw;
            }
            return s;
        }

        public void Prepare(Color dim)
        {
            foreach (FrozenScreen f in screens) f.Prepare(dim);
        }

        public FrozenScreen Get(int index)
        {
            return screens[index];
        }

        // rを含むモニターの絵から切り出す. 枠は1つのモニターに収まる(C6). どこにも収まらなければnull
        public Bitmap Crop(Rectangle r)
        {
            foreach (FrozenScreen f in screens)
            {
                if (f.Bounds.Contains(r)) return f.Crop(r);
            }
            return null;
        }

        public void Dispose()
        {
            foreach (FrozenScreen f in screens) f.Dispose();
            screens.Clear();
        }
    }
}
