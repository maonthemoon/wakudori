using System;
using System.Drawing;

namespace Wakudori
{
    // 枠のハンドル(四隅と各辺の中央)
    internal enum FrameHandle { None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left }

    // 撮影範囲の枠の幾何モデル. 座標はすべて仮想スクリーン上の物理ピクセル(負もありうる).
    internal sealed class Selection
    {
        public const int MinSize = 16;

        private readonly Rectangle[] monitors;
        private Rectangle frame;
        private FrameHandle selectedHandle;

        public event EventHandler Changed;

        public Selection(Rectangle[] monitors, Rectangle initialFrame)
        {
            this.monitors = monitors;
            this.frame = Fit(initialFrame, NearestMonitor(monitors, Center(initialFrame)));
        }

        public Rectangle Frame { get { return frame; } }

        public FrameHandle SelectedHandle
        {
            get { return selectedHandle; }
            set
            {
                if (selectedHandle == value) return;
                selectedHandle = value;
                RaiseChanged();
            }
        }

        // 枠の中心があるモニター(なければ最も近いモニター)
        public Rectangle CurrentMonitor
        {
            get { return NearestMonitor(monitors, Center(frame)); }
        }

        // キーボード用: 枠全体を動かす. 今のモニターの外には出ない
        public void MoveBy(int dx, int dy)
        {
            Rectangle m = CurrentMonitor;
            Rectangle r = frame;
            r.X = Clamp(r.X + dx, m.Left, m.Right - r.Width);
            r.Y = Clamp(r.Y + dy, m.Top, m.Bottom - r.Height);
            SetFrameInternal(r);
        }

        // マウス用: pointerがあるモニターへ枠全体を移し, 収まるよう合わせる(C6)
        public void MoveTo(Point topLeft, Point pointer)
        {
            Rectangle m = NearestMonitor(monitors, pointer);
            Rectangle r = new Rectangle(topLeft, frame.Size);
            SetFrameInternal(Fit(r, m));
        }

        // ハンドルの辺を動かす. 動かす辺は今のモニター内かつMinSize以上に留め, 反対の辺は動かさない
        public void MoveEdges(FrameHandle h, int dx, int dy)
        {
            Rectangle m = CurrentMonitor;
            int l = frame.Left, t = frame.Top, r = frame.Right, b = frame.Bottom;

            bool moveL = h == FrameHandle.TopLeft || h == FrameHandle.Left || h == FrameHandle.BottomLeft;
            bool moveR = h == FrameHandle.TopRight || h == FrameHandle.Right || h == FrameHandle.BottomRight;
            bool moveT = h == FrameHandle.TopLeft || h == FrameHandle.Top || h == FrameHandle.TopRight;
            bool moveB = h == FrameHandle.BottomLeft || h == FrameHandle.Bottom || h == FrameHandle.BottomRight;

            if (moveL) l = Clamp(l + dx, m.Left, r - MinSize);
            if (moveR) r = Clamp(r + dx, l + MinSize, m.Right);
            if (moveT) t = Clamp(t + dy, m.Top, b - MinSize);
            if (moveB) b = Clamp(b + dy, t + MinSize, m.Bottom);

            SetFrameInternal(Rectangle.FromLTRB(l, t, r, b));
        }

        // 枠を置き換える. 中心に最も近いモニターに収める
        public void SetFrame(Rectangle r)
        {
            SetFrameInternal(Fit(r, NearestMonitor(monitors, Center(r))));
        }

        // 最初に出す枠(A2, C5, C7)
        public static Rectangle InitialFrame(Rectangle[] monitors, Rectangle? last, Point cursor, Size defaultSize)
        {
            if (last.HasValue)
            {
                for (int i = 0; i < monitors.Length; i++)
                {
                    if (monitors[i].Contains(last.Value)) return last.Value;
                }
            }

            Rectangle m = NearestMonitor(monitors, cursor);
            Size size = last.HasValue ? last.Value.Size : defaultSize;
            int w = Math.Max(Math.Min(size.Width, m.Width), Math.Min(MinSize, m.Width));
            int h = Math.Max(Math.Min(size.Height, m.Height), Math.Min(MinSize, m.Height));
            int x = m.Left + (m.Width - w) / 2;
            int y = m.Top + (m.Height - h) / 2;
            return new Rectangle(x, y, w, h);
        }

