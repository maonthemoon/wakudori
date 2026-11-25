using System;
using System.Collections.Generic;
using System.Drawing;

namespace Wakudori
{
    // 書き込み1つ. ペンの線か, 四角の塗りつぶし(隠す用). 座標は画像の左上からのピクセル
    internal sealed class MarkItem
    {
        public bool IsFill;
        public Color Color;
        public int Width;                                    // ペンの太さ
        public readonly List<Point> Points = new List<Point>(); // ペンの通った点
        public Rectangle Rect;                               // 塗りつぶす四角

        public static MarkItem Pen(Color color, int width, Point start)
        {
            MarkItem m = new MarkItem();
            m.Color = color;
            m.Width = width;
            m.Points.Add(start);
            return m;
        }

        public static MarkItem Fill()
        {
            MarkItem m = new MarkItem();
            m.IsFill = true;
            m.Color = Color.Black;
            return m;
        }

        // 同じ点が続くときは足さない. 足したらtrue
        public bool AddPoint(Point p)
        {
            if (Points.Count > 0 && Points[Points.Count - 1] == p) return false;
            Points.Add(p);
            return true;
        }

        // 2点を対角とする四角. 両端の画素を含み, 画像の外にははみ出さない
        public static Rectangle RectBetween(Point a, Point b, Size image)
        {
            Rectangle r = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
            return Rectangle.Intersect(r, new Rectangle(Point.Empty, image));
        }
    }

    // 書き込みの並びと, 元に戻す・やり直す
    internal sealed class MarkList
    {
        private readonly List<MarkItem> items = new List<MarkItem>();
        private readonly List<MarkItem> undone = new List<MarkItem>();

        public int Count { get { return items.Count; } }
        public IList<MarkItem> Items { get { return items.AsReadOnly(); } }

        // 新しく書いたら, やり直しの分は捨てる
        public void Add(MarkItem m)
        {
            items.Add(m);
            undone.Clear();
        }

        public bool Undo()
        {
            return MoveLast(items, undone);
        }

        public bool Redo()
        {
            return MoveLast(undone, items);
        }

        private static bool MoveLast(List<MarkItem> from, List<MarkItem> to)
        {
            if (from.Count == 0) return false;
            MarkItem m = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            to.Add(m);
            return true;
        }
    }

    // カラーパレットを置く場所. 画像の下, 上, 右, 左の順に余白を探し, なければ画面の下の端に画像に重ねて置く
    internal static class MarkLayout
    {
        public static Rectangle PlacePalette(Size screen, Rectangle image, Size bar, int gap)
        {
            int cx = Clamp(image.X + (image.Width - bar.Width) / 2, gap, screen.Width - bar.Width - gap);
            int cy = Clamp(image.Y + (image.Height - bar.Height) / 2, gap, screen.Height - bar.Height - gap);
            if (image.Bottom + gap + bar.Height + gap <= screen.Height) return new Rectangle(new Point(cx, image.Bottom + gap), bar);
            if (image.Top - gap - bar.Height - gap >= 0) return new Rectangle(new Point(cx, image.Top - gap - bar.Height), bar);
            if (image.Right + gap + bar.Width + gap <= screen.Width) return new Rectangle(new Point(image.Right + gap, cy), bar);
            if (image.Left - gap - bar.Width - gap >= 0) return new Rectangle(new Point(image.Left - gap - bar.Width, cy), bar);
            return new Rectangle(new Point(cx, screen.Height - gap - bar.Height), bar);
        }

        private static int Clamp(int v, int min, int max)
        {
            return Math.Max(min, Math.Min(v, max));
        }
    }
}
