using System;
using System.Drawing;
using System.Windows.Forms;

namespace Wakudori
{
    // 一覧の名前をダブルクリックしたときの入力欄(その場で名前を変える).
    // 選択画面はレイヤードウィンドウで入力欄を置けないので, 枠なしの小さな画面に入力欄だけを入れ,
    // 選択画面が描くチップの, 名前の文字の場所にぴったり重ねる. チップの丸い形やふちは選択画面が描く.
    // 文字の長さが変わったらTextWidthChangedを出す. 選択画面はチップを伸び縮みさせ, PlaceAtで置き直す.
    // Enterで決める, Escでやめる. ほかの場所をクリックしたときも決める(名前が使えなければやめる).
    internal sealed class InlineNameEditor : Form
    {
        private readonly TextBox box;
        private readonly Func<string, string> apply;     // 名前を変える. 成功ならnull, だめならその理由
        private readonly Action<string> showError;       // 理由を出す(nullで消す)
        private readonly int minWidth;
        private bool done;

        public event EventHandler TextWidthChanged;

        // fontPx: チップの文字の大きさ(ピクセル). scale: そのモニターの拡大率(96 DPIで1)
        public InlineNameEditor(string name, float fontPx, float scale, Func<string, string> apply, Action<string> showError)
        {
            this.apply = apply;
            this.showError = showError;
            minWidth = (int)Math.Round(48 * scale);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            Text = "Wakudori - 名前を変える";

            box = new TextBox();
            box.BorderStyle = BorderStyle.None;
            box.Font = Theme.Body(fontPx);
            box.BackColor = Theme.Surface;
            box.ForeColor = Theme.Text;
            box.MaxLength = 60;
            box.Text = name ?? "";
            box.Dock = DockStyle.Fill;
            box.TextChanged += delegate
            {
                if (showError != null) showError(null);
                EventHandler h = TextWidthChanged;
                if (h != null) h(this, EventArgs.Empty);
            };
            Controls.Add(box);
        }

        // 入力した文字の幅(少し余裕を足す). チップの幅はこれに合わせる
        public int TextWidth
        {
            get
            {
                Size s = TextRenderer.MeasureText(box.Text + " ", box.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl);
                return Math.Max(minWidth, s.Width);
            }
        }

        // 名前の文字の場所(画面の座標)に置く. 高さは入力欄に合わせ, 上下の中央にそろえる
        public void PlaceAt(Rectangle textArea)
        {
            if (textArea.IsEmpty) return;
            int h = box.PreferredHeight;
            Bounds = new Rectangle(textArea.X, textArea.Y + (textArea.Height - h) / 2, textArea.Width, h);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            box.Focus();
            box.SelectAll();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter)
            {
                Commit(false);
                return true;
            }
            if (keyData == Keys.Escape)
            {
                done = true;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ほかの場所をクリックしたら決める. 名前が使えなければ, 何も変えずにやめる.
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!done) BeginInvoke((MethodInvoker)delegate { if (!done && !IsDisposed) Commit(true); });
        }

        private void Commit(bool leaving)
        {
            if (done) return;
            string reason = apply(box.Text);
            if (reason == null || leaving)
            {
                done = true;
                Close();
                return;
            }
            if (showError != null) showError(reason);
            box.Focus();
            box.SelectAll();
        }
    }
}
