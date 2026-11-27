using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Wakudori
{
    // ホットキーを登録する画面(B2). 使うキーを押してもらい, Enterか「登録」で登録する.
    internal sealed class HotkeyForm : ThemedForm
    {
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_KEYMENU = 0xF100;
        private const string DefaultMessage = "例: Ctrl+Shift+S, F12, PrintScreen";

        private readonly Func<HotkeySpec, string> tryRegister;
        private readonly KeycapView keys;
        private readonly Label messageLabel;
        private readonly ThemedButton okButton;
        private bool hasCandidate;
        private HotkeySpec candidate;

        // tryRegister: 実際に登録してみる. 成功ならnull, 失敗ならその理由を返す.
        public HotkeyForm(string current, bool firstRun, Func<HotkeySpec, string> tryRegister)
        {
            this.tryRegister = tryRegister;
            Text = "Wakudori - ホットキーの登録";
            KeyPreview = true;

            float scale = UiScale;
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
            Label h = AddText(firstRun ? "ホットキーを登録する" : "ホットキーを登録し直す", Theme.Heading(S(24)), Theme.Text, pad - S(2), y, inner);
            y = h.Bottom + S(12);
            Label body = AddText("選択画面を開くキーを押してください. 押しても表示されないキーは, 他のアプリかWindowsが使っています.",
                Theme.Body(S(15)), Theme.TextSecondary, pad, y, inner);
            y = body.Bottom + S(24);

            keys = new KeycapView(scale);
            keys.Bounds = new Rectangle(pad, y, inner, S(112));
            Controls.Add(keys);
            EnableDrag(keys);
            y = keys.Bottom + S(12);

            messageLabel = AddText(DefaultMessage, Theme.Body(S(14)), Theme.TextTertiary, pad, y, inner);
            messageLabel.AutoSize = false;
            messageLabel.Size = new Size(inner, S(64)); // 3行まで. 文の長さで画面の高さが変わらないようにする
            y = messageLabel.Bottom + S(16);

            okButton = new ThemedButton("登録", ButtonKind.Accent, Theme.BodyStrong(S(15)), scale);
            okButton.Location = new Point(width - pad - okButton.Width, y);
            okButton.Enabled = false;
            okButton.Click += delegate { Confirm(); };
            Controls.Add(okButton);

            ThemedButton cancel = new ThemedButton("やめる", ButtonKind.Secondary, Theme.BodyStrong(S(15)), scale);
            cancel.Location = new Point(okButton.Left - S(8) - cancel.Width, y);
            cancel.Click += delegate { Cancel(); };
            Controls.Add(cancel);

            if (!string.IsNullOrEmpty(current))
            {
                Label cur = AddText("今のキー: " + current, Theme.Body(S(14)), Theme.TextTertiary, pad, y, cancel.Left - pad - S(8));
                cur.Top = y + (okButton.Height - cur.Height) / 2;
            }

            ClientSize = new Size(width, okButton.Bottom + pad);
        }

        public HotkeySpec Result { get { return candidate; } }

        // キーはここですべて受け取る(Alt, F10, Tab, 矢印キーなども).
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (msg.Msg == NativeMethods.WM_KEYDOWN || msg.Msg == NativeMethods.WM_SYSKEYDOWN)
            {
                OnKeyPressed(keyData & Keys.KeyCode, keyData & Keys.Modifiers);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // PrintScreenはKeyDownが来ないので, KeyUpで受ける.
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.PrintScreen)
            {
                OnKeyPressed(Keys.PrintScreen, e.Modifiers);
                e.Handled = true;
                return;
            }
            base.OnKeyUp(e);
        }

        // Altを離したときなどにウィンドウのメニューへ移らないようにする.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt64() & 0xFFF0) == SC_KEYMENU) return;
            base.WndProc(ref m);
        }

        private void OnKeyPressed(Keys key, Keys modifierKeys)
        {
            bool win = NativeMethods.GetKeyState(NativeMethods.VK_LWIN) < 0 || NativeMethods.GetKeyState(NativeMethods.VK_RWIN) < 0;
            HotkeyModifiers mods = HotkeyModifiers.None;
            if ((modifierKeys & Keys.Control) != 0) mods |= HotkeyModifiers.Ctrl;
            if ((modifierKeys & Keys.Alt) != 0) mods |= HotkeyModifiers.Alt;
            if ((modifierKeys & Keys.Shift) != 0) mods |= HotkeyModifiers.Shift;
            if (win) mods |= HotkeyModifiers.Win;

            int vk = (int)key;
            if (HotkeyText.IsModifierKey(vk)) return; // 修飾キーだけでは決めない

            if (mods == HotkeyModifiers.None && key == Keys.Escape)
            {
                Cancel();
                return;
            }
            if (mods == HotkeyModifiers.None && key == Keys.Enter)
            {
                Confirm();
                return;
            }

            HotkeySpec spec = new HotkeySpec(mods, vk);
            string reason = HotkeyText.Validate(spec);
            hasCandidate = reason == null;
            if (hasCandidate) candidate = spec;
            keys.SetKeys(HotkeyText.Format(spec).Split('+'), hasCandidate);
            if (reason != null) ShowMessage(reason, true);
            else ShowMessage("Enterか「登録」で登録します.", false);
            okButton.Enabled = hasCandidate;
        }

        private void Confirm()
        {
            if (!hasCandidate)
            {
                ShowMessage("先に, 使うキーを押してください.", true);
                return;
            }
            string error = tryRegister(candidate);
            if (error != null)
            {
                hasCandidate = false;
                okButton.Enabled = false;
                keys.SetKeys(keys.Parts, false);
                ShowMessage(error, true);
                return;
            }
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

    // 押したキーを, キーボードのキーの形で並べて見せる.
    internal sealed class KeycapView : Control
    {
        private readonly float scale;
        private readonly Font capFont;
        private readonly Font plusFont;
        private readonly Font placeholderFont;
        private string[] parts;
        private bool valid;

        public KeycapView(float scale)
        {
            this.scale = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            capFont = Theme.BodyStrong(S(18));
            plusFont = Theme.Body(S(16));
            placeholderFont = Theme.Body(S(15));
        }

        public string[] Parts { get { return parts; } }

        public void SetKeys(string[] keys, bool isValid)
        {
            parts = keys;
            valid = isValid;
            Invalidate();
        }

        private int S(float v)
        {
            return (int)Math.Round(v * scale);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color parentBg = Parent != null ? Parent.BackColor : Theme.Bg;
            g.Clear(parentBg);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath panel = Theme.RoundedRect(new RectangleF(0, 0, Width - 1, Height - 1), 12 * scale))
            using (SolidBrush b = new SolidBrush(Theme.BgSubtle))
            {
                g.FillPath(b, panel);
            }

            if (parts == null || parts.Length == 0)
            {
                TextRenderer.DrawText(g, "キーを押してください", placeholderFont, ClientRectangle, Theme.TextTertiary, Theme.BgSubtle,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            int capH = S(48), capPadX = S(16), capMinW = S(48), gap = S(10), lift = Math.Max(2, S(3));
            Size plusSize = TextRenderer.MeasureText(g, "+", plusFont, Size.Empty, TextFormatFlags.NoPadding);
            int[] widths = new int[parts.Length];
            int total = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                Size ts = TextRenderer.MeasureText(g, parts[i], capFont, Size.Empty, TextFormatFlags.NoPadding);
                widths[i] = Math.Max(capMinW, ts.Width + capPadX * 2);
                total += widths[i];
                if (i > 0) total += gap * 2 + plusSize.Width;
            }

            int x = (Width - total) / 2;
            int top = (Height - capH - lift) / 2;
            Color text = valid ? Theme.Text : Theme.TextTertiary;
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    x += gap;
                    Rectangle pr = new Rectangle(x, top, plusSize.Width, capH);
                    TextRenderer.DrawText(g, "+", plusFont, pr, Theme.TextTertiary, Theme.BgSubtle,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    x += plusSize.Width + gap;
                }

                // 下側を少し厚くして, キーボードのキーに見せる(影は使わない).
                RectangleF baseRc = new RectangleF(x + 0.5f, top + lift + 0.5f, widths[i] - 1, capH - 1);
                RectangleF capRc = new RectangleF(x + 0.5f, top + 0.5f, widths[i] - 1, capH - 1);
                float r = 8 * scale;
                using (GraphicsPath basePath = Theme.RoundedRect(baseRc, r))
                using (GraphicsPath capPath = Theme.RoundedRect(capRc, r))
                using (SolidBrush baseBrush = new SolidBrush(Theme.Border))
                using (SolidBrush capBrush = new SolidBrush(Theme.Surface))
                using (Pen border = new Pen(Theme.Border))
                {
                    g.FillPath(baseBrush, basePath);
                    g.FillPath(capBrush, capPath);
                    g.DrawPath(border, capPath);
                }
                TextRenderer.DrawText(g, parts[i], capFont, new Rectangle(x, top, widths[i], capH), text, Theme.Surface,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                x += widths[i];
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                capFont.Dispose();
                plusFont.Dispose();
                placeholderFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
