using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Wakudori
{
    // 見張り中に, 撮る範囲の外側の線と札を表示する.
    internal sealed class WatchFrameForm : Form
    {
        private const int FrameGapPx = 1;
        private const int LineWidth96 = 2;
        private const int FlashLineWidth96 = 5;
        private const int FlashMs = 600;
        private const int BadgeGap96 = 8;
        private const int BadgeHeight96 = 24;
        private const int BadgePadding96 = 10;
        private const int DotSize96 = 8;
        private const int DotGap96 = 6;
        private const TextFormatFlags TextFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        private readonly Rectangle region;
        private readonly Screen monitor;
        private readonly float scale;
        private readonly Font badgeFont;
        private readonly Timer flashTimer;
        private bool flashing;
        private string badgeText;
        private Size badgeSize;
        private Rectangle frameBounds;
        private Rectangle badgeBounds;

        public event EventHandler FlashEnded;

        public WatchFrameForm(Rectangle region)
        {
            this.region = region;
            monitor = Screen.FromRectangle(region);
            scale = Program.GetMonitorDpi(monitor.Bounds) / 96f;

            Text = "Wakudori - 見張り中";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Magenta;
            TransparencyKey = BackColor;
            DoubleBuffered = true;

            badgeFont = Theme.BodyStrong(12 * scale);
            flashTimer = new Timer { Interval = FlashMs };
            flashTimer.Tick += OnFlashEnded;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT
                    | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // 札が範囲の内側に入る場合に備える. 使えない環境でも見張りは続ける.
            try { NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE); }
            catch (EntryPointNotFoundException) { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdateLayout();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_DPICHANGED)
            {
                m.Result = IntPtr.Zero; // 枠の位置と大きさは物理ピクセルで固定する.
                return;
            }
            base.WndProc(ref m);
        }

        public void UpdateStatus(int shotCount, TimeSpan elapsed, bool captured)
        {
            badgeText = "見張り中 · " + shotCount.ToString(CultureInfo.InvariantCulture) + "枚 · " + WatchTime.FormatElapsed(elapsed);
            Size textSize = TextRenderer.MeasureText(badgeText, badgeFont, Size.Empty, TextFlags);
            int extraWidth = Scale(BadgePadding96) * 2 + Scale(DotSize96) + Scale(DotGap96);
            badgeSize = new Size(textSize.Width + extraWidth, Scale(BadgeHeight96));

            if (captured)
            {
                flashing = true;
                flashTimer.Stop();
                flashTimer.Start();
            }
            UpdateLayout();
        }

        private void OnFlashEnded(object sender, EventArgs e)
        {
            flashTimer.Stop();
            flashing = false;
            UpdateLayout();
            EventHandler handler = FlashEnded;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private int Scale(int px96)
        {
            return Math.Max(1, (int)Math.Round(px96 * scale));
        }

        private int LineWidth { get { return Scale(flashing ? FlashLineWidth96 : LineWidth96); } }

        private void UpdateLayout()
        {
            Rectangle outer = Rectangle.Inflate(region, FrameGapPx + LineWidth, FrameGapPx + LineWidth);
            Rectangle badge = PlaceBadge(outer);
            Rectangle window = Rectangle.Union(outer, badge);
            outer.Offset(-window.X, -window.Y);
            badge.Offset(-window.X, -window.Y);
            frameBounds = outer;
            badgeBounds = badge;
            Bounds = window;
            Invalidate();
        }

        private Rectangle PlaceBadge(Rectangle outer)
        {
            Rectangle work = monitor.WorkingArea;
            int gap = Scale(BadgeGap96);
            Rectangle badge = new Rectangle(outer.Right - badgeSize.Width, outer.Top - gap - badgeSize.Height,
                badgeSize.Width, badgeSize.Height);
            if (work.Contains(badge)) return badge;

            badge.Y = outer.Bottom + gap;
            if (work.Contains(badge)) return badge;

            // 狭い範囲でも札がモニターの外にはみ出さないようにする.
            Rectangle screen = monitor.Bounds;
            badge.X = Math.Max(screen.Left, region.Right - badge.Width);
            badge.Y = Math.Min(region.Top, screen.Bottom - badge.Height);
            return badge;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.PageUnit = GraphicsUnit.Pixel;
            Rectangle r = frameBounds;
            int w = LineWidth;
            using (SolidBrush line = new SolidBrush(Theme.Accent))
            {
                g.FillRectangle(line, r.Left, r.Top, r.Width, w);
                g.FillRectangle(line, r.Left, r.Bottom - w, r.Width, w);
                g.FillRectangle(line, r.Left, r.Top + w, w, r.Height - 2 * w);
                g.FillRectangle(line, r.Right - w, r.Top + w, w, r.Height - 2 * w);
            }
            DrawBadge(g);
        }

        private void DrawBadge(Graphics g)
        {
            Rectangle r = badgeBounds;
            // 透過色との境界に中間色が出ないよう, 札の外周はアンチエイリアスをかけない.
            using (GraphicsPath outline = Theme.RoundedRect(r, r.Height / 2f))
            using (SolidBrush background = new SolidBrush(flashing ? Theme.Accent : Theme.Bg))
                g.FillPath(background, outline);

            int padding = Scale(BadgePadding96);
            int dot = Scale(DotSize96);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush indicator = new SolidBrush(Theme.Danger))
                g.FillEllipse(indicator, r.X + padding, r.Y + (r.Height - dot) / 2, dot, dot);

            int textLeft = r.Left + padding + dot + Scale(DotGap96);
            Rectangle textBounds = Rectangle.FromLTRB(textLeft, r.Top, r.Right - padding, r.Bottom);
            TextRenderer.DrawText(g, badgeText, badgeFont, textBounds, flashing ? Color.White : Theme.Text,
                TextFlags | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                flashTimer.Dispose();
                badgeFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