        // 8個のハンドルの四角(FrameHandle順 TopLeft..Left). 枠の各辺の線を中心にする
        public static Rectangle[] HandleRects(Rectangle frame, int handleSize)
        {
            int l = frame.Left, r = frame.Right, t = frame.Top, b = frame.Bottom;
            int cx = l + (r - l) / 2, cy = t + (b - t) / 2;
            return new Rectangle[]
            {
                Square(l, t, handleSize),
                Square(cx, t, handleSize),
                Square(r, t, handleSize),
                Square(r, cy, handleSize),
                Square(r, b, handleSize),
                Square(cx, b, handleSize),
                Square(l, b, handleSize),
                Square(l, cy, handleSize)
            };
        }

        // pが枠のどの角・辺の上か(Macのように, 8点だけでなく辺のどこでもつかめる).
        // grip: 辺の線からの当たり幅. 角は辺より広く(grip*2)取り, 角を優先する.
        // 枠が細くて両側の辺に当たるときは, 近いほうを選ぶ.
        public static FrameHandle HitTest(Rectangle frame, Point p, int grip)
        {
            int l = frame.Left, r = frame.Right, t = frame.Top, b = frame.Bottom;
            if (p.X < l - grip || p.X > r + grip || p.Y < t - grip || p.Y > b + grip) return FrameHandle.None;

            int dl = Math.Abs(p.X - l), dr = Math.Abs(p.X - r);
            int dt = Math.Abs(p.Y - t), db = Math.Abs(p.Y - b);
            bool leftSide = dl <= dr;
            bool topSide = dt <= db;
            int dx = leftSide ? dl : dr;
            int dy = topSide ? dt : db;

            int corner = grip * 2;
            if (dx <= corner && dy <= corner)
            {
                if (topSide) return leftSide ? FrameHandle.TopLeft : FrameHandle.TopRight;
                return leftSide ? FrameHandle.BottomLeft : FrameHandle.BottomRight;
            }
            if (dx <= grip && (dy > grip || dx <= dy)) return leftSide ? FrameHandle.Left : FrameHandle.Right;
            if (dy <= grip) return topSide ? FrameHandle.Top : FrameHandle.Bottom;
            return FrameHandle.None;
        }

        // ---- 内部 ----

        private static Rectangle Square(int cx, int cy, int size)
        {
            return new Rectangle(cx - size / 2, cy - size / 2, size, size);
        }

        private static Point Center(Rectangle r)
        {
            return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
        }

        // hi < lo のときはloを返す
        private static int Clamp(int v, int lo, int hi)
        {
            if (hi < lo) return lo;
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        // モニターより大きければ縮め, 全体が入るようずらす
        private static Rectangle Fit(Rectangle r, Rectangle m)
        {
            int w = Math.Min(r.Width, m.Width);
            int h = Math.Min(r.Height, m.Height);
            int x = Clamp(r.X, m.Left, m.Right - w);
            int y = Clamp(r.Y, m.Top, m.Bottom - h);
            return new Rectangle(x, y, w, h);
        }

        // 点を含むモニター, なければ距離が最小のモニター
        private static Rectangle NearestMonitor(Rectangle[] monitors, Point p)
        {
            Rectangle best = monitors[0];
            long bestDist = long.MaxValue;
            for (int i = 0; i < monitors.Length; i++)
            {
                Rectangle m = monitors[i];
                if (m.Contains(p)) return m;
                long dx = Math.Max(Math.Max(m.Left - p.X, 0), p.X - (m.Right - 1));
                long dy = Math.Max(Math.Max(m.Top - p.Y, 0), p.Y - (m.Bottom - 1));
                long d = dx * dx + dy * dy;
                if (d < bestDist) { bestDist = d; best = m; }
            }
            return best;
        }

        private void SetFrameInternal(Rectangle r)
        {
            if (frame == r) return;
            frame = r;
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            EventHandler h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
