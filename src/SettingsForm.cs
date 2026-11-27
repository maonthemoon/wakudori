using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Wakudori
{
    internal sealed class SettingsForm : ThemedForm
    {
        private readonly TrayApp app;
        private readonly AppSettings settings;
        private readonly Font body;
        private readonly Font small;
        private readonly Font strong;
        private readonly Font heading;
        private readonly Font sectionHeading;
        private readonly ToolTip tips = new ToolTip();
        private readonly Timer statusTimer = new Timer { Interval = 4000 };
        private readonly Label status;
        private readonly Label hotkey;
        private readonly ComboBox captureKey;
        private readonly Label savePath;
        private readonly ThemedButton resetFolder;
        private readonly Panel cleanupRow;
        private readonly SettingsSwitch cleanupSwitch;
        private readonly NumericUpDown cleanupDays;
        private readonly Label cleanupDaysUnit;
        private readonly SettingsSwitch startupSwitch;
        private const int ControlHeight = 32; // ボタンの高さ(hikidashiのCTL_H)

        public SettingsForm(TrayApp app, AppSettings settings)
        {
            this.app = app;
            this.settings = settings;
            Text = "Wakudori - 設定";
            TopMost = false;
            // 大きさはhikidashiの設定画面にそろえる(96dpiのときの値. 窓780×660まで, 余白24, 本文14px, 説明13px,
            // 区分の見出し20px, 部品の高さ32, 下の帯60)
            body = Theme.Body(S(14));
            small = Theme.Body(S(13));
            strong = Theme.BodyStrong(S(14));
            heading = Theme.Heading(S(28));
            sectionHeading = Theme.Heading(S(20));

            int width = S(780);
            int pad = Math.Max(S(24), SystemInformation.VerticalScrollBarWidth + S(6));
            int cardWidth = width - pad * 2;
            Label version = AddText("Wakudori " + Application.ProductVersion, small, Theme.TextTertiary, pad, S(23), cardWidth);
            Label title = AddText("設定", heading, Theme.Text, pad, version.Bottom + S(4), cardWidth);
            ThemedButton close = Button("×", ButtonKind.Ghost, Close);
            close.AccessibleName = "閉じる";
            close.Bounds = new Rectangle(width - S(16) - S(36), S(16), S(36), S(36));
            Controls.Add(close);

            int top = title.Bottom + S(16);
            if (!settings.CanSave)
            {
                Panel banner = new Panel { BackColor = Color.FromArgb(252, 235, 232), Location = new Point(pad, top), Width = cardWidth };
                Label warning = TextLabel("設定ファイルを読めないので変えられません. 設定フォルダーを開いて直してください",
                    small, Theme.Danger, cardWidth - S(24));
                warning.Location = new Point(S(12), S(8));
                banner.Controls.Add(warning);
                banner.Height = warning.Bottom + S(8);
                Controls.Add(banner);
                EnableDrag(banner);
                EnableDrag(warning);
                top = banner.Bottom + S(8);
            }

            Panel content = new Panel { AutoScroll = true, Location = new Point(1, top), Width = width - 2 };
            Controls.Add(content);
            int y = S(4);
            Panel capture = AddCard(content, pad - 1, cardWidth, "撮影", y);
            ThemedButton changeHotkey = Button("変更", ButtonKind.Secondary, ChangeHotkey);
            changeHotkey.Enabled = settings.CanSave;
            hotkey = new Label
            {
                Font = small, ForeColor = Theme.TextSecondary, TextAlign = ContentAlignment.MiddleRight,
                UseMnemonic = false, AutoEllipsis = true, Size = new Size(S(162), S(32))
            };
            AddRow(capture, "ホットキー", "選択画面を開くキー", hotkey, changeHotkey);
            RefreshHotkey();

            captureKey = Choice("撮影キー", 80);
            for (char key = 'A'; key <= 'Z'; key++)
            {
                int vk;
                if (HotkeyText.TryParseCaptureKey(key.ToString(), out vk)) captureKey.Items.Add(key.ToString());
            }
            captureKey.SelectedItem = ((char)settings.CaptureVk).ToString();
            captureKey.SelectedIndexChanged += ChangeCaptureKey;
            AddRow(capture, "撮影キー", "Enterのほかに撮るキー. 左手で押せるキー", captureKey);

            y = capture.Bottom + S(18);
            Panel storage = AddCard(content, pad - 1, cardWidth, "保存", y);
            ThemedButton changeFolder = Button("変更", ButtonKind.Secondary, ChooseSaveFolder);
            changeFolder.Enabled = settings.CanSave;
            resetFolder = Button("既定に戻す", ButtonKind.Secondary, delegate { ChangeSaveFolder(null); });
            savePath = new Label { Font = small, ForeColor = Theme.TextTertiary, UseMnemonic = false, Height = small.Height + S(2) };
            AddRow(storage, "保存先", savePath, changeFolder, resetFolder);
            RefreshSaveFolder();

            cleanupSwitch = new SettingsSwitch("古い画像をごみ箱へ移す", S(42), S(24)) { Checked = settings.AutoDelete, Enabled = settings.CanSave };
            cleanupRow = AddRow(storage, "古い画像をごみ箱へ移す",
                "Wakudoriで撮った画像だけを, 撮ってから指定の日数を過ぎたら移します. この版より前の画像や, 移した・名前を変えた・直した画像は消しません",
                cleanupSwitch);
            cleanupSwitch.Click += ToggleCleanup;
            cleanupRow.Click += ToggleCleanup;
            foreach (Control label in cleanupRow.Controls)
                if (label is Label) label.Click += ToggleCleanup;
            cleanupRow.Cursor = settings.CanSave ? Cursors.Hand : Cursors.Default;

            // 日数は打ち込むか上下の矢印で変える. 範囲の外の値は, 確定したときに1〜3650へ収まる.
            // 打ち込んでいる途中は反映せず, Enter, ほかの項目へ移る, 画面を閉じるのどれかで確定する
            cleanupDays = new NumericUpDown
            {
                AccessibleName = "日数", Font = body, Width = S(80), TextAlign = HorizontalAlignment.Right,
                Minimum = AppSettings.MinAutoDeleteDays, Maximum = AppSettings.MaxAutoDeleteDays,
                Value = settings.AutoDeleteDays
            };
            cleanupDays.ValueChanged += ChangeCleanupDays;
            cleanupDaysUnit = new Label
            {
                Text = "日", Font = body, ForeColor = Theme.Text, BackColor = Color.Transparent, UseMnemonic = false,
                AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
                Size = new Size(TextRenderer.MeasureText("日", body).Width, cleanupDays.Height)
            };
            RefreshCleanupDays();
            AddRow(storage, "日数", "撮ってから何日たった画像を移すか. 1〜3650日", cleanupDays, cleanupDaysUnit);

            y = storage.Bottom + S(18);
            Panel startup = AddCard(content, pad - 1, cardWidth, "起動", y);
            startupSwitch = new SettingsSwitch("PC起動時に起動", S(42), S(24)) { Checked = Startup.IsEnabled(), Enabled = settings.CanSave };
            startupSwitch.Click += ToggleStartup;
            AddRow(startup, "PC起動時に起動", (Label)null, startupSwitch);

            int contentHeight = startup.Bottom + S(16);
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            int footerHeight = S(60);
            int maxHeight = Math.Min(S(660), work.Height - S(24));
            content.Height = Math.Min(contentHeight, Math.Max(S(60), maxHeight - top - footerHeight));
            content.AutoScrollMinSize = new Size(0, contentHeight);
            int footer = content.Bottom;
            Controls.Add(new Panel { BackColor = Theme.BorderFaint, Bounds = new Rectangle(1, footer, width - 2, 1) });
            ThemedButton openConfig = Button("設定フォルダーを開く", ButtonKind.Secondary, delegate { TrayApp.OpenFolder(AppSettings.ConfigDir); });
            openConfig.Location = new Point(pad, footer + (footerHeight - openConfig.Height) / 2);
            ThemedButton done = Button("閉じる", ButtonKind.Accent, Close);
            done.Location = new Point(width - pad - done.Width, footer + (footerHeight - done.Height) / 2);
            status = new Label
            {
                Font = small, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleCenter, UseMnemonic = false,
                Bounds = new Rectangle(openConfig.Right + S(8), footer + S(4), done.Left - openConfig.Right - S(16), footerHeight - S(8))
            };
            Controls.AddRange(new Control[] { openConfig, status, done });
            EnableDrag(status);
            statusTimer.Tick += delegate { statusTimer.Stop(); status.Text = ""; tips.SetToolTip(status, null); };

            ClientSize = new Size(width, footer + footerHeight);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(work.Left + Math.Max(0, (work.Width - Width) / 2), work.Top + Math.Max(0, (work.Height - Height) / 2));
        }

        private ThemedButton Button(string text, ButtonKind kind, Action click)
        {
            ThemedButton button = new ThemedButton(text, kind, strong, UiScale);
            button.Height = S(ControlHeight);
            button.Click += delegate { click(); };
            return button;
        }

        private ComboBox Choice(string name, int width)
        {
            return new ComboBox
            {
                AccessibleName = name, DropDownStyle = ComboBoxStyle.DropDownList, Font = body, Width = S(width), Enabled = settings.CanSave
            };
        }

        private static Label TextLabel(string text, Font font, Color color, int width)
        {
            return new Label
            {
                Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent,
                AutoSize = true, MaximumSize = new Size(width, 0), UseMnemonic = false
            };
        }

        private Panel AddCard(Panel content, int left, int width, string title, int y)
        {
            Label label = TextLabel(title, sectionHeading, Theme.Text, width);
            label.Location = new Point(left, y);
            content.Controls.Add(label);
            EnableDrag(label);
            y = label.Bottom + S(7);
            Panel card = new SettingsCard(S(10)) { Location = new Point(left, y), Width = width, Height = S(10) };
            content.Controls.Add(card);
            return card;
        }

        private Panel AddRow(Panel card, string title, string description, params Control[] actions)
        {
            return AddRow(card, title, TextLabel(description, small, Theme.TextTertiary, card.Width), actions);
        }

        private Panel AddRow(Panel card, string title, Label detail, params Control[] actions)
        {
            int pad = S(15);
            int corner = S(10);
            int right = card.Width - 2 - pad;
            int height = S(50);
            for (int i = actions.Length - 1; i >= 0; i--)
            {
                Control action = actions[i];
                action.Left = right - action.Width;
                right = action.Left - S(8);
                height = Math.Max(height, action.Height + S(18));
            }
            int textWidth = right - pad;
            Label name = TextLabel(title, strong, Theme.Text, textWidth);
            Panel row = new Panel { BackColor = Theme.Surface, Width = card.Width - 2 };
            row.Controls.Add(name);
            name.Left = pad;
            int textHeight = name.Height;
            if (detail != null)
            {
                detail.MaximumSize = new Size(textWidth, 0);
                detail.Width = textWidth;
                detail.Left = pad;
                row.Controls.Add(detail);
                textHeight += S(3) + detail.Height;
            }
            row.Height = Math.Max(height, textHeight + S(20));
            name.Top = (row.Height - textHeight) / 2;
            if (detail != null) detail.Top = name.Bottom + S(3);
            foreach (Control action in actions)
            {
                action.Top = (row.Height - action.Height) / 2;
                row.Controls.Add(action);
            }
            bool firstRow = card.Controls.Count == 0;
            row.Location = new Point(1, firstRow ? corner : card.Height - corner + 1);
            if (!firstRow)
            {
                card.Controls.Add(new Panel
                {
                    BackColor = Theme.BorderFaint, Bounds = new Rectangle(pad, row.Top - 1, card.Width - pad * 2, 1)
                });
            }
            card.Controls.Add(row);
            card.Height = row.Bottom + corner;
            return row;
        }

        private void RefreshHotkey()
        {
            hotkey.Text = app.CurrentHotkey ?? "未登録";
            tips.SetToolTip(hotkey, hotkey.Text);
        }

        private void ChangeHotkey()
        {
            if (app.ShowHotkeyDialog(false, settings)) ShowStatus("反映しました", false);
            RefreshHotkey();
        }

        private void ChangeCaptureKey(object sender, EventArgs e)
        {
            string key = captureKey.SelectedItem as string;
            if (key == null || key[0] == settings.CaptureVk) return;
            if (TrySave("captureKey", key)) settings.CaptureVk = key[0];
            else captureKey.SelectedItem = ((char)settings.CaptureVk).ToString();
        }

        private void ChooseSaveFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "画像の保存先を選んでください";
                dialog.SelectedPath = settings.SaveDir;
                if (dialog.ShowDialog(this) == DialogResult.OK && !IsDisposed) ChangeSaveFolder(dialog.SelectedPath);
            }
        }

        private void ChangeSaveFolder(string path)
        {
            if (path != null && IsDefaultSaveFolder(path)) path = null;
            if (!TrySave("saveDir", path)) return;
            settings.SaveDir = path ?? AppSettings.DefaultSaveDir;
            RefreshSaveFolder();
        }

        private void RefreshSaveFolder()
        {
            savePath.Text = SettingsValues.ShortenPath(settings.SaveDir, savePath.Width,
                text => TextRenderer.MeasureText(text, small, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width);
            tips.SetToolTip(savePath, settings.SaveDir);
            resetFolder.Enabled = settings.CanSave && !IsDefaultSaveFolder(settings.SaveDir);
        }

        private static bool IsDefaultSaveFolder(string path)
        {
            return string.Equals(path.TrimEnd('\\', '/'),
                AppSettings.DefaultSaveDir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        private async void ToggleCleanup(object sender, EventArgs e)
        {
            if (!cleanupSwitch.Enabled) return;
            bool enable = !settings.AutoDelete;
            if (enable)
            {
                cleanupSwitch.Enabled = false;
                try
                {
                    int days = settings.AutoDeleteDays;
                    int count = await Task.Run(() => TrayApp.CountCleanupCandidates(days));
                    if (IsDisposed) return;
                    if (count > 0 && !Notice.Confirm(this, "古い画像をごみ箱へ移しますか?",
                        "今オンにすると, " + count + "枚をごみ箱へ移します", "オンにする")) return;
                }
                catch (Exception ex)
                {
                    if (!IsDisposed) ShowStatus("対象の画像を確認できませんでした. " + ex.Message, true);
                    return;
                }
                finally
                {
                    if (!IsDisposed) cleanupSwitch.Enabled = settings.CanSave;
                }
            }
            if (IsDisposed || !TrySave("autoDelete", enable)) return;
            settings.AutoDelete = enable;
            cleanupSwitch.Checked = enable;
            RefreshCleanupDays();
            app.ApplyCleanupSetting(enable, settings.AutoDeleteDays);
        }

        private void RefreshCleanupDays()
        {
            cleanupDays.Enabled = settings.CanSave && settings.AutoDelete;
            cleanupDays.BackColor = cleanupDays.Enabled ? Theme.Surface : Theme.BgSubtle;
            cleanupDaysUnit.ForeColor = cleanupDays.Enabled ? Theme.Text : Theme.TextTertiary;
        }

        private void ChangeCleanupDays(object sender, EventArgs e)
        {
            int days = (int)cleanupDays.Value;
            if (days == settings.AutoDeleteDays) return;
            if (TrySave("autoDeleteDays", days)) settings.AutoDeleteDays = days;
            else cleanupDays.Value = settings.AutoDeleteDays;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 打ち込んだまま閉じたときも反映する(Valueを読むと, 打ち込んだ文字が確定してValueChangedが起きる)
            if (cleanupDays.Enabled) { decimal unused = cleanupDays.Value; }
            base.OnFormClosing(e);
        }

        private void ToggleStartup(object sender, EventArgs e)
        {
            bool enable = !startupSwitch.Checked;
            string error = Startup.SetEnabled(enable);
            if (error != null) ShowStatus("自動起動を設定できませんでした. " + error, true);
            else
            {
                startupSwitch.Checked = enable;
                ShowStatus("反映しました", false);
            }
        }

        private bool TrySave(string key, object value)
        {
            try
            {
                settings.SaveValues(new Dictionary<string, object> { { key, value } });
            }
            catch (Exception ex)
            {
                ShowSaveError(ex.Message);
                return false;
            }
            ShowStatus("反映しました", false);
            return true;
        }

        internal void ShowSaveError(string reason)
        {
            ShowStatus("設定を保存できませんでした. " + reason, true);
        }

        private void ShowStatus(string message, bool error)
        {
            statusTimer.Stop();
            status.ForeColor = error ? Theme.Danger : Color.FromArgb(47, 107, 70);
            status.Text = message;
            tips.SetToolTip(status, error ? message : null);
            if (!error) statusTimer.Start();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && !captureKey.DroppedDown)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            statusTimer.Dispose();
            tips.Dispose();
            body.Dispose();
            small.Dispose();
            strong.Dispose();
            heading.Dispose();
            sectionHeading.Dispose();
        }
    }

    internal sealed class SettingsCard : Panel
    {
        private readonly int radius;

        public SettingsCard(int radius)
        {
            this.radius = radius;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath outline = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), radius))
            using (SolidBrush surface = new SolidBrush(Theme.Surface))
            using (Pen border = new Pen(Theme.Border))
            {
                e.Graphics.FillPath(surface, outline);
                e.Graphics.DrawPath(border, outline);
            }
        }
    }

    internal sealed class SettingsSwitch : CheckBox
    {
        public SettingsSwitch(string name, int width, int height)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AccessibleName = name;
            AutoCheck = false;
            Size = new Size(width, height);
            Cursor = Cursors.Hand;
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            Invalidate();
            base.OnCheckedChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color track = Checked ? Theme.AccentInteractive : Theme.Border;
            if (!Enabled) track = Theme.BgMuted;
            RectangleF bounds = new RectangleF(1, 1, Width - 2, Height - 2);
            using (GraphicsPath path = Theme.RoundedRect(bounds, bounds.Height / 2))
            using (SolidBrush fill = new SolidBrush(track))
            using (SolidBrush thumb = new SolidBrush(Theme.Surface))
            {
                g.FillPath(fill, path);
                float pad = Height / 6f;
                float diameter = Height - pad * 2;
                g.FillEllipse(thumb, Checked ? Width - pad - diameter : pad, pad, diameter, diameter);
            }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, ClientRectangle);
        }
    }
}
