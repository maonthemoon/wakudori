using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Wakudori
{
    // 1つのモニターを覆う選択画面.
    // 異なるDPIのモニターをまたぐ1枚のウィンドウはずれやすいので, モニターごとに1枚ずつ作る.
    // 枠(Selection)は全フォームで共有し, 枠が変わったら関係するフォームだけ描き直す.
    // 描画はUpdateLayeredWindow(ピクセル単位のアルファ)で行い, 下の画面は動いたまま見える.
    internal sealed class OverlayForm : Form
    {
        private const int DimAlpha = 110;   // 範囲の外の暗さ
        private const int InsideAlpha = 1;  // 範囲の内側. 0にするとクリックが下へ抜けるので1にする
        private const int HandleSize96 = 10; // ハンドル(丸)の直径(96 DPIのとき)
        private const int Grip96 = 5;       // 辺をつかめる幅(96 DPIのとき). 角はこの2倍
        private const int Dash96 = 3;       // 破線の長さと間隔(96 DPIのとき)

        // 選んだ辺の色. Anthropicのクレイ色
        internal static readonly Color AccentColor = Color.FromArgb(255, 0xD9, 0x77, 0x57);
        // 線の色. 明るい画面でも暗い画面でも見えるよう, 白地に黒の破線の2色にする
        private static readonly Color BorderBackColor = Color.FromArgb(230, 255, 255, 255);     // 破線の地(白)
        private static readonly Color BorderDashColor = Color.FromArgb(255, 0x20, 0x20, 0x20);  // 破線(黒)
        private static readonly Color HandleFillColor = Color.FromArgb(255, 0xF5, 0xF5, 0xF5);  // 丸の中
        private static readonly Color HandleBorderColor = Color.FromArgb(255, 0x50, 0x50, 0x50); // 丸のふち
        private const float HandleBorder96 = 1.5f; // 丸のふちの太さ(96 DPIのとき)

        private enum DragMode { None, Move, FrameHandle }

        private readonly int monitorIndex;
        private readonly Rectangle bounds;  // モニターの範囲(仮想スクリーン座標, 物理ピクセル)
        private readonly uint dpi;
        private readonly float scale;
        private readonly Selection selection;
        private readonly bool showHelp;

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

        public OverlayForm(int monitorIndex, Rectangle bounds, uint dpi, Selection selection, bool showHelp)
        {
            this.monitorIndex = monitorIndex;
            this.bounds = bounds;
            this.dpi = dpi;
            this.scale = dpi / 96f;
            this.selection = selection;
            this.showHelp = showHelp;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Wakudori";
            Location = bounds.Location;
            Size = bounds.Size;

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

        private void OnSelectionChanged(object sender, EventArgs e)
        {
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

                g.Clear(Color.FromArgb(DimAlpha, 0, 0, 0));

                if (visible)
                {
                    Rectangle local = ToLocal(frame);
                    using (SolidBrush inside = new SolidBrush(Color.FromArgb(InsideAlpha, 0, 0, 0)))
                    {
                        g.FillRectangle(inside, local);
                    }

                    DrawBorder(g, local);
                    DrawHandles(g, local);
                }

                if (showHelp) DrawHelp(g);
            }
        }

        // 線の太さ. DPIに合わせて太くする(100%で1px, 150%で2px)
        private int BorderWidth
        {
            get { return Scaled(1); }
        }

        // 線の中心線(ローカル座標, 小数). 線は範囲のすぐ外側に描き, 太くするときは外側へ広げる.
        // 撮るときは選択画面を消すので, 線も丸も画像には入らない.
        // GDI+(PixelOffsetMode.None)では画素の中心が整数座標なので,
        // 範囲の左端の画素がlocal.Xのとき, 1px外側の画素の中心はlocal.X - 1.
        // 太さwの線を外側のw画素に収めると, 中心線は local.X - 1 - (w - 1) / 2 になる.
        private RectangleF BorderCenterLine(Rectangle local)
        {
            float c = 1f + (BorderWidth - 1) / 2f;
            return new RectangleF(local.Left - c, local.Top - c,
                (local.Right - 1 + c) - (local.Left - c), (local.Bottom - 1 + c) - (local.Top - c));
        }

        private void DrawBorder(Graphics g, Rectangle local)
        {
            RectangleF line = BorderCenterLine(local);
            float w = BorderWidth;
            g.CompositingMode = CompositingMode.SourceOver;
            using (Pen back = new Pen(BorderBackColor, w))
            using (Pen dash = new Pen(BorderDashColor, w))
            {
                // DashPatternは線の太さを単位にするので, 太さで割って画素の長さをそろえる
                float len = Scaled(Dash96) / w;
                dash.DashPattern = new float[] { len, len };
                g.DrawRectangle(back, line.X, line.Y, line.Width, line.Height);
                g.DrawRectangle(dash, line.X, line.Y, line.Width, line.Height);
            }

            // 選んだ辺(角なら2辺)は, クレイ色の実線で示す. 矢印キーで動くのはこの辺. 中心線は破線と同じ.
            FrameHandle h = selection.SelectedHandle;
            if (h != FrameHandle.None)
            {
                using (Pen accent = new Pen(AccentColor, Math.Max(w + 1, Scaled(2))))
                {
                    if (MovesLeft(h)) g.DrawLine(accent, line.Left, line.Top, line.Left, line.Bottom);
                    if (MovesRight(h)) g.DrawLine(accent, line.Right, line.Top, line.Right, line.Bottom);
                    if (MovesTop(h)) g.DrawLine(accent, line.Left, line.Top, line.Right, line.Top);
                    if (MovesBottom(h)) g.DrawLine(accent, line.Left, line.Bottom, line.Right, line.Bottom);
                }
            }
            g.CompositingMode = CompositingMode.SourceCopy;
        }

        // ハンドルはMacのように明るいグレーの小さな丸. 中心は線の中心線の上(四隅は線の交点).
        // 選んだ辺の上の丸はクレイ色にする.
        private void DrawHandles(Graphics g, Rectangle local)
        {
            RectangleF line = BorderCenterLine(local);
            float l = line.Left, r = line.Right, t = line.Top, b = line.Bottom;
            float cx = (l + r) / 2f, cy = (t + b) / 2f;
            // FrameHandleの順(TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left)
            PointF[] centers = new PointF[]
            {
                new PointF(l, t), new PointF(cx, t), new PointF(r, t), new PointF(r, cy),
                new PointF(r, b), new PointF(cx, b), new PointF(l, b), new PointF(l, cy)
            };

            float d = Scaled(HandleSize96);
            FrameHandle sel = selection.SelectedHandle;
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush normal = new SolidBrush(HandleFillColor))
            using (SolidBrush accent = new SolidBrush(AccentColor))
            using (Pen border = new Pen(HandleBorderColor, HandleBorder96 * scale))
            {
                for (int i = 0; i < centers.Length; i++)
                {
                    RectangleF rc = new RectangleF(centers[i].X - d / 2f, centers[i].Y - d / 2f, d, d);
                    FrameHandle h = (FrameHandle)(i + 1);
                    g.FillEllipse(OnSelectedEdge(h, sel) ? accent : normal, rc);
                    g.DrawEllipse(border, rc);
                }
            }
            g.SmoothingMode = SmoothingMode.None;
            g.CompositingMode = CompositingMode.SourceCopy;
        }

        // ハンドルhが, 選んだ辺(角なら2辺)の上にあるか
        private static bool OnSelectedEdge(FrameHandle h, FrameHandle sel)
        {
            if (sel == FrameHandle.None) return false;
            if (h == sel) return true;
            bool selCorner = sel == FrameHandle.TopLeft || sel == FrameHandle.TopRight || sel == FrameHandle.BottomLeft || sel == FrameHandle.BottomRight;
            if (selCorner) return false; // 角を選んだときは, その角の丸だけを塗る
            // 辺を選んだときは, その辺の両端の角の丸も塗る
            return (MovesLeft(sel) && MovesLeft(h)) || (MovesRight(sel) && MovesRight(h))
                || (MovesTop(sel) && MovesTop(h)) || (MovesBottom(sel) && MovesBottom(h));
        }

        private void DrawHelp(Graphics g)
        {
            const string text = "辺・角のドラッグで大きさ, 内側のドラッグで位置 / 辺・角をクリックして矢印キーで調整 (Ctrl: 10px) / 内側をクリックで解除 / Enter: 撮影 / Esc: 閉じる";
            g.CompositingMode = CompositingMode.SourceOver;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; // ClearTypeは透明な面だと汚くなる
            using (Font font = new Font("Yu Gothic UI", 10f * scale, FontStyle.Regular, GraphicsUnit.Point))
            using (SolidBrush back = new SolidBrush(Color.FromArgb(220, 30, 30, 30)))
            using (SolidBrush fore = new SolidBrush(Color.White))
            {
                SizeF size = g.MeasureString(text, font);
                int pad = Scaled(6);
                int w = (int)Math.Ceiling(size.Width) + pad * 2;
                int h = (int)Math.Ceiling(size.Height) + pad * 2;
                // A7で上部に範囲の一覧を出すので, 説明は下部の中央に出す.
                Rectangle box = new Rectangle((bounds.Width - w) / 2, bounds.Height - h - Scaled(48), w, h);
                g.FillRectangle(back, box);
                g.DrawString(text, font, fore, box.X + pad, box.Y + pad);
            }
            g.CompositingMode = CompositingMode.SourceCopy;
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
                return; // 枠の外のドラッグで範囲を選び直す操作は作らない(B3)
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

        private void UpdateCursor(Point p)
        {
            Rectangle frame = selection.Frame;
            FrameHandle hit = Selection.HitTest(frame, p, Scaled(Grip96));
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
                    if (CancelRequested != null) CancelRequested(this, EventArgs.Empty);
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Nudge(int dx, int dy)
        {
            if (dragMode != DragMode.None) return; // ドラッグ中は矢印キーを無視する
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
