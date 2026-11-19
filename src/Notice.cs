using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Wakudori
{
    internal enum NoticeKind { Info, Warning, Error }

    // MessageBoxの代わりのお知らせ. 見出し(明朝), 本文, OKボタンだけの画面.
    internal static class Notice
    {
        private const int MaxBodyLength = 1200;

        public static void Show(string heading, string body, NoticeKind kind)
        {
            if (body != null && body.Length > MaxBodyLength) body = body.Substring(0, MaxBodyLength) + "\r\n…";
            using (NoticeForm f = new NoticeForm(heading, body, kind))
            {
                f.ShowDialog();
            }
        }
    }

    internal sealed class NoticeForm : ThemedForm
    {
        private readonly NoticeKind kind;
        private readonly Rectangle dotRect;

        public NoticeForm(string heading, string body, NoticeKind kind)
        {
            this.kind = kind;
            int pad = S(32);
            int width = S(440);
            int inner = width - pad * 2;

            // 上の小さな行: 種類を示す丸と「Wakudori」
            int dot = S(8);
            Label app = AddText("Wakudori", Theme.Body(S(14)), Theme.TextTertiary, pad + dot + S(8), pad - S(4), inner);
            dotRect = new Rectangle(pad, app.Top + (app.Height - dot) / 2, dot, dot);
            int y = app.Bottom + S(8);

            Label h = AddText(heading, Theme.Heading(S(22)), Theme.Text, pad - S(2), y, inner);
            y = h.Bottom + S(12);

            if (!string.IsNullOrEmpty(body))
            {
                Label b = AddText(body, Theme.Body(S(15)), Theme.TextSecondary, pad, y, inner);
                y = b.Bottom;
            }
            y += S(28);

            ThemedButton ok = new ThemedButton("OK", ButtonKind.Accent, Theme.BodyStrong(S(15)), DeviceDpi / 96f);
            ok.Location = new Point(width - pad - ok.Width, y);
            ok.Click += delegate { Close(); };
            Controls.Add(ok);

            ClientSize = new Size(width, ok.Bottom + pad);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Color c = kind == NoticeKind.Error ? Theme.Danger : kind == NoticeKind.Warning ? Theme.Warning : Theme.Accent;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(c))
            {
                e.Graphics.FillEllipse(b, dotRect);
            }
        }

        // Enter, Esc, スペースで閉じる.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter || keyData == Keys.Escape || keyData == Keys.Space)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
