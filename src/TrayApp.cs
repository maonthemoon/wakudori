using System;
using System.Diagnostics;
using System.Drawing;
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

            iconHandle = Theme.CreateAppIconHandle(SystemInformation.SmallIconSize.Width);
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

            Notice.Show("ホットキーを使えません", problem + "\r\n別のキーを登録してください.", NoticeKind.Warning);
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
                        if (error != null) Notice.Show("前のホットキーに戻せませんでした", error, NoticeKind.Warning);
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
                Notice.Show("設定ファイルに保存できませんでした",
                    "ホットキーは登録しましたが, 次に起動したときは登録し直しになります.\r\n\r\n" + ex.Message, NoticeKind.Warning);
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
                Notice.Show("設定ファイルを読めません", error, NoticeKind.Warning);
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
                Notice.Show("選択画面を開けませんでした", ex.ToString(), NoticeKind.Error);
            }
        }

        // ---- メニュー ----

        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripProfessionalRenderer renderer = new ToolStripProfessionalRenderer(new Theme.MenuColors());
            renderer.RoundedEdges = false;
            menu.Renderer = renderer;
            menu.BackColor = Theme.Bg;
            menu.ForeColor = Theme.Text;
            menu.Padding = new Padding(0, 4, 0, 4);
            ToolStripMenuItem select = new ToolStripMenuItem("撮影");
            select.Font = new Font(select.Font, FontStyle.Bold);
            select.Click += delegate { OpenSelection(); };
            menu.Items.Add(select);
            menu.Items.Add(new ToolStripMenuItem("ホットキー再登録", null, delegate { ShowHotkeyDialog(false); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("画像保存フォルダーを開く", null, delegate { OpenFolder(settings.SaveDir); }));
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
                Notice.Show("フォルダーを開けませんでした", path + "\r\n\r\n" + ex.Message, NoticeKind.Warning);
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
    }
}
