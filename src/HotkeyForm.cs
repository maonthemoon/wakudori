using System;
using System.Drawing;
using System.Windows.Forms;

namespace Wakudori
{
    // ホットキーを登録する画面(B2). 使うキーを押してもらい, Enterか「登録」で登録する.
    internal sealed class HotkeyForm : Form
    {
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_KEYMENU = 0xF100;

        private readonly Func<HotkeySpec, string> tryRegister;
        private readonly Label keyLabel;
        private readonly Label messageLabel;
        private readonly Button okButton;
        private bool hasCandidate;
        private HotkeySpec candidate;

        // tryRegister: 実際に登録してみる. 成功ならnull, 失敗ならその理由を返す.
        public HotkeyForm(string current, bool firstRun, Func<HotkeySpec, string> tryRegister)
        {
            this.tryRegister = tryRegister;

            float scale = DeviceDpi / 96f;
            Func<int, int> s = delegate(int v) { return (int)Math.Round(v * scale); };

            Text = "Wakudori - ホットキーの登録";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = true;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(s(16));

            TableLayoutPanel table = new TableLayoutPanel();
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.ColumnCount = 1;
            table.Dock = DockStyle.Fill;
            Controls.Add(table);

            int width = s(420);
            string intro = firstRun
                ? "選択画面を開くホットキーを登録します."
                : "選択画面を開くホットキーを登録し直します.";
            table.Controls.Add(MakeLabel(intro, width, s(0)));
            table.Controls.Add(MakeLabel(
                "使うキーを押してください(例: Ctrl+Shift+S, F12, PrintScreen).\r\n"
                + "Enterで登録, Escでやめます.\r\n"
                + "押しても何も表示されないキーは, 他のアプリかWindowsが使っています.", width, s(8)));
            if (!string.IsNullOrEmpty(current))
            {
                table.Controls.Add(MakeLabel("今のキー: " + current, width, s(8)));
            }

            keyLabel = MakeLabel("(キーを押してください)", width, s(12));
            keyLabel.Font = new Font(Font.FontFamily, 16f, FontStyle.Bold);
            keyLabel.TextAlign = ContentAlignment.MiddleCenter;
            keyLabel.ForeColor = SystemColors.GrayText;
            table.Controls.Add(keyLabel);

            messageLabel = MakeLabel(" ", width, s(8));
            table.Controls.Add(messageLabel);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.Dock = DockStyle.Fill;
            buttons.Margin = new Padding(0, s(12), 0, 0);

            Button cancelButton = MakeButton("やめる", s);
            cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            okButton = MakeButton("登録", s);
            okButton.Enabled = false;
            okButton.Click += delegate { Confirm(); };
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(okButton);
            table.Controls.Add(buttons);
        }

        public HotkeySpec Result { get { return candidate; } }

        private static Label MakeLabel(string text, int width, int top)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.MaximumSize = new Size(width, 0);
            l.MinimumSize = new Size(width, 0);
            l.Margin = new Padding(0, top, 0, 0);
            return l;
        }

        private static Button MakeButton(string text, Func<int, int> s)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.MinimumSize = new Size(s(88), s(28));
            b.TabStop = false; // キーはすべてホットキーの入力に使う
            b.Margin = new Padding(s(8), 0, 0, 0);
            return b;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

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
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            if (mods == HotkeyModifiers.None && key == Keys.Enter)
            {
                Confirm();
                return;
            }

            HotkeySpec spec = new HotkeySpec(mods, vk);
            keyLabel.Text = HotkeyText.Format(spec);
            string reason = HotkeyText.Validate(spec);
            if (reason != null)
            {
                hasCandidate = false;
                keyLabel.ForeColor = SystemColors.GrayText;
                ShowMessage(reason, true);
            }
            else
            {
                hasCandidate = true;
                candidate = spec;
                keyLabel.ForeColor = SystemColors.ControlText;
                ShowMessage("Enterか「登録」で登録します.", false);
            }
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
                keyLabel.ForeColor = SystemColors.GrayText;
                ShowMessage(error, true);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ShowMessage(string text, bool error)
        {
            messageLabel.Text = text;
            messageLabel.ForeColor = error ? Color.FromArgb(0xB0, 0x30, 0x20) : SystemColors.ControlText;
        }
    }
}
