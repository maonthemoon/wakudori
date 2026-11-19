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
    // 描画はUpdateLayeredWindow(ピクセル単位のアルファ)で行い, 下の画面は動いたまま見える.
    internal sealed class OverlayForm : Form
    {
        private const int DimAlpha = 110;   // 範囲の外の暗さ
        private const int InsideAlpha = 1;  // 範囲の内側. 0にするとクリックが下へ抜けるので1にする

        private readonly int monitorIndex;
        private readonly Screen screen;
        private readonly Rectangle bounds;  // モニターの範囲(仮想スクリーン座標, 物理ピクセル)
        private readonly uint dpi;

        private Rectangle frame;            // 撮る範囲(仮想スクリーン座標, 物理ピクセル)
        private bool dragging;
        private Point dragStart;
        private Rectangle frameAtDragStart;

        private IntPtr memDC = IntPtr.Zero;
        private IntPtr hBitmap = IntPtr.Zero;
        private IntPtr oldBitmap = IntPtr.Zero;
        private Bitmap surface;             // hBitmapのピクセルを直接指すBitmap(PArgb)

        public bool AllowClose;             // trueのときだけ閉じられる(Alt+F4で1枚だけ閉じないように)

        public event EventHandler CaptureRequested;
        public event EventHandler CancelRequested;

        public OverlayForm(int monitorIndex, Screen screen, uint dpi, Size frameSize)
        {
            this.monitorIndex = monitorIndex;
            this.screen = screen;
            this.bounds = screen.Bounds;
            this.dpi = dpi;

            int w = Math.Min(frameSize.Width, bounds.Width);
            int h = Math.Min(frameSize.Height, bounds.Height);
            frame = new Rectangle(bounds.X + (bounds.Width - w) / 2, bounds.Y + (bounds.Height - h) / 2, w, h);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            Text = "Wakudori";
            Location = bounds.Location;
            Size = bounds.Size;
        }

        public int MonitorIndex { get { return monitorIndex; } }
        public Screen MonitorScreen { get { return screen; } }
        public uint Dpi { get { return dpi; } }
        public Rectangle Frame { get { return frame; } }

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

        private void Render()
        {
            Rectangle local = new Rectangle(frame.X - bounds.X, frame.Y - bounds.Y, frame.Width, frame.Height);
            float scale = dpi / 96f;

            using (Graphics g = Graphics.FromImage(surface))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.None;

                g.Clear(Color.FromArgb(DimAlpha, 0, 0, 0));
                using (SolidBrush inside = new SolidBrush(Color.FromArgb(InsideAlpha, 0, 0, 0)))
                {
                    g.FillRectangle(inside, local);
                }

                // 点線の枠は範囲のすぐ外側の1pxに描く(撮った画像に入らないように).
                Rectangle line = new Rectangle(local.X - 1, local.Y - 1, local.Width + 1, local.Height + 1);
                using (Pen white = new Pen(Color.White, 1f))
                using (Pen black = new Pen(Color.Black, 1f))
                {
                    black.DashPattern = new float[] { 4f, 4f };
                    g.DrawRectangle(white, line);
                    g.DrawRectangle(black, line);
                }

                DrawLabel(g, scale);
            }
        }

        private void DrawLabel(Graphics g, float scale)
        {
            string text = string.Format(
                "モニター{0}{1}  {2}  {3}×{4}  DPI {5} ({6}%)\r\n枠の内側をドラッグで移動 / Enter: 全モニターの枠を撮影 / Esc: 終了",
                monitorIndex + 1,
                screen.Primary ? " (メイン)" : "",
                screen.DeviceName,
                bounds.Width, bounds.Height,
                dpi, (int)Math.Round(dpi * 100.0 / 96.0));

            g.CompositingMode = CompositingMode.SourceOver;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; // ClearTypeは透明な面だと汚くなる
            using (Font font = new Font("Yu Gothic UI", 11f * scale, FontStyle.Regular, GraphicsUnit.Point))
            using (SolidBrush back = new SolidBrush(Color.FromArgb(220, 30, 30, 30)))
            using (SolidBrush fore = new SolidBrush(Color.White))
            {
                SizeF size = g.MeasureString(text, font);
                int pad = (int)(8 * scale);
                Rectangle box = new Rectangle(pad, pad, (int)Math.Ceiling(size.Width) + pad * 2, (int)Math.Ceiling(size.Height) + pad * 2);
                g.FillRectangle(back, box);
                g.DrawString(text, font, fore, box.X + pad, box.Y + pad);
            }
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

        // e.Locationはクライアント座標(物理ピクセル). モニター左上からの位置と同じ.
        private Point ToVirtual(Point client)
        {
            return new Point(client.X + bounds.X, client.Y + bounds.Y);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Point p = ToVirtual(e.Location);
            if (e.Button == MouseButtons.Left && frame.Contains(p))
            {
                dragging = true;
                dragStart = p;
                frameAtDragStart = frame;
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point p = ToVirtual(e.Location);
            if (dragging)
            {
                int x = frameAtDragStart.X + (p.X - dragStart.X);
                int y = frameAtDragStart.Y + (p.Y - dragStart.Y);
                // 枠はこのモニターの中に収める.
                x = Math.Max(bounds.Left, Math.Min(x, bounds.Right - frame.Width));
                y = Math.Max(bounds.Top, Math.Min(y, bounds.Bottom - frame.Height));
                if (x != frame.X || y != frame.Y)
                {
                    frame = new Rectangle(x, y, frame.Width, frame.Height);
                    Present();
                }
            }
            Cursor = frame.Contains(p) ? Cursors.SizeAll : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && dragging)
            {
                dragging = false;
                Capture = false;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                if (CaptureRequested != null) CaptureRequested(this, EventArgs.Empty);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                if (CancelRequested != null) CancelRequested(this, EventArgs.Empty);
            }
        }

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
            if (disposing && surface != null)
            {
                surface.Dispose();
                surface = null;
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
