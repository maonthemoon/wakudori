using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Wakudori
{
    // タスクトレイに常駐し, ホットキーかトレイのアイコンで選択画面を開く(B1, B2).
    internal sealed class TrayApp : ApplicationContext
    {
        private static readonly Size DefaultFrameSize = new Size(800, 600);

        private readonly AppSettings settings;
        private readonly HotkeyWindow hotkeyWindow;
        private readonly NotifyIcon tray;
        private readonly IntPtr iconHandle;
        private CaptureSession session;
        private bool dialogOpen;
        private bool exiting;

        public TrayApp(AppSettings settings)
        {
            this.settings = settings;

            hotkeyWindow = new HotkeyWindow();
            hotkeyWindow.Pressed += delegate { OpenSelection(); };

            iconHandle = CreateIconHandle(SystemInformation.SmallIconSize.Width);
            tray = new NotifyIcon();
            tray.Icon = Icon.FromHandle(iconHandle);
            tray.ContextMenuStrip = BuildMenu();
            tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) OpenSelection();
            };
            tray.Visible = true;

            SetUpHotkeyAtStartup();
            UpdateTrayText();
        }

        // ---- ホットキー ----

        private void SetUpHotkeyAtStartup()
        {
            if (settings.Hotkey == null)
            {
                ShowHotkeyDialog(true); // 初回の登録
                return;
            }

            HotkeySpec spec;
            string problem;
            if (!HotkeyText.TryParse(settings.Hotkey, out spec))
            {
                problem = "設定ファイルのホットキー「" + settings.Hotkey + "」を読めませんでした.";
            }
            else
            {
                problem = HotkeyText.Validate(spec);
                if (problem == null) problem = hotkeyWindow.Register(spec);
            }
            if (problem == null) return;

            MessageBox.Show(problem + "\r\n\r\n別のキーを登録してください.", "Wakudori", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ShowHotkeyDialog(false);
        }

        private void ShowHotkeyDialog(bool firstRun)
        {
            if (dialogOpen || session != null) return;
            dialogOpen = true;
            try
            {
                // 登録中に今のキーを押しても入力できるよう, いったん外す.
                bool hadHotkey = hotkeyWindow.IsRegistered;
                hotkeyWindow.Unregister();

                string current = hadHotkey ? settings.Hotkey : null;
                using (HotkeyForm form = new HotkeyForm(current, firstRun, TryRegister))
                {
                    if (form.ShowDialog() == DialogResult.OK) return;
                }

                // やめたときは前のキーに戻す.
                if (hadHotkey)
                {
                    HotkeySpec old;
                    if (HotkeyText.TryParse(settings.Hotkey, out old))
                    {
                        string error = hotkeyWindow.Register(old);
                        if (error != null) MessageBox.Show(error, "Wakudori", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                else
                {
                    tray.ShowBalloonTip(5000, "Wakudori",
                        "ホットキーは登録されていません. トレイのアイコンをクリックすると範囲を選べます. 右クリックのメニューから登録できます.",
                        ToolTipIcon.Info);
                }
            }
            finally
            {
                dialogOpen = false;
                UpdateTrayText();
            }
        }

        // 登録ダイアログから呼ばれる. 登録できたら設定ファイルにも書く.
        private string TryRegister(HotkeySpec spec)
        {
            string error = hotkeyWindow.Register(spec);
            if (error != null) return error;

            string text = HotkeyText.Format(spec);
            try
            {
                settings.SaveHotkey(text);
            }
            catch (Exception ex)
            {
                settings.Hotkey = text;
                MessageBox.Show("ホットキーは登録しましたが, 設定ファイルに保存できませんでした. 次に起動したときは登録し直しになります.\r\n\r\n" + ex.Message,
                    "Wakudori", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return null;
        }

        private void UpdateTrayText()
        {
            string text = hotkeyWindow.IsRegistered && settings.Hotkey != null
                ? "Wakudori (" + settings.Hotkey + ")"
                : "Wakudori (ホットキー未登録)";
            if (text.Length > 63) text = text.Substring(0, 63);
            tray.Text = text;
        }

        // ---- 選択画面 ----

        private void OpenSelection()
        {
            if (exiting || dialogOpen) return;
            if (session != null)
            {
                session.Activate(); // 開いている選択画面を前に出す
                return;
            }

            // 保存先は開くたびに読み直す(手で書き換えても再起動しなくてよい). ホットキーは読み直さない.
            string error;
            AppSettings latest = AppSettings.Load(out error);
            if (error != null)
            {
                MessageBox.Show(error, "Wakudori", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            settings.SaveDir = latest.SaveDir;
            settings.CanSave = latest.CanSave;

            try
            {
                session = new CaptureSession(settings.SaveDir, DefaultFrameSize);
                session.Finished += delegate { session = null; };
            }
            catch (Exception ex)
            {
                session = null;
                MessageBox.Show("選択画面を開けませんでした.\r\n\r\n" + ex, "Wakudori", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---- メニュー ----

        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem select = new ToolStripMenuItem("範囲を選ぶ");
            select.Font = new Font(select.Font, FontStyle.Bold);
            select.Click += delegate { OpenSelection(); };
            menu.Items.Add(select);
            menu.Items.Add(new ToolStripMenuItem("ホットキーを登録し直す", null, delegate { ShowHotkeyDialog(false); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("保存先のフォルダーを開く", null, delegate { OpenFolder(settings.SaveDir); }));
            menu.Items.Add(new ToolStripMenuItem("設定フォルダーを開く", null, delegate { OpenFolder(AppSettings.ConfigDir); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("終了", null, delegate { Exit(); }));
            return menu;
        }

        private static void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show("フォルダーを開けませんでした: " + path + "\r\n\r\n" + ex.Message, "Wakudori", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Exit()
        {
            if (exiting) return;
            exiting = true;
            if (session != null) session.Close();
            tray.Visible = false;
            tray.Dispose();
            hotkeyWindow.Dispose();
            NativeMethods.DestroyIcon(iconHandle);
            ExitThread();
        }

        // ---- アイコン ----

        // クレイ色の角丸の四角に, 白い枠と四隅の丸を描く. 明るいタスクバーでも暗いタスクバーでも見える.
        private static IntPtr CreateIconHandle(int size)
        {
            if (size < 16) size = 16;
            using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    float r = size * 0.22f;
                    using (GraphicsPath bg = RoundedRect(new RectangleF(0, 0, size - 1, size - 1), r))
                    using (SolidBrush clay = new SolidBrush(OverlayForm.AccentColor))
                    {
                        g.FillPath(clay, bg);
                    }

                    float inset = size * 0.27f;
                    RectangleF frame = new RectangleF(inset, inset, size - 1 - inset * 2, size - 1 - inset * 2);
                    float line = Math.Max(1f, size / 16f);
                    using (Pen pen = new Pen(Color.White, line))
                    {
                        g.DrawRectangle(pen, frame.X, frame.Y, frame.Width, frame.Height);
                    }
                    float d = Math.Max(3f, size * 0.2f);
                    using (SolidBrush white = new SolidBrush(Color.White))
                    {
                        PointF[] corners =
                        {
                            new PointF(frame.Left, frame.Top), new PointF(frame.Right, frame.Top),
                            new PointF(frame.Left, frame.Bottom), new PointF(frame.Right, frame.Bottom)
                        };
                        foreach (PointF c in corners) g.FillEllipse(white, c.X - d / 2, c.Y - d / 2, d, d);
                    }
                }
                return bmp.GetHicon();
            }
        }

        private static GraphicsPath RoundedRect(RectangleF rc, float r)
        {
            float d = r * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(rc.Left, rc.Top, d, d, 180, 90);
            p.AddArc(rc.Right - d, rc.Top, d, d, 270, 90);
            p.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0, 90);
            p.AddArc(rc.Left, rc.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
