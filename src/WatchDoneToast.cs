using System;
using System.Drawing;
using System.Windows.Forms;

namespace Wakudori
{
    // 見張りを止めたときに, 主モニターの右下に出す知らせ.
    internal sealed class WatchDoneToast : Form
    {
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;

        private readonly float scale;
        private readonly string folder;
        private readonly Font bodyFont;
        private readonly Font headingFont;
        private readonly Font hintFont;
        private readonly Timer timer;

        public WatchDoneToast(WatchFinishedEventArgs result)
        {
            Screen primary = Screen.PrimaryScreen;
            scale = Program.GetMonitorDpi(primary.Bounds) / 96f;
            folder = result.ShotCount > 0 ? result.Folder : null;
            bodyFont = Theme.Body(12 * scale);
            headingFont = new Font(bodyFont.FontFamily, 15 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            hintFont = Theme.Body(11 * scale);

            Text = "Wakudori - 見張りを終えました";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            if (folder != null) Cursor = Cursors.Hand;
            MouseClick += OnToastClick;

            int width = Scale(330);
            int pad = Scale(16);
            int inner = width - pad * 2;
            int y = AddLine("Wakudori", bodyFont, Theme.TextTertiary, pad, Scale(14), inner) + Scale(2);
            y = AddLine(WatchDoneText.Heading(result.Reason), headingFont, Theme.Text, pad, y, inner) + Scale(4);
            y = AddLine(WatchDoneText.Body(result), bodyFont, Theme.TextSecondary, pad, y, inner) + Scale(6);
            y = AddLine(WatchDoneText.Hint(result.ShotCount), hintFont, Theme.AccentDeep, pad, y, inner);
            ClientSize = new Size(width, y + Scale(14));

            Rectangle work = primary.WorkingArea;
            Location = new Point(work.Right - Width - pad, work.Bottom - Height - pad);

            timer = new Timer { Interval = 8000 };
            timer.Tick += delegate { Close(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = new IntPtr(MA_NOACTIVATE);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen border = new Pen(Theme.Border))
            {
                e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
        }

        private int AddLine(string text, Font font, Color color, int x, int y, int width)
        {
            Label line = new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                AutoSize = true,
                MaximumSize = new Size(width, 0),
                Location = new Point(x, y),
                UseMnemonic = false,
                UseCompatibleTextRendering = true
            };
            line.MouseClick += OnToastClick;
            Controls.Add(line);
            return line.Bottom;
        }

        private void OnToastClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Close();
            if (folder != null) TrayApp.OpenFolder(folder);
        }

        private int Scale(int value)
        {
            return (int)Math.Round(value * scale);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                timer.Dispose();
                hintFont.Dispose();
                headingFont.Dispose();
                bodyFont.Dispose();
            }
        }
    }
}
