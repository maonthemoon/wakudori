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

        // 確かめる. OKならtrue. owner: 親のウィンドウ(nullならなし). okText: 実行ボタンの文字(例: "削除")
        public static bool Confirm(IWin32Window owner, string heading, string body, string okText)
        {
            using (ConfirmForm f = new ConfirmForm(heading, body, okText, owner != null))
            {
                DialogResult r = owner != null ? f.ShowDialog(owner) : f.ShowDialog();
                return r == DialogResult.OK;
            }
        }
    }

    internal sealed class NoticeForm : ThemedForm
    {
        public NoticeForm(string heading, string body, NoticeKind kind)
        {
            int pad = S(32);
            int width = S(440);
            int inner = width - pad * 2;

            // 上の小さな行: 「Wakudori」. 種類(案内, 注意, エラー)は見出しの文で分かるので, 印は付けない
            Label app = AddText("Wakudori", Theme.Body(S(14)), Theme.TextTertiary, pad, pad - S(4), inner);
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

    // はい/いいえを選ぶ画面. Enterで実行, Escで取りやめ.
    internal sealed class ConfirmForm : ThemedForm
    {
        public ConfirmForm(string heading, string body, string okText, bool hasOwner)
        {
            if (hasOwner)
            {
                StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false;
            }
            float scale = DeviceDpi / 96f;
            int pad = S(32);
            int width = S(440);
            int inner = width - pad * 2;

            Label app = AddText("Wakudori", Theme.Body(S(14)), Theme.TextTertiary, pad, pad - S(4), inner);
            ThemedButton close = new ThemedButton("×", ButtonKind.Ghost, Theme.Body(S(18)), scale);
            close.Size = new Size(S(36), S(36));
            close.Location = new Point(width - S(16) - close.Width, S(16));
            close.Click += delegate { Cancel(); };
            Controls.Add(close);

            int y = app.Bottom + S(8);
            Label h = AddText(heading, Theme.Heading(S(22)), Theme.Text, pad - S(2), y, inner);
            y = h.Bottom + S(12);

            if (!string.IsNullOrEmpty(body))
            {
                Label b = AddText(body, Theme.Body(S(15)), Theme.TextSecondary, pad, y, inner);
                y = b.Bottom;
            }
            y += S(28);

            ThemedButton ok = new ThemedButton(okText, ButtonKind.Accent, Theme.BodyStrong(S(15)), scale);
            ok.Width = Math.Max(ok.Width, S(96));
            ok.Location = new Point(width - pad - ok.Width, y);
            ok.Click += delegate { Accept(); };
            Controls.Add(ok);

            ThemedButton cancel = new ThemedButton("やめる", ButtonKind.Secondary, Theme.BodyStrong(S(15)), scale);
            cancel.Location = new Point(ok.Left - S(8) - cancel.Width, y);
            cancel.Click += delegate { Cancel(); };
            Controls.Add(cancel);

            ClientSize = new Size(width, ok.Bottom + pad);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter)
            {
                Accept();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                Cancel();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Accept()
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Cancel()
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
