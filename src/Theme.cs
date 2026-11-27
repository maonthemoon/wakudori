using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Wakudori
{
    // 画面の見た目(Anthropic風のライトテーマ). アイボリーの背景, ほぼ黒の文字, クレイ色は1画面1〜2か所だけ.
    // 領域は余白と背景色の段差で分け, 影やグラデーションは使わない.
    internal static class Theme
    {
        public static readonly Color Bg = Hex(0xFAF9F5);           // 背景
        public static readonly Color BgSubtle = Hex(0xF0EEE6);     // 一段下げた面
        public static readonly Color BgMuted = Hex(0xE8E6DC);      // ホバー
        public static readonly Color Surface = Hex(0xFFFFFF);      // カード, キー
        public static readonly Color Text = Hex(0x141413);
        public static readonly Color TextSecondary = Hex(0x3D3D3A);
        public static readonly Color TextTertiary = Hex(0x5E5D59);
        public static readonly Color Border = Hex(0xD1CFC5);
        public static readonly Color BorderFaint = Hex(0xE8E6DC);
        public static readonly Color Accent = Hex(0xD97757);            // 装飾
        public static readonly Color AccentInteractive = Hex(0xB85A39); // 主ボタン(白文字)
        public static readonly Color AccentDeep = Hex(0xA9472A);        // 主ボタンのホバー, アクセントの文字
        public static readonly Color Warning = Hex(0xC08532);
        public static readonly Color Danger = Hex(0xBF4D43);
        public static readonly Color PrimaryHover = Hex(0x3D3D3A);

        private static FontFamily headingFamily;
        private static FontStyle headingStyle;
        private static FontFamily bodyFamily;
        private static FontFamily bodyStrongFamily;
        private static FontStyle bodyStrongStyle;

        private static Color Hex(int rgb)
        {
            return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }

        // 見出し: 明朝の太字. Noto Serif JPがなければWindows標準の游明朝.
        public static Font Heading(float px)
        {
            if (headingFamily == null)
            {
                if (!Pick(new string[] { "Noto Serif JP" }, FontStyle.Bold, out headingFamily, out headingStyle)
                    && !Pick(new string[] { "Yu Mincho Demibold", "游明朝 Demibold" }, FontStyle.Regular, out headingFamily, out headingStyle)
                    && !Pick(new string[] { "Yu Mincho", "游明朝", "MS Mincho", "ＭＳ 明朝" }, FontStyle.Bold, out headingFamily, out headingStyle))
                {
                    headingFamily = BodyFamily;
                    headingStyle = FontStyle.Bold;
                }
            }
            return new Font(headingFamily, px, headingStyle, GraphicsUnit.Pixel);
        }

        // 本文・UI: Noto Sans JPがなければYu Gothic UI, Meiryo UI.
        public static Font Body(float px)
        {
            return new Font(BodyFamily, px, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        // ボタンやキーの文字: 少し太いゴシック.
        public static Font BodyStrong(float px)
        {
            if (bodyStrongFamily == null)
            {
                if (!Pick(new string[] { "Noto Sans JP Medium", "Yu Gothic UI Semibold" }, FontStyle.Regular, out bodyStrongFamily, out bodyStrongStyle))
                {
                    bodyStrongFamily = BodyFamily;
                    bodyStrongStyle = FontStyle.Bold;
                }
            }
            return new Font(bodyStrongFamily, px, bodyStrongStyle, GraphicsUnit.Pixel);
        }

        private static FontFamily BodyFamily
        {
            get
            {
                if (bodyFamily == null)
                {
                    FontStyle st;
                    if (!Pick(new string[] { "Noto Sans JP", "Yu Gothic UI", "Meiryo UI", "Meiryo" }, FontStyle.Regular, out bodyFamily, out st))
                    {
                        bodyFamily = SystemFonts.MessageBoxFont.FontFamily;
                    }
                }
                return bodyFamily;
            }
        }

        private static bool Pick(string[] names, FontStyle style, out FontFamily family, out FontStyle chosen)
        {
            foreach (string n in names)
            {
                try
                {
                    FontFamily f = new FontFamily(n);
                    if (f.IsStyleAvailable(style))
                    {
                        family = f;
                        chosen = style;
                        return true;
                    }
                    f.Dispose();
                }
                catch (ArgumentException)
                {
                }
            }
            family = null;
            chosen = FontStyle.Regular;
            return false;
        }

        public static GraphicsPath RoundedRect(RectangleF rc, float r)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(r * 2, Math.Min(rc.Width, rc.Height));
            if (d <= 0)
            {
                p.AddRectangle(rc);
                return p;
            }
            p.AddArc(rc.Left, rc.Top, d, d, 180, 90);
            p.AddArc(rc.Right - d, rc.Top, d, d, 270, 90);
            p.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0, 90);
            p.AddArc(rc.Left, rc.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // アプリのアイコン(案G「枠とつまみ」): クレイ色の角丸の四角に, アイボリーの細い枠と右の辺の太いつまみ.
        // 座標は64×64の格子. exeのアイコン(app.ico)はtools/make_icon.pyで同じ絵を描いている. 変えるときは両方直す.
        // 返すハンドルは, 使い終わったらDestroyIconで消す.
        public static IntPtr CreateAppIconHandle(int size)
        {
            if (size < 16) size = 16;
            float k = size / 64f;
            using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.Clear(Color.Transparent);
                    using (GraphicsPath bg = RoundedRect(new RectangleF(2 * k, 2 * k, 60 * k, 60 * k), 14 * k))
                    using (SolidBrush clay = new SolidBrush(Accent))
                    {
                        g.FillPath(clay, bg);
                    }
                    // 細い枠. 小さいアイコンでも1px未満にしない
                    float fw = Math.Max(1f, 3 * k);
                    using (GraphicsPath frame = RoundedRect(new RectangleF(14 * k + fw / 2, 16 * k + fw / 2, 32 * k - fw, 32 * k - fw), 2 * k))
                    using (Pen pen = new Pen(Bg, fw))
                    {
                        g.DrawPath(pen, frame);
                    }
                    // 右の辺のつまみ. 小さいアイコンでも2px未満にしない
                    float kw = Math.Max(2f, 6 * k);
                    using (Pen knob = new Pen(Bg, kw))
                    {
                        knob.StartCap = LineCap.Round;
                        knob.EndCap = LineCap.Round;
                        g.DrawLine(knob, 53 * k, 25 * k, 53 * k, 39 * k);
                    }
                }
                return bmp.GetHicon();
            }
        }

        // トレイのメニューの色.
        public sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Bg; } }
            public override Color ImageMarginGradientBegin { get { return Bg; } }
            public override Color ImageMarginGradientMiddle { get { return Bg; } }
            public override Color ImageMarginGradientEnd { get { return Bg; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return BgMuted; } }
            public override Color MenuItemSelected { get { return BgMuted; } }
            public override Color MenuItemSelectedGradientBegin { get { return BgMuted; } }
            public override Color MenuItemSelectedGradientEnd { get { return BgMuted; } }
            public override Color MenuItemPressedGradientBegin { get { return BgMuted; } }
            public override Color MenuItemPressedGradientEnd { get { return BgMuted; } }
            public override Color SeparatorDark { get { return BorderFaint; } }
            public override Color SeparatorLight { get { return Bg; } }
        }
    }

    // 枠なしの画面の土台. 背景はアイボリー, ふちは細い線. Windows 11では角を丸くする. どこをつかんでも動かせる.
    internal class ThemedForm : Form
    {
        private const int CS_DROPSHADOW = 0x00020000;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private static IntPtr iconHandle;
        private readonly float scale;

        public ThemedForm()
        {
            // 開く場所(カーソルのあるモニター)の拡大率で大きさを決める. hikidashiの設定画面と同じ.
            // DeviceDpiはWindows全体の値なので, 拡大率の違うモニターで開くと小さくなる
            scale = Program.GetMonitorDpi(new Rectangle(Cursor.Position, new Size(1, 1))) / 96f;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body(S(15));
            TopMost = true;
            ShowInTaskbar = true;
            DoubleBuffered = true;
            Text = "Wakudori";
            if (iconHandle == IntPtr.Zero) iconHandle = Theme.CreateAppIconHandle(32); // プロセスの終了まで使う
            Icon = Icon.FromHandle(iconHandle);
            MouseDown += OnDragMouseDown;
        }

        // 96 DPIのときを1とした, この画面の倍率
        protected float UiScale { get { return scale; } }

        // 96 DPIのときの長さを, この画面のDPIでの長さにする.
        protected int S(float v)
        {
            return (int)Math.Round(v * scale);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int pref = DWMWCP_ROUND;
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)); // Windows 10では何もしない
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(Theme.Border))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
        }

        // ラベルなどをつかんでも画面を動かせるようにする.
        protected void EnableDrag(Control c)
        {
            c.MouseDown += OnDragMouseDown;
        }

        private void OnDragMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
        }

        // 文字のラベルを置いて, その下端を返す.
        protected Label AddText(string text, Font font, Color color, int x, int y, int width)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            l.AutoSize = true;
            l.MaximumSize = new Size(width, 0);
            l.Location = new Point(x, y);
            l.UseMnemonic = false;
            Controls.Add(l);
            EnableDrag(l);
            return l;
        }
    }

    internal enum ButtonKind { Accent, Primary, Secondary, Ghost }

    // 角丸のボタン. Accent(クレイ, 1画面1つ), Primary(黒), Secondary(線), Ghost(地なし).
    internal sealed class ThemedButton : Control
    {
        private readonly ButtonKind kind;
        private readonly float scale;
        private bool hover;
        private bool pressed;

        public ThemedButton(string text, ButtonKind kind, Font font, float scale)
        {
            this.kind = kind;
            this.scale = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Text = text;
            Font = font;
            Cursor = Cursors.Hand;
            Size textSize = TextRenderer.MeasureText(text, font);
            int pad = (int)Math.Round((kind == ButtonKind.Ghost ? 10 : 20) * scale);
            Size = new Size(Math.Max(textSize.Width + pad * 2, (int)Math.Round(88 * scale)), (int)Math.Round(40 * scale));
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color parentBg = Parent != null ? Parent.BackColor : Theme.Bg;
            g.Clear(parentBg);

            Color bg, fg, border;
            bool active = hover || pressed;
            switch (kind)
            {
                case ButtonKind.Accent:
                    bg = active ? Theme.AccentDeep : Theme.AccentInteractive; fg = Color.White; border = bg; break;
                case ButtonKind.Primary:
                    bg = active ? Theme.PrimaryHover : Theme.Text; fg = Theme.Bg; border = bg; break;
                case ButtonKind.Secondary:
                    bg = active ? Theme.BgMuted : parentBg; fg = Theme.Text; border = Theme.Border; break;
                default:
                    bg = active ? Theme.BgMuted : parentBg; fg = Theme.TextTertiary; border = bg; break;
            }
            if (!Enabled)
            {
                bg = Theme.BgMuted; fg = Theme.TextTertiary; border = bg;
                if (kind == ButtonKind.Ghost || kind == ButtonKind.Secondary) bg = parentBg;
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF rc = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Theme.RoundedRect(rc, 8 * scale))
            using (SolidBrush b = new SolidBrush(bg))
            using (Pen p = new Pen(border))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg, bg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }
}
