using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

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
        private readonly Timer cleanupTimer;
        private CancellationTokenSource cleanupRun;
        private int? requestedCleanupDays;
        private SettingsForm settingsForm;
        private CaptureSession session;
        private bool dialogOpen;
        private volatile bool exiting;
        private bool askingExit;
        private bool reportingError;

        public TrayApp(AppSettings settings)
        {
            this.settings = settings;
            Application.ThreadException += OnThreadException;

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

            cleanupTimer = new Timer { Interval = (int)TimeSpan.FromMinutes(1).TotalMilliseconds };
            cleanupTimer.Tick += OnCleanupTick;
            cleanupTimer.Start();

            string startupError = Startup.FixPathIfMoved();
            if (startupError != null)
            {
                Notice.Show("自動起動の場所を直せませんでした", startupError, NoticeKind.Warning);
            }

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

        internal string CurrentHotkey
        {
            get { return hotkeyWindow.IsRegistered ? settings.Hotkey : null; }
        }

        internal bool ShowHotkeyDialog(bool firstRun, AppSettings editable = null)
        {
            if (dialogOpen || session != null || exiting) return false;
            SettingsForm owner = editable == null ? null : settingsForm;
            dialogOpen = true;
            try
            {
                // 登録中に今のキーを押しても入力できるよう, いったん外す.
                bool hadHotkey = hotkeyWindow.IsRegistered;
                hotkeyWindow.Unregister();

                string current = hadHotkey ? settings.Hotkey : null;
                using (HotkeyForm form = new HotkeyForm(current, firstRun, spec => TryRegister(spec, editable, owner)))
                {
                    if (owner != null)
                    {
                        form.StartPosition = FormStartPosition.CenterParent;
                        form.ShowInTaskbar = false;
                    }
                    DialogResult result = owner == null ? form.ShowDialog() : form.ShowDialog(owner);
                    if (result == DialogResult.OK)
                    {
                        if (firstRun) AskStartupOnFirstRun();
                        return true;
                    }
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
                return false;
            }
            finally
            {
                dialogOpen = false;
                UpdateTrayText();
            }
        }

        // 登録ダイアログから呼ばれる. 登録できたら設定ファイルにも書く.
        private string TryRegister(HotkeySpec spec, AppSettings editable, SettingsForm owner)
        {
            string error = hotkeyWindow.Register(spec);
            if (error != null) return error;

            string text = HotkeyText.Format(spec);
            try
            {
                (editable ?? settings).SaveHotkey(text);
            }
            catch (Exception ex)
            {
                if (editable != null)
                {
                    // 登録を保留し, 画面を閉じるときに前のキーへ戻す.
                    hotkeyWindow.Unregister();
                    owner.ShowSaveError(ex.Message);
                    return "設定を保存できませんでした.\r\n" + ex.Message;
                }
                Notice.Show("設定ファイルに保存できませんでした",
                    "ホットキーは登録しましたが, 次に起動したときは登録し直しになります.\r\n\r\n" + ex.Message, NoticeKind.Warning);
            }
            settings.Hotkey = text;
            return null;
        }

        // ---- PC起動時に起動 ----

        // 初回の登録が済んだ直後にだけ聞く. すでに登録されていれば聞かない
        private static void AskStartupOnFirstRun()
        {
            if (Startup.IsEnabled()) return;
            if (!Notice.Confirm(null, "PCの起動時にWakudoriを起動しますか?",
                "あとからトレイの「設定」で変えられます.", "起動する")) return;
            string error = Startup.SetEnabled(true);
            if (error != null) Notice.Show("自動起動を設定できませんでした", error, NoticeKind.Warning);
        }

        // ---- 古い画像の自動整理 ----

        private void OnCleanupTick(object sender, EventArgs e)
        {
            cleanupTimer.Interval = (int)TimeSpan.FromHours(6).TotalMilliseconds;
            StartCleanup();
        }

        internal void ApplyCleanupSetting(bool enabled, int days)
        {
            if (enabled) StartCleanup(days);
            else
            {
                requestedCleanupDays = null;
                if (cleanupRun != null) cleanupRun.Cancel();
            }
        }

        internal static int CountCleanupCandidates(int days)
        {
            return SelectCleanup(CaptureLog.Read(CaptureLog.DefaultPath), days, true).ToRecycle.Count;
        }

        private static CleanupResult SelectCleanup(List<CaptureRecord> records, int days, bool autoDelete)
        {
            return Cleanup.Select(records, DateTime.UtcNow, days, autoDelete,
                CaptureRecycler.ReadState, CaptureRecycler.IsOnFixedDrive);
        }

        private async void StartCleanup(int? confirmedDays = null)
        {
            if (exiting) return;
            if (cleanupRun != null)
            {
                if (confirmedDays.HasValue) requestedCleanupDays = confirmedDays;
                return;
            }
            cleanupRun = new CancellationTokenSource();
            CancellationToken token = cleanupRun.Token;
            try
            {
                // 設定画面からの実行には確認に使った日数を使う. 定期実行ではファイルを読み直す.
                AppSettings latest = confirmedDays.HasValue
                    ? new AppSettings { AutoDelete = true, AutoDeleteDays = confirmedDays.Value }
                    : LoadCleanupSettings();
                if (exiting) return;
                int moved = await Task.Run(() => Cleanup.RunIfSettingsReadable(latest.CanSave, () => RunCleanup(latest, token)));

                // awaitの後はWinFormsのUIスレッドで知らせる
                if (exiting) return;
                if (moved > 0)
                    tray.ShowBalloonTip(5000, "Wakudori", latest.AutoDeleteDays + "日を過ぎたWakudoriの画像を" + moved + "枚ごみ箱へ移しました", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Program.LogError("CaptureLog.Cleanup", ex);
            }
            finally
            {
                cleanupRun.Dispose();
                cleanupRun = null;
                if (requestedCleanupDays.HasValue)
                {
                    int days = requestedCleanupDays.Value;
                    requestedCleanupDays = null;
                    StartCleanup(days);
                }
            }
        }

        private static AppSettings LoadCleanupSettings()
        {
            string error;
            AppSettings latest = AppSettings.Load(out error);
            if (error != null)
            {
                Notice.Show("設定ファイルを読めません", error, NoticeKind.Warning);
            }
            else if (latest.AutoDeleteProblem != null || latest.AutoDeleteDaysProblem != null)
            {
                string problem = latest.AutoDeleteProblem ?? latest.AutoDeleteDaysProblem;
                if (latest.AutoDeleteProblem != null && latest.AutoDeleteDaysProblem != null)
                    problem += "\r\n\r\n" + latest.AutoDeleteDaysProblem;
                Notice.Show("画像の自動整理の設定を読めません", problem, NoticeKind.Warning);
            }
            return latest;
        }

        private int RunCleanup(AppSettings latest, CancellationToken token)
        {
            int moved = 0;
            if (exiting) return moved;
            try
            {
                string logPath = CaptureLog.DefaultPath;
                List<CaptureRecord> records = CaptureLog.Read(logPath);
                CleanupResult result = SelectCleanup(records, latest.AutoDeleteDays, latest.AutoDelete);
                foreach (CaptureRecord record in result.ToRecycle)
                {
                    if (exiting || token.IsCancellationRequested) break;
                    // 選別後にドライブの接続が変わることがあるので, 移す前にもう一度確かめる
                    if (!CaptureRecycler.IsOnFixedDrive(record.FullPath)) result.ToForget.Add(record);
                    else if (CaptureRecycler.TryMove(record))
                    {
                        result.ToForget.Add(record);
                        moved++;
                    }
                }
                CaptureLog.Remove(logPath, result.ToForget);
            }
            catch (Exception ex)
            {
                Program.LogError("CaptureLog.Cleanup", ex);
            }
            return moved;
        }

        // ---- マニュアル ----

        // docs\manual.htmlを既定のブラウザーで開く. Webのマニュアルは見にいかない.
        // ZIPでもリポジトリでも, exe(bin\かout\)の1つ上のdocsにある. v0.3.3までのZIPではexeの隣のdocs.
        private static void OpenManual()
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            string target = Path.Combine(dir, "docs", "manual.html");
            if (!File.Exists(target)) target = Path.GetFullPath(Path.Combine(dir, "..", "docs", "manual.html"));
            if (!File.Exists(target))
            {
                Notice.Show("マニュアルが見つかりません", "exeと一緒に配られたdocsフォルダー(docs\\manual.html)が必要です. exeを別の場所へ移すときは, binとdocsを入れたフォルダーごと移してください.", NoticeKind.Warning);
                return;
            }
            try
            {
                Process.Start(target);
            }
            catch (Exception ex)
            {
                Notice.Show("マニュアルを開けませんでした", target + "\r\n\r\n" + ex.Message, NoticeKind.Warning);
            }
        }

        private void UpdateTrayText()
        {
            // 問い合わせのときに版が分かるよう, 版も出す
            string name = "Wakudori " + Application.ProductVersion;
            string text = hotkeyWindow.IsRegistered && settings.Hotkey != null
                ? name + " (" + settings.Hotkey + ")"
                : name + " (ホットキー未登録)";
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

            // まず画面を撮る. 選択画面や知らせを出すと, 開いていたメニューが閉じるため.
            Rectangle[] monitors = MonitorBounds();
            FrozenScreens frozen = null;
            try
            {
                frozen = FrozenScreens.Capture(monitors);
                MenuCloser.CloseActiveMenu();
                frozen.Prepare(OverlayForm.DimColor);
            }
            catch (Exception)
            {
                // 撮れなければ, これまでどおり動いている画面の上で選ぶ.
                if (frozen != null) frozen.Dispose();
                frozen = null;
            }

            // 保存先と撮影キーは開くたびに読み直す(手で書き換えても再起動しなくてよい). ホットキーは読み直さない.
            string error;
            AppSettings latest = AppSettings.Load(out error);
            if (error != null)
            {
                Notice.Show("設定ファイルを読めません", error, NoticeKind.Warning);
            }
            else if (latest.CaptureKeyProblem != null)
            {
                Notice.Show("撮影キーを読めません", latest.CaptureKeyProblem, NoticeKind.Warning);
            }
            try
            {
                session = new CaptureSession(latest.SaveDir, (Keys)latest.CaptureVk, DefaultFrameSize, monitors, frozen);
                session.Finished += delegate { session = null; };
            }
            catch (Exception ex)
            {
                session = null;
                if (frozen != null) frozen.Dispose();
                Program.LogError("OpenSelection", ex);
                Notice.Show("選択画面を開けませんでした", ex.ToString(), NoticeKind.Error);
            }
        }

        private static Rectangle[] MonitorBounds()
        {
            Screen[] screens = Screen.AllScreens;
            Rectangle[] r = new Rectangle[screens.Length];
            for (int i = 0; i < screens.Length; i++) r[i] = screens[i].Bounds;
            return r;
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
            menu.Items.Add(new ToolStripMenuItem("設定", null, delegate { OpenSettings(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("画像保存フォルダーを開く", null, delegate
            {
                string error;
                AppSettings latest = AppSettings.Load(out error);
                if (error != null) Notice.Show("設定ファイルを読めません", error, NoticeKind.Warning);
                OpenFolder(latest.SaveDir);
            }));
            menu.Items.Add(new ToolStripMenuItem("マニュアル", null, delegate { OpenManual(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("終了", null, delegate { Exit(); }));
            return menu;
        }

        private void OpenSettings()
        {
            if (exiting || dialogOpen || askingExit) return;
            if (settingsForm != null)
            {
                if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
                settingsForm.Activate();
                return;
            }
            string error;
            AppSettings latest = AppSettings.Load(out error);
            settingsForm = new SettingsForm(this, latest);
            settingsForm.FormClosed += delegate { settingsForm = null; };
            settingsForm.Show();
        }

        internal static void OpenFolder(string path)
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

        // ---- 想定外のエラー ----

        // 画面の操作中に起きた想定外のエラー. 記録して知らせ, 選択画面は閉じて動き続ける.
        // 書き込みの画面は, 書き込みを失わないよう開いたままにする.
        private void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            bool logged = Program.LogError("ThreadException", e.Exception);
            if (reportingError) return; // 知らせている間の次のエラーは, 記録だけ
            reportingError = true;
            bool dialogWasOpen = dialogOpen;
            dialogOpen = true; // 知らせている間は, 選択画面を開かない(お知らせの上に重なるため)
            CaptureSession open = session;
            session = null; // 閉じるのに失敗しても, 次のホットキーで開き直せるように
            try
            {
                if (open != null) open.Close(); // 全画面を覆ったままにしない
            }
            catch (Exception ex)
            {
                Program.LogError("OnThreadException(Close)", ex);
            }
            try
            {
                Notice.Show("エラーが起きました", e.Exception.Message + "\r\n\r\nWakudoriはこのまま使えます."
                    + (logged ? " 詳しい内容は, 設定フォルダーのerror.logに記録しました." : ""), NoticeKind.Error);
            }
            catch (Exception ex)
            {
                Program.LogError("OnThreadException", ex);
            }
            finally
            {
                reportingError = false;
                dialogOpen = dialogWasOpen;
            }
        }

        // 書き込みの途中なら, 捨ててよいか確かめる. 終了してよければtrue
        private bool ConfirmDiscardMarks()
        {
            foreach (Form f in Application.OpenForms)
            {
                MarkForm mark = f as MarkForm;
                if (mark == null || !mark.HasUnsavedMarks) continue;
                bool dialogWasOpen = dialogOpen;
                askingExit = true;
                dialogOpen = true; // 確かめている間は, 選択画面を開かない
                try
                {
                    return Notice.Confirm(mark, "書き込みを捨てて終了しますか?",
                        "書き込みの途中の画像があります. 書き込む前の画像は, 保存フォルダーに残っています.", "終了する");
                }
                finally
                {
                    askingExit = false;
                    dialogOpen = dialogWasOpen;
                }
            }
            return true;
        }

        private void Exit()
        {
            if (exiting || askingExit || dialogOpen) return;
            if (!ConfirmDiscardMarks()) return;
            exiting = true;
            cleanupTimer.Dispose();
            if (settingsForm != null) settingsForm.Close();
            if (session != null) session.Close();
            tray.Visible = false;
            tray.Dispose();
            hotkeyWindow.Dispose();
            NativeMethods.DestroyIcon(iconHandle);
            ExitThread();
        }
    }
}
