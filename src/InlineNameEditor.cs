using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Wakudori
{
    // 一覧の名前をダブルクリックしたときに, その名前の上に重ねて出す入力欄(その場で名前を変える).
    // 選択画面はレイヤードウィンドウで入力欄を置けないので, 小さな枠なしの画面を名前の位置にぴったり重ねる.
    // Enterで決める, Escでやめる. ほかの場所をクリックしたときも決める(名前が使えなければやめる).
    internal sealed class InlineNameEditor : Form
    {
        private readonly TextBox box;
        private readonly Label error;
        private readonly Func<string, string> apply; // 名前を変える. 成功ならnull, だめならその理由
        private readonly float scale;
        private readonly int pillHeight;
        private bool done;

        // itemRect: 名前の場所(画面の座標, 物理ピクセル). scale: そのモニターの拡大率(96 DPIで1)
        public InlineNameEditor(Rectangle itemRect, string name, float scale, Func<string, string> apply)
        {
            this.apply = apply;
            this.scale = scale;
            pillHeight = itemRect.Height;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            DoubleBuffered = true;
            Text = "Wakudori - 名前を変える";

            int width = Math.Max(itemRect.Width, S(220));
            Bounds = new Rectangle(itemRect.X, itemRect.Y, width, pillHeight);

            box = new TextBox();
            box.BorderStyle = BorderStyle.None;
            box.Font = Theme.Body(14 * scale);
            box.BackColor = Theme.Surface;
            box.ForeColor = Theme.Text;
            box.MaxLength = 60;
            box.Text = name ?? "";
            box.Location = new Point(S(14), (pillHeight - box.PreferredHeight) / 2);
            box.Width = width - S(28);
            box.TextChanged += delegate { HideError(); };
            Controls.Add(box);

            error = new Label();
            error.AutoSize = false;
            error.Font = Theme.Body(13 * scale);
            error.ForeColor = Theme.Danger;
            error.BackColor = Theme.Bg;
            error.Location = new Point(S(12), pillHeight + S(4));
            error.Size = new Size(width - S(24), S(24));
            error.TextAlign = ContentAlignment.MiddleLeft;
            error.Visible = false;
            Controls.Add(error);

            UpdateShape();
        }

        private int S(float v)
        {
            return (int)Math.Round(v * scale);
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
            error.Text = reason;
            error.Visible = true;
            UpdateShape();
            box.Focus();
            box.SelectAll();
        }

        private void HideError()
        {
            if (!error.Visible) return;
            error.Visible = false;
            UpdateShape();
        }

        // 名前のチップと同じ丸い形にする. 説明を出すときは下に伸ばす.
        private void UpdateShape()
        {
            int h = error.Visible ? pillHeight + S(32) : pillHeight;
            Height = h;
            using (GraphicsPath path = error.Visible
                ? Theme.RoundedRect(new RectangleF(0, 0, Width, h), S(10))
                : Theme.RoundedRect(new RectangleF(0, 0, Width, h), h / 2f))
            {
                Region = new Region(path);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (error.Visible)
            {
                using (SolidBrush b = new SolidBrush(Theme.Bg))
                {
                    g.FillRectangle(b, 0, pillHeight, Width, Height - pillHeight);
                }
            }
            // 入力中と分かるよう, 名前の部分を濃い線で囲む
            using (GraphicsPath path = Theme.RoundedRect(new RectangleF(1, 1, Width - 3, pillHeight - 3), (pillHeight - 3) / 2f))
            using (Pen pen = new Pen(Theme.Text, Math.Max(1f, 1.5f * scale)))
            {
                g.DrawPath(pen, path);
            }
        }
    }
}
