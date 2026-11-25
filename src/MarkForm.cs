using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Wakudori
{
    // 撮った画像に書き込む画面(H5, H7, H8). モニター全体を覆い, 撮ったのと同じ位置に等倍で画像を出す.
    // ドラッグ=ペン, Shift+ドラッグ=黒の塗りつぶし, Ctrl+Z/Ctrl+Y, 数字キーかパレットで色, Tabでパレットを隠す.
    // Enter=クリップボードと<元の名前>_edit.pngに出して閉じる, Esc=書き込みを捨てて閉じる(元の画像は残る).
    internal sealed class MarkForm : Form
    {
        private const int PenWidth96 = 4;
        private const int Swatch96 = 24;
        private const int SwatchGap96 = 12;
        private const int BarPad96 = 14;
        private const int BarGap96 = 16;

        private static readonly Color Back = Color.FromArgb(0x2A, 0x2A, 0x28);

        // 先頭が既定の色(デザインのクレイ色)
        private static readonly Color[] Colors = new Color[]
        {
            Theme.Accent,
            Color.FromArgb(0xD6, 0x2F, 0x2F), // 赤
            Color.FromArgb(0xF2, 0xC2, 0x30), // 黄
            Color.FromArgb(0x3A, 0x9A, 0x5B), // 緑
            Color.FromArgb(0x2F, 0x74, 0xD0), // 青
            Color.FromArgb(0x14, 0x14, 0x13), // 黒
            Color.FromArgb(0xFF, 0xFF, 0xFF)  // 白
        };

        private readonly Rectangle monitor;  // 仮想スクリーン座標
        private readonly Rectangle imageRect; // 画面の左上からの位置
        private readonly float scale;
        private readonly string originalPath;
        private readonly Bitmap original;    // 撮ったままの画像
        private readonly Bitmap canvas;      // 書き込みを重ねた画像
        private readonly MarkList marks = new MarkList();
        private readonly Rectangle barRect;
        private readonly bool barOverImage;
        private MarkItem current;            // 書いている途中のもの
        private Point fillStart;
        private int colorIndex;
        private bool barHidden;

        // image: 撮った画像(この画面が破棄する). imageBounds: 撮った範囲. originalPath: 保存済みの元の画像
        public MarkForm(Bitmap image, Rectangle imageBounds, Rectangle monitor, float scale, string originalPath)
        {
            this.monitor = monitor;
            this.scale = scale;
            this.originalPath = originalPath;
            original = image;
            imageRect = new Rectangle(imageBounds.X - monitor.X, imageBounds.Y - monitor.Y, imageBounds.Width, imageBounds.Height);

            canvas = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppPArgb);
            RebuildCanvas();

            Size bar = new Size(S(BarPad96) * 2 + S(Swatch96) * Colors.Length + S(SwatchGap96) * (Colors.Length - 1), S(BarPad96) * 2 + S(Swatch96));
            barRect = MarkLayout.PlacePalette(monitor.Size, imageRect, bar, S(BarGap96));
            barOverImage = barRect.IntersectsWith(imageRect);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            ShowInTaskbar = true;
            Text = "Wakudori - 書き込み";
            BackColor = Back;
            DoubleBuffered = true;
            ImeMode = ImeMode.Disable; // 日本語入力がオンでも, 数字キーなどが届くように
            Bounds = monitor;
        }

        private int S(int px96)
        {
            return Math.Max(1, (int)Math.Round(px96 * scale));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = monitor; // WinFormsが位置や大きさを変えている場合に備える
            Activate();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_DPICHANGED)
            {
                m.Result = IntPtr.Zero; // 位置と大きさは物理ピクセルで固定する
                return;
            }
            base.WndProc(ref m);
        }

        // ---- 描画 ----

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.PageUnit = GraphicsUnit.Pixel;
            Rectangle part = Rectangle.Intersect(e.ClipRectangle, imageRect);
            if (part.Width > 0 && part.Height > 0)
            {
                GraphicsState state = g.Save();
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(canvas, part, part.X - imageRect.X, part.Y - imageRect.Y, part.Width, part.Height, GraphicsUnit.Pixel);
                g.Restore(state);
                if (current != null)
                {
                    state = g.Save();
                    g.SetClip(imageRect, CombineMode.Intersect);
                    g.TranslateTransform(imageRect.X, imageRect.Y);
                    DrawMark(g, current);
                    g.Restore(state);
                }
            }

            // 画像のふち. 画像のすぐ外側に引くので, 画像の中身は隠さない
            int w = S(1);
            using (SolidBrush line = new SolidBrush(Theme.Border))
            {
                g.FillRectangle(line, imageRect.Left - w, imageRect.Top - w, imageRect.Width + w * 2, w);
                g.FillRectangle(line, imageRect.Left - w, imageRect.Bottom, imageRect.Width + w * 2, w);
                g.FillRectangle(line, imageRect.Left - w, imageRect.Top, w, imageRect.Height);
                g.FillRectangle(line, imageRect.Right, imageRect.Top, w, imageRect.Height);
            }

            if (BarVisible && e.ClipRectangle.IntersectsWith(barRect)) DrawBar(g);
        }

        private bool BarVisible
        {
            // 画像に重なっているときは, 書いている間は隠す
            get { return !barHidden && !(barOverImage && current != null); }
        }

        private void DrawBar(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (GraphicsPath path = Theme.RoundedRect(barRect, barRect.Height / 2f))
            using (SolidBrush bg = new SolidBrush(Theme.Bg))
            {
                g.FillPath(bg, path);
            }
            for (int i = 0; i < Colors.Length; i++)
            {
                RectangleF rc = SwatchRect(i);
                using (SolidBrush b = new SolidBrush(Colors[i]))
                using (Pen edge = new Pen(Theme.Border, S(1)))
                {
                    g.FillEllipse(b, rc);
                    g.DrawEllipse(edge, rc);
                }
                if (i != colorIndex) continue;
                using (Pen ring = new Pen(Theme.Text, S(2)))
                {
                    float o = S(4);
                    g.DrawEllipse(ring, RectangleF.Inflate(rc, o, o));
                }
            }
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.SmoothingMode = SmoothingMode.None;
        }

        private Rectangle SwatchRect(int i)
        {
            return new Rectangle(barRect.X + S(BarPad96) + i * (S(Swatch96) + S(SwatchGap96)), barRect.Y + S(BarPad96), S(Swatch96), S(Swatch96));
        }

        // 座標は画像の左上から. 整数の座標は画素の中心
        private static void DrawMark(Graphics g, MarkItem m)
        {
            g.PixelOffsetMode = PixelOffsetMode.None;
            if (m.IsFill)
            {
                if (m.Rect.Width <= 0 || m.Rect.Height <= 0) return;
                g.SmoothingMode = SmoothingMode.None;
                using (SolidBrush b = new SolidBrush(m.Color)) g.FillRectangle(b, m.Rect);
                return;
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (m.Points.Count == 1)
            {
                Point p = m.Points[0];
                float d = m.Width * 1.5f; // 点は少し大きく
                using (SolidBrush b = new SolidBrush(m.Color)) g.FillEllipse(b, p.X - d / 2, p.Y - d / 2, d, d);
                return;
            }
            using (Pen pen = new Pen(m.Color, m.Width))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                if (m.Points.Count == 2) g.DrawLine(pen, m.Points[0], m.Points[1]);
                else g.DrawCurve(pen, m.Points.ToArray(), 0.5f);
            }
        }

        private void RebuildCanvas()
        {
            using (Graphics g = Graphics.FromImage(canvas))
            {
                GraphicsState state = g.Save();
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(original, new Rectangle(0, 0, canvas.Width, canvas.Height), 0, 0, original.Width, original.Height, GraphicsUnit.Pixel);
                g.Restore(state);
                foreach (MarkItem m in marks.Items) DrawMark(g, m);
            }
        }

        // ---- マウス ----

        private Point ToImage(Point p)
        {
            return new Point(p.X - imageRect.X, p.Y - imageRect.Y);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || current != null) return;
            if (BarVisible && barRect.Contains(e.Location))
            {
                int i = SwatchAt(e.Location);
                if (i >= 0) SelectColor(i);
                return;
            }
            if (!imageRect.Contains(e.Location)) return;

            Point p = ToImage(e.Location);
            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                current = MarkItem.Fill();
                fillStart = p;
                current.Rect = MarkItem.RectBetween(p, p, canvas.Size);
            }
            else
            {
                current = MarkItem.Pen(Colors[colorIndex], S(PenWidth96), p);
            }
            Capture = true;
            if (barOverImage) Invalidate(barRect);
            InvalidateImage(new Rectangle(p, new Size(1, 1)), S(PenWidth96) * 2);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (current == null)
            {
                Cursor c = Cursors.Default;
                if (BarVisible && barRect.Contains(e.Location)) c = SwatchAt(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
                else if (imageRect.Contains(e.Location)) c = Cursors.Cross;
                if (Cursor != c) Cursor = c;
                return;
            }

            Point p = ToImage(e.Location);
            if (current.IsFill)
            {
                Rectangle old = current.Rect;
                current.Rect = MarkItem.RectBetween(fillStart, p, canvas.Size);
                InvalidateImage(Rectangle.Union(old, current.Rect), 2);
                return;
            }
            if (!current.AddPoint(p)) return;
            // なめらかにつなぐので, 点を足すと1つ前の区間の形も変わる. 最後の4点の周りを描き直す
            int n = current.Points.Count;
            Rectangle r = new Rectangle(p, new Size(1, 1));
            for (int i = Math.Max(0, n - 4); i < n; i++) r = Rectangle.Union(r, new Rectangle(current.Points[i], new Size(1, 1)));
            InvalidateImage(r, current.Width * 2 + Math.Max(r.Width, r.Height) / 2);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || current == null) return;
            MarkItem m = current;
            current = null;
            Capture = false;
            // Shift+クリックだけ(ほぼ動かしていない)なら, 塗りつぶさない
            if (!m.IsFill || (m.Rect.Width > 2 && m.Rect.Height > 2))
            {
                marks.Add(m);
                using (Graphics g = Graphics.FromImage(canvas)) DrawMark(g, m);
            }
            Invalidate();
        }

        private void InvalidateImage(Rectangle r, int margin)
        {
            r.Inflate(margin, margin);
            r.Offset(imageRect.Location);
            Invalidate(r);
        }

        private int SwatchAt(Point p)
        {
            for (int i = 0; i < Colors.Length; i++)
            {
                if (Rectangle.Inflate(SwatchRect(i), S(SwatchGap96) / 2, S(BarPad96)).Contains(p)) return i;
            }
            return -1;
        }

        private void SelectColor(int i)
        {
            colorIndex = i;
            Invalidate(barRect);
        }

        // ---- キーボード ----

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                if (current != null)
                {
                    // 書いている途中なら, それだけをやめる
                    current = null;
                    Capture = false;
                    Invalidate();
                }
                else
                {
                    BeginInvoke((MethodInvoker)Close);
                }
                return true;
            }
            if (current != null) return base.ProcessCmdKey(ref msg, keyData); // 書いている間は, ほかのキーを無視する

            if (keyData == Keys.Enter)
            {
                BeginInvoke((MethodInvoker)Finish);
                return true;
            }
            if (keyData == (Keys.Control | Keys.Z))
            {
                if (marks.Undo()) Redraw();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z))
            {
                if (marks.Redo()) Redraw();
                return true;
            }
            if (keyData == Keys.Tab)
            {
                barHidden = !barHidden;
                Invalidate(barRect);
                return true;
            }
            int n = -1;
            if (keyData >= Keys.D1 && keyData <= Keys.D9) n = keyData - Keys.D1;
            else if (keyData >= Keys.NumPad1 && keyData <= Keys.NumPad9) n = keyData - Keys.NumPad1;
            if (n >= 0)
            {
                if (n < Colors.Length) SelectColor(n);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Redraw()
        {
            RebuildCanvas();
            Invalidate(imageRect);
            if (BarVisible) Invalidate(barRect);
        }

        // ---- 出力 ----

        private bool finishing;

        private void Finish()
        {
            if (finishing || IsDisposed) return;
            finishing = true;
            try
            {
                if (marks.Count == 0)
                {
                    CaptureOutput.CopyToClipboard(original); // 何も書いていなければ, 別名のファイルは作らない
                }
                else
                {
                    using (Bitmap bmp = canvas.Clone(new Rectangle(0, 0, canvas.Width, canvas.Height), PixelFormat.Format32bppArgb))
                    {
                        CaptureOutput.CopyToClipboard(bmp);
                        CaptureOutput.SaveEditedPng(bmp, originalPath);
                    }
                }
            }
            catch (Exception ex)
            {
                // 書き込みが消えないよう, 画面は開いたままにする. お知らせが隠れないよう, いったん最前面をやめる
                TopMost = false;
                Notice.Show("書き込みを保存できませんでした", ex.Message, NoticeKind.Error);
                TopMost = true;
                finishing = false;
                return;
            }
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                if (original != null) original.Dispose();
                if (canvas != null) canvas.Dispose();
            }
        }
    }
}
