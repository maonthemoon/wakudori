using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Wakudori
{
    internal delegate void RegionChosenHandler(object sender, SavedRegion region);

    // 1つのモニターを覆う選択画面.
    // 異なるDPIのモニターをまたぐ1枚のウィンドウはずれやすいので, モニターごとに1枚ずつ作る.
    // 枠(Selection)は全フォームで共有し, 枠が変わったら関係するフォームだけ描き直す.
    // 描画はUpdateLayeredWindow(ピクセル単位のアルファ)で行い, 下の画面は動いたまま見える.
    internal sealed class OverlayForm : Form
    {
        private const int DimAlpha = 140;   // 範囲の外の暗さ(0〜255)
        private const int InsideAlpha = 1;  // 範囲の内側. 0にするとクリックが下へ抜けるので1にする
        private const int Grip96 = 5;       // 辺をつかめる幅(96 DPIのとき). 角はこの2倍
        // つまみ(選んだ辺・カーソルを乗せた辺の外側に出す太い線. 角はL字). 96 DPIのときの大きさ
        private const int KnobThick96 = 4;  // 太さ
        private const int KnobLength96 = 32; // 辺のつまみの長さ(短い辺では辺の40%まで)
        private const int KnobCorner96 = 18; // 角のL字の1本の長さ
        private const int KnobGap96 = 2;    // 線とつまみのすき間
        private const int KnobHoverAlpha = 115; // カーソルを乗せただけのときのつまみの濃さ(選んだときは255)

        // 色はanthropic-htmlのライトテーマに合わせる(B12). 黒は暖色の黒(#141413), 線はアイボリー(#FAF9F5).
        private static readonly Color DimColor = Color.FromArgb(DimAlpha, 0x14, 0x14, 0x13); // 範囲の外
        // 線は範囲のすぐ外側(必ず暗くした側)に引くので, 明るい色の実線ならどんな画面の上でも見える.
        private static readonly Color LineColor = Color.FromArgb(242, 0xFA, 0xF9, 0xF5);
        private static readonly Color KnobColor = Color.FromArgb(255, 0xFA, 0xF9, 0xF5);

        private enum DragMode { None, Move, FrameHandle }

        private readonly int monitorIndex;
        private readonly Rectangle bounds;  // モニターの範囲(仮想スクリーン座標, 物理ピクセル)
        private readonly uint dpi;
        private readonly float scale;
        private readonly Selection selection;

        private bool frameWasVisible;       // 前回の描画で枠がこのモニターにあったか
        private DragMode dragMode = DragMode.None;
        private FrameHandle dragHandle = FrameHandle.None;
        private Point dragStart;
        private Rectangle frameAtDragStart;

        private IntPtr memDC = IntPtr.Zero;
        private IntPtr hBitmap = IntPtr.Zero;
        private IntPtr oldBitmap = IntPtr.Zero;
        private Bitmap surface;             // hBitmapのピクセルを直接指すBitmap(PArgb)

        public bool AllowClose;             // trueのときだけ閉じられる(Alt+F4で1枚だけ閉じないように)

        public event EventHandler CaptureRequested;
        public event EventHandler CancelRequested;
        public event EventHandler SaveRequested;          // Ctrl+S(A3)
        public event RegionChosenHandler RegionChosen;    // 一覧の名前をクリックした(A4)

        // 保存した範囲の一覧(A4, A7). 一覧を出すモニターのフォームだけが持つ. ほかはnull
        private readonly IList<SavedRegion> regions;
        private List<Rectangle> chipRects;  // 一覧の名前(チップ)の位置. ローカル座標. nullなら作り直す
        private int hoverChip = -1;

        public OverlayForm(int monitorIndex, Rectangle bounds, uint dpi, Selection selection, IList<SavedRegion> regions)
        {
            this.monitorIndex = monitorIndex;
            this.bounds = bounds;
            this.dpi = dpi;
            this.scale = dpi / 96f;
            this.selection = selection;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Wakudori";
            Location = bounds.Location;
            Size = bounds.Size;

            this.regions = regions;
            selection.Changed += OnSelectionChanged;
        }

        public int MonitorIndex { get { return monitorIndex; } }
        public Rectangle MonitorBounds { get { return bounds; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            CreateSurface();
            Present();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // WinFormsが位置や大きさを変えている場合に備えて, 物理ピクセルで置き直す.
            Present();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_DPICHANGED)
            {
                // 既定の処理(提案された大きさへの変更)をさせない. 位置と大きさは物理ピクセルで固定する.
                m.Result = IntPtr.Zero;
                if (surface != null) Present();
                return;
            }
            base.WndProc(ref m);
        }

        // 一覧の中身が変わった(Ctrl+Sで保存した)ときに呼ぶ.
        public void RefreshRegions()
        {
            chipRects = null;
            hoverChip = -1;
            Present();
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            // 一覧があるフォームは, 選んだ名前の色が変わるので描き直す.
            if (regions != null && regions.Count > 0)
            {
                Present();
                return;
            }
            // 枠がこのモニターにある, または前回あった(消す必要がある)ときだけ描き直す.
            bool visible = selection.Frame.IntersectsWith(bounds);
            if (visible || frameWasVisible) Present();
        }

        // ---- 描画 ----

        private void CreateSurface()
        {
            if (surface != null) return; // ハンドルが作り直されても, 描画面は作り直さない
            IntPtr screenDC = NativeMethods.GetDC(IntPtr.Zero);
            try
            {
                memDC = NativeMethods.CreateCompatibleDC(screenDC);
                NativeMethods.BITMAPINFOHEADER bmi = new NativeMethods.BITMAPINFOHEADER();
                bmi.biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER));
                bmi.biWidth = bounds.Width;
                bmi.biHeight = -bounds.Height; // 上から下へ並ぶDIB
                bmi.biPlanes = 1;
                bmi.biBitCount = 32;
                bmi.biCompression = NativeMethods.BI_RGB;
                IntPtr bits;
                hBitmap = NativeMethods.CreateDIBSection(screenDC, ref bmi, NativeMethods.DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
                if (hBitmap == IntPtr.Zero)
                {
                    throw new InvalidOperationException("CreateDIBSection failed: " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                }
                oldBitmap = NativeMethods.SelectObject(memDC, hBitmap);
                surface = new Bitmap(bounds.Width, bounds.Height, bounds.Width * 4, PixelFormat.Format32bppPArgb, bits);
                surface.SetResolution(96f, 96f); // フォントの換算をDPIに左右されないようにする(拡大はscaleで行う)
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDC);
            }
        }

        private int Scaled(int px96)
        {
            return Math.Max(1, (int)Math.Round(px96 * scale));
        }

        private void Render()
        {
            Rectangle frame = selection.Frame;
            bool visible = frame.IntersectsWith(bounds);
            frameWasVisible = visible;

            using (Graphics g = Graphics.FromImage(surface))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.None;

                g.Clear(DimColor);

                if (visible)
                {
                    Rectangle local = ToLocal(frame);
                    using (SolidBrush inside = new SolidBrush(Color.FromArgb(InsideAlpha, 0, 0, 0)))
                    {
                        g.FillRectangle(inside, local);
                    }

                    DrawBorder(g, local);
                    DrawKnobs(g, local);
                }

                if (regions != null && regions.Count > 0) DrawRegionList(g);

            }
        }

        // 線の太さ. DPIに合わせて太くする(100%で1px, 150%で2px)
        private int BorderWidth
        {
            get { return Scaled(1); }
        }

        // 線はアイボリーの細い実線で, 範囲のすぐ外側の太さw画素に塗る(撮るときは選択画面を消すので画像には入らない).
        // 選んでも, カーソルを乗せても, 線の太さ・位置・色は変えない(変えると辺が動いたように見えるため).
        private void DrawBorder(Graphics g, Rectangle local)
        {
            int w = BorderWidth;
            g.CompositingMode = CompositingMode.SourceOver;
            using (SolidBrush line = new SolidBrush(LineColor))
            {
                // 上下の帯は角の画素を含み, 左右の帯は含まない(同じ画素を2回塗らないため).
                g.FillRectangle(line, local.Left - w, local.Top - w, local.Width + w * 2, w);
                g.FillRectangle(line, local.Left - w, local.Bottom, local.Width + w * 2, w);
                g.FillRectangle(line, local.Left - w, local.Top, w, local.Height);
                g.FillRectangle(line, local.Right, local.Top, w, local.Height);
            }
            g.CompositingMode = CompositingMode.SourceCopy;
        }

        // 選んだ辺(角)と, カーソルを乗せた辺(角)の外側に, つまみを出す(B12, 写真アプリのトリミング風).
        // 辺は中央に太い線, 角はL字. 選んだほうは濃く, 乗せただけのほうは半透明.
        // 四隅の丸は出さない. つまみは線の外側にあるので, 撮る範囲の中身は隠さない.
        private void DrawKnobs(Graphics g, Rectangle local)
        {
            FrameHandle sel = selection.SelectedHandle;
            FrameHandle hover = selection.HoverHandle;
            if (sel == FrameHandle.None && hover == FrameHandle.None) return;

            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half; // 塗りの四角を画素の境目にそろえる
            if (hover != FrameHandle.None && hover != sel) DrawKnob(g, local, hover, Color.FromArgb(KnobHoverAlpha, KnobColor));
            if (sel != FrameHandle.None) DrawKnob(g, local, sel, KnobColor);
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.SmoothingMode = SmoothingMode.None;
            g.CompositingMode = CompositingMode.SourceCopy;
        }

        private void DrawKnob(Graphics g, Rectangle local, FrameHandle h, Color color)
        {
            int th = Scaled(KnobThick96);
            int o = BorderWidth + Scaled(KnobGap96); // 範囲の端からつまみまでの距離
            int l = local.Left, t = local.Top, r = local.Right, b = local.Bottom;
            float radius = th / 2f;
            using (SolidBrush brush = new SolidBrush(color))
            {
                if (h == FrameHandle.TopLeft || h == FrameHandle.TopRight || h == FrameHandle.BottomRight || h == FrameHandle.BottomLeft)
                {
                    int len = Scaled(KnobCorner96);
                    int cx = MovesLeft(h) ? l - o - th : r + o;   // 縦の棒の左端
                    int cy = MovesTop(h) ? t - o - th : b + o;    // 横の棒の上端
                    int hx = MovesLeft(h) ? cx : cx + th - len;   // 横の棒の左端
                    int vy = MovesTop(h) ? cy : cy + th - len;    // 縦の棒の上端
                    FillRounded(g, brush, new RectangleF(hx, cy, len, th), radius);
                    FillRounded(g, brush, new RectangleF(cx, vy, th, len), radius);
                    return;
                }
                bool horizontal = h == FrameHandle.Top || h == FrameHandle.Bottom;
                int edge = horizontal ? local.Width : local.Height;
                int knob = Math.Max(th, Math.Min(Scaled(KnobLength96), (int)(edge * 0.4f)));
                if (h == FrameHandle.Top) FillRounded(g, brush, new RectangleF(l + (local.Width - knob) / 2, t - o - th, knob, th), radius);
                if (h == FrameHandle.Bottom) FillRounded(g, brush, new RectangleF(l + (local.Width - knob) / 2, b + o, knob, th), radius);
                if (h == FrameHandle.Left) FillRounded(g, brush, new RectangleF(l - o - th, t + (local.Height - knob) / 2, th, knob), radius);
                if (h == FrameHandle.Right) FillRounded(g, brush, new RectangleF(r + o, t + (local.Height - knob) / 2, th, knob), radius);
            }
        }

        private static void FillRounded(Graphics g, Brush brush, RectangleF rc, float radius)
        {
            using (GraphicsPath path = Theme.RoundedRect(rc, radius))
            {
                g.FillPath(brush, path);
            }
        }

        // ---- 保存した範囲の一覧(A4, A7) ----
        // モニターの上部の中央に, 名前をアイボリーのチップで並べる. 入りきらなければ折り返す.
        // 今の枠が, 選んだ名前付きの範囲のまま(動かしていない)なら, そのチップをクレイ色にする(D5).

        private const int ChipHeight96 = 32;
        private const int ChipPadX96 = 14;
        private const int ChipGap96 = 8;
        private const int ChipTop96 = 24;
        private const int ChipFont96 = 14;

        private void LayoutChips(Graphics g)
        {
            chipRects = new List<Rectangle>();
            int h = Scaled(ChipHeight96), pad = Scaled(ChipPadX96), gap = Scaled(ChipGap96);
            int maxRow = Math.Max(h, bounds.Width - Scaled(64));
            List<List<Rectangle>> rows = new List<List<Rectangle>>();
            List<Rectangle> row = new List<Rectangle>();
            int x = 0, y = Scaled(ChipTop96);
            using (Font font = Theme.Body(ChipFont96 * scale))
            {
                for (int i = 0; i < regions.Count; i++)
                {
                    SizeF ts = g.MeasureString(regions[i].Name ?? "", font, PointF.Empty, StringFormat.GenericTypographic);
                    int w = Math.Min(maxRow, (int)Math.Ceiling(ts.Width) + pad * 2);
                    if (row.Count > 0 && x + gap + w > maxRow)
                    {
                        rows.Add(row);
                        row = new List<Rectangle>();
                        x = 0;
                        y += h + gap;
                    }
                    if (row.Count > 0) x += gap;
                    row.Add(new Rectangle(x, y, w, h));
                    x += w;
                }
            }
            if (row.Count > 0) rows.Add(row);
            foreach (List<Rectangle> r in rows)
            {
                int width = r[r.Count - 1].Right;
                int offset = (bounds.Width - width) / 2;
                foreach (Rectangle c in r) chipRects.Add(new Rectangle(c.X + offset, c.Y, c.Width, c.Height));
            }
        }

        private void DrawRegionList(Graphics g)
        {
            if (chipRects == null || chipRects.Count != regions.Count) LayoutChips(g);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; // ClearTypeは透明度つきの面だと汚くなる
            using (Font font = Theme.Body(ChipFont96 * scale))
            using (StringFormat fmt = new StringFormat(StringFormat.GenericTypographic))
            {
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                fmt.Trimming = StringTrimming.EllipsisCharacter;
                fmt.FormatFlags |= StringFormatFlags.NoWrap;
                for (int i = 0; i < regions.Count; i++)
                {
                    Rectangle rc = chipRects[i];
                    bool active = selection.RegionName != null && selection.RegionName == regions[i].Name;
                    Color back = active ? Theme.AccentInteractive : (i == hoverChip ? Theme.BgMuted : Theme.Bg);
                    Color fore = active ? Color.White : Theme.Text;
                    using (GraphicsPath path = Theme.RoundedRect(rc, rc.Height / 2f))
                    using (SolidBrush b = new SolidBrush(back))
                    using (SolidBrush t = new SolidBrush(fore))
                    {
                        g.FillPath(b, path);
                        g.DrawString(regions[i].Name ?? "", font, t, rc, fmt);
                    }
                }
            }
            g.TextRenderingHint = TextRenderingHint.SystemDefault;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.SmoothingMode = SmoothingMode.None;
            g.CompositingMode = CompositingMode.SourceCopy;
        }

        // pの上にあるチップの番号. なければ-1
        private int HitChip(Point local)
        {
            if (regions == null || chipRects == null) return -1;
            for (int i = 0; i < chipRects.Count && i < regions.Count; i++)
            {
                if (chipRects[i].Contains(local)) return i;
            }
            return -1;
        }

        private void SetHoverChip(int i)
        {
            if (hoverChip == i) return;
            hoverChip = i;
            Present();
        }

        private void Present()
        {
            if (surface == null || !IsHandleCreated) return;
            Render();
            NativeMethods.GdiFlush();

            IntPtr screenDC = NativeMethods.GetDC(IntPtr.Zero);
            try
            {
                NativeMethods.POINT dst = new NativeMethods.POINT(bounds.X, bounds.Y);
                NativeMethods.SIZE size = new NativeMethods.SIZE(bounds.Width, bounds.Height);
                NativeMethods.POINT src = new NativeMethods.POINT(0, 0);
                NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION();
                blend.BlendOp = NativeMethods.AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = NativeMethods.AC_SRC_ALPHA;
                NativeMethods.UpdateLayeredWindow(Handle, screenDC, ref dst, ref size, memDC, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDC);
            }
        }

        private Rectangle ToLocal(Rectangle r)
        {
            return new Rectangle(r.X - bounds.X, r.Y - bounds.Y, r.Width, r.Height);
        }

        // e.Locationはクライアント座標(物理ピクセル). モニター左上からの位置と同じ.
        // マウスをキャプチャ中は, ほかのモニター上の位置も同じ座標系で届く.
        private Point ToVirtual(Point client)
        {
            return new Point(client.X + bounds.X, client.Y + bounds.Y);
        }

        // ---- マウス ----

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            // 一覧の名前をクリックしたら, その範囲を枠にセットする(A4). 枠の操作より優先する.
            int chip = HitChip(e.Location);
            if (chip >= 0)
            {
                if (RegionChosen != null) RegionChosen(this, regions[chip]);
                return;
            }

            Point p = ToVirtual(e.Location);
            Rectangle frame = selection.Frame;

            FrameHandle hit = Selection.HitTest(frame, p, Scaled(Grip96));
            if (hit != FrameHandle.None)
            {
                // 辺・角をクリックすると選んだ状態になり, そのままドラッグすると辺を動かす(Macのように8点以外もつかめる).
                selection.SelectedHandle = hit;
                dragMode = DragMode.FrameHandle;
                dragHandle = hit;
            }
            else if (frame.Contains(p))
            {
                // 枠の内側をクリックするとハンドルの選択を外し, ドラッグすると枠全体を動かす.
                selection.SelectedHandle = FrameHandle.None;
                dragMode = DragMode.Move;
            }
            else
            {
                // 枠の外をクリックしても選択を外す. 枠の外のドラッグで範囲を選び直す操作は作らない(B3).
                selection.SelectedHandle = FrameHandle.None;
                return;
            }
            dragStart = p;
            frameAtDragStart = frame;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point p = ToVirtual(e.Location);
            int tdx = p.X - dragStart.X;
            int tdy = p.Y - dragStart.Y;

            if (dragMode == DragMode.Move)
            {
                selection.MoveTo(new Point(frameAtDragStart.X + tdx, frameAtDragStart.Y + tdy), p);
            }
            else if (dragMode == DragMode.FrameHandle)
            {
                // ドラッグ開始からの移動量で「動かしたい辺の位置」を決め, 今の枠との差を渡す.
                // 端で止められても, マウスと辺の位置がずれていかない.
                Rectangle f = selection.Frame;
                int dx = 0, dy = 0;
                if (MovesLeft(dragHandle)) dx = (frameAtDragStart.Left + tdx) - f.Left;
                else if (MovesRight(dragHandle)) dx = (frameAtDragStart.Right + tdx) - f.Right;
                if (MovesTop(dragHandle)) dy = (frameAtDragStart.Top + tdy) - f.Top;
                else if (MovesBottom(dragHandle)) dy = (frameAtDragStart.Bottom + tdy) - f.Bottom;
                if (dx != 0 || dy != 0) selection.MoveEdges(dragHandle, dx, dy);
            }
            else
            {
                int chip = HitChip(e.Location);
                SetHoverChip(chip);
                if (chip >= 0)
                {
                    selection.HoverHandle = FrameHandle.None;
                    if (Cursor != Cursors.Hand) Cursor = Cursors.Hand;
                    return;
                }
                UpdateCursor(p);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && dragMode != DragMode.None)
            {
                dragMode = DragMode.None;
                dragHandle = FrameHandle.None;
                Capture = false;
                UpdateCursor(ToVirtual(e.Location));
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (dragMode == DragMode.None) selection.HoverHandle = FrameHandle.None;
            SetHoverChip(-1);
        }

        private void UpdateCursor(Point p)
        {
            Rectangle frame = selection.Frame;
            FrameHandle hit = Selection.HitTest(frame, p, Scaled(Grip96));
            selection.HoverHandle = hit; // つかむ場所の予告
            Cursor c;
            switch (hit)
            {
                case FrameHandle.TopLeft:
                case FrameHandle.BottomRight: c = Cursors.SizeNWSE; break;
                case FrameHandle.TopRight:
                case FrameHandle.BottomLeft: c = Cursors.SizeNESW; break;
                case FrameHandle.Top:
                case FrameHandle.Bottom: c = Cursors.SizeNS; break;
                case FrameHandle.Left:
                case FrameHandle.Right: c = Cursors.SizeWE; break;
                default: c = frame.Contains(p) ? Cursors.SizeAll : Cursors.Default; break;
            }
            if (Cursor != c) Cursor = c;
        }

        private static bool MovesLeft(FrameHandle h) { return h == FrameHandle.TopLeft || h == FrameHandle.Left || h == FrameHandle.BottomLeft; }
        private static bool MovesRight(FrameHandle h) { return h == FrameHandle.TopRight || h == FrameHandle.Right || h == FrameHandle.BottomRight; }
        private static bool MovesTop(FrameHandle h) { return h == FrameHandle.TopLeft || h == FrameHandle.Top || h == FrameHandle.TopRight; }
        private static bool MovesBottom(FrameHandle h) { return h == FrameHandle.BottomLeft || h == FrameHandle.Bottom || h == FrameHandle.BottomRight; }

        // ---- キーボード ----

        // 矢印キーはフォームのフォーカス移動に使われてOnKeyDownまで届かないことがあるので, ここで受ける.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) == Keys.Control;
            int step = ctrl ? 10 : 1;

            if (keyData == (Keys.Control | Keys.S))
            {
                // 今の枠に名前を付けて保存する(A3).
                if (dragMode == DragMode.None && SaveRequested != null) SaveRequested(this, EventArgs.Empty);
                return true;
            }

            switch (key)
            {
                case Keys.Left: Nudge(-step, 0); return true;
                case Keys.Right: Nudge(step, 0); return true;
                case Keys.Up: Nudge(0, -step); return true;
                case Keys.Down: Nudge(0, step); return true;
                case Keys.Enter:
                    if (CaptureRequested != null) CaptureRequested(this, EventArgs.Empty);
                    return true;
                case Keys.Escape:
                    // 辺・角を選んでいればまず選択を外す. 選んでいなければ閉じる.
                    if (selection.SelectedHandle != FrameHandle.None)
                    {
                        if (dragMode == DragMode.None) selection.SelectedHandle = FrameHandle.None;
                    }
                    else if (CancelRequested != null)
                    {
                        CancelRequested(this, EventArgs.Empty);
                    }
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Nudge(int dx, int dy)
        {
            if (dragMode != DragMode.None) return; // ドラッグ中は矢印キーを無視する
            // 動く辺を必ず見せる. カーソルの辺の予告は, マウスを動かすまで消す.
            selection.HoverHandle = FrameHandle.None;
            if (selection.SelectedHandle != FrameHandle.None)
            {
                selection.MoveEdges(selection.SelectedHandle, dx, dy);
            }
            else
            {
                selection.MoveBy(dx, dy);
            }
        }

        // ---- 終了 ----

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing) // サインアウトなどは止めない
            {
                // Alt+F4などで1枚だけ閉じず, Escと同じく全体を終える.
                e.Cancel = true;
                if (CancelRequested != null) CancelRequested(this, EventArgs.Empty);
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                selection.Changed -= OnSelectionChanged;
                if (surface != null)
                {
                    surface.Dispose();
                    surface = null;
                }
            }
            if (memDC != IntPtr.Zero)
            {
                if (oldBitmap != IntPtr.Zero) NativeMethods.SelectObject(memDC, oldBitmap);
                NativeMethods.DeleteDC(memDC);
                memDC = IntPtr.Zero;
            }
            if (hBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(hBitmap);
                hBitmap = IntPtr.Zero;
            }
            base.Dispose(disposing);
        }
    }
}
