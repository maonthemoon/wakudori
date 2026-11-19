using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Wakudori
{
    // 範囲に名前を付ける画面. 選択画面でCtrl+Sを押すと開く. 同じ名前があるときは, 上書きする前に確かめる.
    internal sealed class NameForm : ThemedForm
    {
        private const string DefaultMessage = "ファイル名は「名前_日付_時刻.png」になります.";

        private readonly HashSet<string> existing;
        private readonly NameField field;
        private readonly Label messageLabel;
        private readonly ThemedButton okButton;
        private string pendingOverwrite; // 上書きの確認を出した名前. 出していなければnull
        private string result;
        private readonly bool rename;       // trueなら名前を変える画面(上書きはしない)
        private readonly string currentName; // 名前を変えるときの今の名前

        // existingNames: 保存済みの名前. initialName: 最初に入れておく名前(nullでもよい).
        public NameForm(ICollection<string> existingNames, string initialName)
            : this(existingNames, initialName, false)
        {
        }

        // 名前を変える(一覧の右クリックから). currentName: 今の名前. existingNames: ほかの範囲の名前(今の名前は含めない)
        public NameForm(ICollection<string> existingNames, string currentName, bool rename)
        {
            this.rename = rename;
            this.currentName = currentName != null ? currentName : "";
            string initialName = currentName;
            existing = new HashSet<string>(existingNames != null ? existingNames : new List<string>(), StringComparer.Ordinal);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Text = rename ? "Wakudori - 名前を変える" : "Wakudori - 範囲に名前を付ける";

            float scale = DeviceDpi / 96f;
            int pad = S(32);
            int width = S(480);
            int inner = width - pad * 2;

            Label app = AddText("Wakudori", Theme.Body(S(14)), Theme.TextTertiary, pad, pad - S(4), inner);
            ThemedButton close = new ThemedButton("×", ButtonKind.Ghost, Theme.Body(S(18)), scale);
            close.Size = new Size(S(36), S(36));
            close.Location = new Point(width - S(16) - close.Width, S(16));
            close.Click += delegate { Cancel(); };
            Controls.Add(close);

            int y = app.Bottom + S(8);
            Label h = AddText(rename ? "名前を変える" : "範囲に名前を付ける", Theme.Heading(S(24)), Theme.Text, pad - S(2), y, inner);
            y = h.Bottom + S(12);
            Label body = AddText(rename
                ? "「" + this.currentName + "」の名前を変えます. 範囲の位置と大きさは変わりません."
                : "今の枠の位置と大きさを, 名前を付けて保存します. 次からは選択画面の上の一覧から選べます.",
                Theme.Body(S(15)), Theme.TextSecondary, pad, y, inner);
            y = body.Bottom + S(24);

            field = new NameField(scale, Theme.Body(S(16)));
            field.Bounds = new Rectangle(pad, y, inner, S(44));
            field.Box.MaxLength = 60;
            field.Box.Text = initialName != null ? initialName : "";
            field.Box.TextChanged += delegate { OnNameChanged(); };
            Controls.Add(field);
            y = field.Bottom + S(12);

            messageLabel = AddText(DefaultMessage, Theme.Body(S(14)), Theme.TextTertiary, pad, y, inner);
            messageLabel.AutoSize = false;
            messageLabel.Size = new Size(inner, S(44)); // 2行まで. 文の長さで画面の高さが変わらないようにする
            y = messageLabel.Bottom + S(16);

            okButton = new ThemedButton(rename ? "変える" : "保存", ButtonKind.Accent, Theme.BodyStrong(S(15)), scale);
            okButton.Width = Math.Max(okButton.Width, S(96)); // 「上書き」に変えても幅が変わらないようにする
            okButton.Location = new Point(width - pad - okButton.Width, y);
            okButton.Click += delegate { Confirm(); };
            Controls.Add(okButton);

            ThemedButton cancel = new ThemedButton("やめる", ButtonKind.Secondary, Theme.BodyStrong(S(15)), scale);
            cancel.Location = new Point(okButton.Left - S(8) - cancel.Width, y);
            cancel.Click += delegate { Cancel(); };
            Controls.Add(cancel);

            ClientSize = new Size(width, okButton.Bottom + pad);
        }

        // 付けた名前(前後の空白は除く). DialogResultがOKのときだけ使える.
        public string ResultName { get { return result; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            field.Box.Focus();
            field.Box.SelectAll();
        }

        // Enterで決め, Escで閉じる(修飾キーなしのときだけ).
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter)
            {
                Confirm();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                Cancel();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // 文字を変えたら, 上書きの確認を最初からやり直す.
        private void OnNameChanged()
        {
            if (rename)
            {
                ShowMessage(DefaultMessage, false);
                return;
            }
            if (pendingOverwrite == null) return;
            pendingOverwrite = null;
            okButton.Text = "保存";
            okButton.Invalidate();
            ShowMessage(DefaultMessage, false);
        }

        private void Confirm()
        {
            string name = field.Box.Text.Trim();
            if (name.Length == 0)
            {
                ShowMessage("名前を入力してください.", true);
                return;
            }
            if (rename)
            {
                if (string.Equals(name, currentName, StringComparison.Ordinal))
                {
                    Cancel(); // 変わっていないので何もしない
                    return;
                }
                if (existing.Contains(name))
                {
                    ShowMessage("「" + name + "」はほかの範囲で使っています. 別の名前にしてください.", true);
                    return;
                }
            }
            else if (existing.Contains(name) && !string.Equals(pendingOverwrite, name, StringComparison.Ordinal))
            {
                pendingOverwrite = name;
                okButton.Text = "上書き";
                okButton.Invalidate();
                ShowMessage("「" + name + "」はすでにあります. もう一度Enterを押すか「上書き」で上書きします.", true);
                return;
            }
            result = name;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Cancel()
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void ShowMessage(string text, bool error)
        {
            messageLabel.Text = text;
            messageLabel.ForeColor = error ? Theme.Danger : Theme.TextTertiary;
        }
    }

    // 角丸の枠の中に1行の入力欄を置いたもの. 入力中は枠の線が濃くなる.
    internal sealed class NameField : Control
    {
        private readonly float scale;
        private readonly TextBox box;

        public NameField(float scale, Font font)
        {
            this.scale = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;

            box = new TextBox();
            box.BorderStyle = BorderStyle.None;
            box.Font = font;
            box.BackColor = Theme.Surface;
            box.ForeColor = Theme.Text;
            box.GotFocus += delegate { Invalidate(); };
            box.LostFocus += delegate { Invalidate(); };
            Controls.Add(box);
        }

        public TextBox Box { get { return box; } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int padX = (int)Math.Round(14 * scale);
            box.Width = Math.Max(10, Width - padX * 2);
            box.Location = new Point(padX, Math.Max(0, (Height - box.Height) / 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8 * scale))
            using (SolidBrush b = new SolidBrush(Theme.Surface))
            using (Pen p = new Pen(box.Focused ? Theme.Text : Theme.Border))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
        }
    }
}
