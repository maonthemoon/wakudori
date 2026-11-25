using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace Wakudori
{
    // タスクトレイに常駐し, ホットキーで選択画面を開く(SPEC.mdの8章の4). 2つ目は起動しない.
    internal static class Program
    {
        private const string MutexName = "Local\\Wakudori.SingleInstance";

        [STAThread]
        private static int Main()
        {
            // マニフェストでPer-Monitor V2にしている. 念のためAPIでも指定する(すでに指定済みなら失敗するのが正常).
            try
            {
                NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            }
            catch (EntryPointNotFoundException)
            {
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 想定外のエラーを記録する. 画面の操作中のものは, 知らせて動き続ける(TrayAppが受ける).
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                LogError("UnhandledException", e.ExceptionObject as Exception);
            };

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    Notice.Show("すでに起動しています", "タスクトレイのアイコンかホットキーで起動", NoticeKind.Info);
                    return 0;
                }

                string error;
                AppSettings settings = AppSettings.Load(out error);
                if (error != null)
                {
                    Notice.Show("設定ファイルを読めません", error + "\r\n\r\n既定の設定で続けます. このファイルは上書きしません.", NoticeKind.Warning);
                }

                try
                {
                    Application.Run(new TrayApp(settings));
                }
                catch (Exception ex)
                {
                    LogError("Main", ex);
                    Notice.Show("エラーで終了します", ex.ToString(), NoticeKind.Error);
                    return 1;
                }
                return 0;
            }
        }

        // 記録できたらtrue
        public static bool LogError(string where, Exception ex)
        {
            return ErrorLog.Write(ErrorLog.DefaultPath, Application.ProductVersion, where, ex, DateTime.Now);
        }

        public static uint GetMonitorDpi(Rectangle bounds)
        {
            NativeMethods.POINT center = new NativeMethods.POINT(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            IntPtr hmon = NativeMethods.MonitorFromPoint(center, NativeMethods.MONITOR_DEFAULTTONEAREST);
            uint dpiX, dpiY;
            try
            {
                if (NativeMethods.GetDpiForMonitor(hmon, NativeMethods.MDT_EFFECTIVE_DPI, out dpiX, out dpiY) == 0)
                {
                    return dpiX;
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return 96;
        }
    }

    // 選択画面を1回開いて, 撮るか閉じるまでを受け持つ. 閉じたらFinishedを出す.
    internal sealed class CaptureSession
    {
        private readonly string saveDir;
        private readonly RegionStore store;
        private readonly List<OverlayForm> forms = new List<OverlayForm>();
        private readonly Selection selection;
        private readonly Rectangle[] monitors;
        private readonly Size defaultFrameSize;
        private bool capturing;
        private bool closed;
        // 一覧の名前に乗せたときの下見(枠ごと動かす). 下見の前の枠と名前を覚えておき, 離れたら戻す
        private bool previewing;
        private Rectangle frameBeforePreview;
        private string nameBeforePreview;
        private readonly FrozenScreens frozen; // 開いた瞬間に撮った全モニターの絵. 撮れなければnull
        private bool live;                     // Space: 止めた絵ではなく, 動いている画面を見て撮る
        private Timer foregroundTimer;
        private InlineNameEditor editor; // 名前を変えている最中の入力欄

        public event EventHandler Finished;

        public CaptureSession(string saveDir, Size defaultFrameSize, Rectangle[] monitors, FrozenScreens frozen)
        {
            this.saveDir = saveDir;
            this.defaultFrameSize = defaultFrameSize;

            this.monitors = monitors;
            this.frozen = frozen;
            live = frozen == null;

            // 前回の範囲(A2)を読む. 開くたびに読み直す(A5). 読めないときは, 前回の範囲なしで開く(7.1).
            string error;
            store = RegionStore.Load(RegionStore.DefaultPath, out error);
            if (error != null)
            {
                Notice.Show("範囲の保存ファイルを読めません", error, NoticeKind.Warning);
            }

            // 前回の範囲がなければ, マウスカーソルがあるモニターの中央に出す(C5).
            // 前回の範囲が画面の外になっていれば, カーソルのモニターの中央に同じ大きさで出し直す(C7).
            Point cursor = Cursor.Position;
            Rectangle initial = Selection.InitialFrame(monitors, store.Last, cursor, defaultFrameSize);
            selection = new Selection(monitors, initial);

            try
            {
                OpenForms(cursor);
            }
            catch
            {
                CloseAll(); // 途中まで出した選択画面を残さない
                throw;
            }
        }

        private void OpenForms(Point cursor)
        {
            for (int i = 0; i < monitors.Length; i++)
            {
                // 保存した範囲の一覧は, 開いたときにマウスカーソルがあるモニターにだけ出す(A7).
                bool listHere = monitors[i].Contains(cursor);
                OverlayForm f = new OverlayForm(i, monitors[i], Program.GetMonitorDpi(monitors[i]), selection, store.Regions, listHere, frozen != null ? frozen.Get(i) : null);
                f.CaptureRequested += OnCaptureRequested;
                f.CancelRequested += OnCancelRequested;
                f.LiveToggleRequested += OnLiveToggleRequested;
                f.EditRequested += OnEditRequested;
                f.FullScreenRequested += delegate(object sender, EventArgs e) { CaptureFullScreen((OverlayForm)sender, false); };
                f.FullScreenEditRequested += delegate(object sender, EventArgs e) { CaptureFullScreen((OverlayForm)sender, true); };
                f.SaveRequested += OnSaveRequested;
                f.RegionChosen += OnRegionChosen;
                f.RegionHovered += OnRegionHovered;
                f.RegionEditRequested += OnRegionEditRequested;
                f.RegionDeleteRequested += OnRegionDeleteRequested;
                f.RegionMoved += OnRegionMoved;
                f.PreviewCancelRequested += delegate { EndPreview(); };
                forms.Add(f);
            }

            foreach (OverlayForm f in forms) f.Show();
            activeIndex = 0;
            for (int i = 0; i < forms.Count; i++)
            {
                if (forms[i].MonitorBounds.Contains(cursor)) { activeIndex = i; break; }
            }
            Activate();
            StartForegroundCheck();
        }

        // 前面に出られた(キー入力が届く)かを調べる. 出られていなければ, 少し待って出し直す(メニューが開いていたときなど).
        private void StartForegroundCheck()
        {
            int tries = 0;
            foregroundTimer = new Timer();
            foregroundTimer.Interval = 50;
            foregroundTimer.Tick += delegate
            {
                tries++;
                if (closed || capturing || editor != null || IsForeground() || tries > 6)
                {
                    foregroundTimer.Stop();
                    return;
                }
                Activate();
            };
            foregroundTimer.Start();
        }

        private bool IsForeground()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            foreach (OverlayForm f in forms)
            {
                if (f.IsHandleCreated && f.Handle == fg) return true;
            }
            return false;
        }

        private void OnLiveToggleRequested(object sender, EventArgs e)
        {
            if (capturing || closed || frozen == null) return;
            live = !live;
            foreach (OverlayForm f in forms) f.SetLive(live);
        }

        private int activeIndex;

        // キーボードを受けるため, 開いたときにマウスカーソルがあったモニターのフォームを前面にする.
        public void Activate()
        {
            if (closed || capturing || forms.Count == 0) return;
            forms[activeIndex].Activate();
        }

        // 撮らずに閉じる(終了するときなど).
        public void Close()
        {
            CloseAll();
        }

        private void OnCancelRequested(object sender, EventArgs e)
        {
            // キー処理の途中でフォームを破棄しないよう, 処理が終わってから閉じる.
            ((Control)sender).BeginInvoke((MethodInvoker)CloseAll);
        }

        // 一覧の名前をクリックした: その範囲を枠にセットする(A4).
        // 画面の外になっていれば, カーソルのモニターの中央に同じ大きさで出し直す(C7).
        private void OnRegionChosen(object sender, SavedRegion region)
        {
            if (capturing || closed) return;
            previewing = false; // 下見していた範囲に決める
            Rectangle r = Selection.InitialFrame(monitors, region.Bounds, Cursor.Position, defaultFrameSize);
            selection.SelectedHandle = FrameHandle.None;
            selection.SetNamedFrame(r, region.Name);
        }

        // 一覧の名前にカーソルが乗った: 枠ごとその範囲へ動かして見せる. 離れたら(region == null)元の枠に戻す.
        // 下見中にEnterを押すと, 見えている範囲を撮る.
        private void OnRegionHovered(object sender, SavedRegion region)
        {
            if (capturing || closed) return;
            if (region == null)
            {
                EndPreview();
                return;
            }
            if (!previewing)
            {
                frameBeforePreview = selection.Frame;
                nameBeforePreview = selection.RegionName;
                previewing = true;
            }
            Rectangle r = Selection.InitialFrame(monitors, region.Bounds, Cursor.Position, defaultFrameSize);
            selection.SetNamedFrame(r, region.Name);
        }

        private void EndPreview()
        {
            if (!previewing || capturing || closed) return;
            previewing = false;
            selection.SetNamedFrame(frameBeforePreview, nameBeforePreview);
        }

        // 一覧の名前をダブルクリック: その場で名前を変える(A5を変更. regions.jsonの手での編集もこれまでどおりできる).
        private void OnRegionEditRequested(object sender, SavedRegion region)
        {
            if (capturing || closed) return;
            EndPreview();
            if (!CheckCanSave()) return;
            if (editor != null) editor.Close();
            OverlayForm owner = (OverlayForm)sender;
            // チップの幅と色は, 入力を始める前と同じにする
            Rectangle area;
            using (Font f = Theme.Body(owner.ChipFontPx))
            {
                area = owner.BeginEdit(region, OverlayForm.MeasureNameWidth(region.Name, f, owner.DpiScale));
            }
            Color back, fore;
            owner.GetEditColors(out back, out fore);
            InlineNameEditor ed = new InlineNameEditor(region.Name, owner.ChipFontPx, owner.DpiScale, back, fore,
                delegate(string text) { return ApplyRename(region, text); },
                delegate(string message) { owner.SetEditError(message); });
            if (area.IsEmpty)
            {
                owner.EndEdit();
                ed.Dispose();
                return;
            }
            ed.PlaceAt(area);
            // 文字の長さに合わせてチップを伸び縮みさせ, 入力欄を置き直す
            ed.TextWidthChanged += delegate { ed.PlaceAt(owner.UpdateEditWidth(ed.TextWidth)); };
            ed.FormClosed += delegate
            {
                if (editor == ed) editor = null; // 閉じた画面は自動で破棄される
                owner.EndEdit();
                if (!closed) owner.Activate();
            };
            editor = ed;
            ed.Show(owner);
        }

        // 名前を変える. 成功ならnull, だめならその理由
        private string ApplyRename(SavedRegion region, string text)
        {
            string name = (text ?? "").Trim();
            if (name.Length == 0) return "名前を入力してください.";
            if (name == region.Name || !store.Regions.Contains(region)) return null;
            foreach (SavedRegion r in store.Regions)
            {
                if (r != region && r.Name == name) return "「" + name + "」はほかの範囲で使っています.";
            }
            string oldName = region.Name;
            region.Name = name;
            try
            {
                store.Save(RegionStore.DefaultPath);
            }
            catch (Exception ex)
            {
                region.Name = oldName;
                return "保存できませんでした: " + ex.Message;
            }
            if (selection.RegionName == oldName) selection.NameCurrent(name); // 呼び出し中なら, 撮ったときのファイル名も新しい名前に
            foreach (OverlayForm f in forms) f.RefreshRegions();
            return null;
        }

        // 一覧の名前をドラッグで並べ替えた. 数字キーの番号も並び順に合わせて1, 2, 3...になる.
        private void OnRegionMoved(object sender, int from, int to)
        {
            if (capturing || closed) return;
            if (!CheckCanSave()) return;
            if (from < 0 || from >= store.Regions.Count || to < 0 || to >= store.Regions.Count) return;
            SavedRegion r = store.Regions[from];
            store.Regions.RemoveAt(from);
            store.Regions.Insert(to, r);
            if (!TrySaveStore())
            {
                store.Regions.RemoveAt(to);
                store.Regions.Insert(from, r);
            }
            foreach (OverlayForm f in forms) f.RefreshRegions();
        }

        // 一覧の名前の右端の×: 確認してから削除する.
        private void OnRegionDeleteRequested(object sender, SavedRegion region)
        {
            if (capturing || closed) return;
            EndPreview();
            Form owner = (Form)sender;
            owner.BeginInvoke((MethodInvoker)delegate { DeleteRegion(owner, region); });
        }

        private void DeleteRegion(Form owner, SavedRegion region)
        {
            if (capturing || closed || !store.Regions.Contains(region)) return;
            if (!CheckCanSave()) return;
            bool ok = Notice.Confirm(owner, "「" + region.Name + "」を削除しますか?",
                "保存した範囲の一覧から消します. これまでに撮った画像は消えません.", "削除");
            if (!ok) { owner.Activate(); return; }
            int index = store.Regions.IndexOf(region);
            store.Regions.RemoveAt(index);
            if (!TrySaveStore())
            {
                store.Regions.Insert(index, region);
            }
            else if (selection.RegionName == region.Name)
            {
                selection.NameCurrent(null); // 枠はそのまま, 名前のない範囲にする
            }
            foreach (OverlayForm f in forms) f.RefreshRegions();
            owner.Activate();
        }

        private bool CheckCanSave()
        {
            if (store.CanSave) return true;
            Notice.Show("範囲を保存できません", "範囲の保存ファイル(regions.json)を読めなかったので, 上書きしないようにしています. ファイルを直してから, 選択画面を開き直してください.\r\n\r\n" + RegionStore.DefaultPath, NoticeKind.Warning);
            return false;
        }

        private bool TrySaveStore()
        {
            try
            {
                store.Save(RegionStore.DefaultPath);
                return true;
            }
            catch (Exception ex)
            {
                Notice.Show("範囲を保存できませんでした", ex.Message, NoticeKind.Warning);
                return false;
            }
        }

        // Ctrl+S: 今の枠に名前を付けて保存する(A3). 同じ名前があれば, 名前の画面で上書きを確かめる(7.1).
        private void OnSaveRequested(object sender, EventArgs e)
        {
            if (capturing || closed) return;
            EndPreview();
            if (!CheckCanSave()) return;

            List<string> names = new List<string>();
            foreach (SavedRegion r in store.Regions) names.Add(r.Name);
            string name;
            using (NameForm form = new NameForm(names, selection.RegionName))
            {
                if (form.ShowDialog((IWin32Window)sender) != DialogResult.OK) return;
                name = form.ResultName;
            }

            Rectangle frame = selection.Frame;
            SavedRegion existing = null;
            foreach (SavedRegion r in store.Regions)
            {
                if (r.Name == name) { existing = r; break; }
            }
            Rectangle oldBounds = existing != null ? existing.Bounds : Rectangle.Empty;
            if (existing != null) existing.Bounds = frame; // 上書きしても一覧の並び順は変えない
            else
            {
                SavedRegion added = new SavedRegion();
                added.Name = name;
                added.Bounds = frame;
                store.Regions.Add(added);
            }

            if (!TrySaveStore())
            {
                // 保存できなかったら, 一覧も元に戻す.
                if (existing != null) existing.Bounds = oldBounds;
                else store.Regions.RemoveAt(store.Regions.Count - 1);
                return;
            }

            selection.NameCurrent(name); // そのまま撮れば, ファイル名に名前が付く(D2)
            foreach (OverlayForm f in forms) f.RefreshRegions();
            ((Form)sender).Activate();
        }

        // Enter, E: 今の枠を撮る. 名前付きの範囲を動かさずに撮るときだけ名前がある(D5)
        private void OnCaptureRequested(object sender, EventArgs e)
        {
            BeginCapture((Control)sender, selection.Frame, selection.RegionName, false, true);
        }

        private void OnEditRequested(object sender, EventArgs e)
        {
            BeginCapture((Control)sender, selection.Frame, selection.RegionName, true, true);
        }

        // F, Shift+F: カーソルのあるモニター全体を撮る(H2). 前回の範囲は変えない
        private void CaptureFullScreen(OverlayForm sender, bool edit)
        {
            Rectangle m = sender.MonitorBounds;
            Point cursor = Cursor.Position;
            foreach (Rectangle r in monitors)
            {
                if (r.Contains(cursor)) { m = r; break; }
            }
            BeginCapture(sender, m, null, edit, false);
        }

        // edit: 撮った画像に書き込む画面を開く. saveLast: 前回の範囲として残す
        private void BeginCapture(Control sender, Rectangle frame, string name, bool edit, bool saveLast)
        {
            if (capturing || closed) return;
            capturing = true;

            if (!live)
            {
                // 止めた絵から切り出す. 見えている絵そのものなので, 選択画面が消えるのを待たなくてよい.
                // キー処理の途中でフォームを破棄しないよう, 処理が終わってから行う.
                sender.BeginInvoke((MethodInvoker)delegate { CaptureAndClose(frame, name, edit, saveLast); });
                return;
            }

            // 動いている画面では, 選択画面を消してから撮る(キーを押した時点の画面). 消えた部分が描き直されるのを少し待つ.
            foreach (OverlayForm f in forms) f.Hide();
            Timer timer = new Timer();
            timer.Interval = 150;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                CaptureAndClose(frame, name, edit, saveLast);
            };
            timer.Start();
        }

        private void CaptureAndClose(Rectangle frame, string name, bool edit, bool saveLast)
        {
            if (closed) return;
            foreach (OverlayForm f in forms) f.Hide();
            try
            {
                Capture(frame, name, edit, saveLast);
            }
            catch (Exception ex)
            {
                Program.LogError("Capture", ex);
                Notice.Show("撮影できませんでした", ex.Message, NoticeKind.Error);
            }
            CloseAll();
        }

        private void Capture(Rectangle frame, string regionName, bool edit, bool saveLast)
        {
            DateTime now = live ? DateTime.Now : frozen.Time;
            Bitmap bmp = live ? CaptureOutput.CaptureScreen(frame) : CropFrozen(frame);
            try
            {
                // D1: ファイルとクリップボードの両方に出す. 片方が失敗しても, もう片方には出す. D4: 成功の通知は出さない.
                // ファイル名は<範囲名>_..., 名前がなければscreenshot_...(D2, D5).
                // 書き込むときは, 隠す前の画像がクリップボードの履歴に残らないよう, 書き終えたときに出す.
                Bitmap image = bmp;
                Action copy = null;
                if (!edit) copy = delegate { CaptureOutput.CopyToClipboard(image); };
                OutputSteps result = OutputSteps.Run(
                    delegate { return CaptureOutput.SavePng(image, saveDir, regionName, now); }, copy);
                if (saveLast && result.AnyDone) SaveLast(frame);
                if (result.Heading != null) Notice.Show(result.Heading, result.Body, NoticeKind.Error);
                if (edit && result.Path != null)
                {
                    Rectangle monitor = MonitorOf(frame);
                    MarkForm form = new MarkForm(bmp, frame, monitor, Program.GetMonitorDpi(monitor) / 96f, result.Path);
                    bmp = null; // 書き込みの画面が破棄する
                    form.Show();
                }
            }
            finally
            {
                if (bmp != null) bmp.Dispose();
            }
        }

        private Rectangle MonitorOf(Rectangle frame)
        {
            foreach (Rectangle m in monitors)
            {
                if (m.Contains(frame)) return m;
            }
            return Screen.FromRectangle(frame).Bounds;
        }

        private Bitmap CropFrozen(Rectangle frame)
        {
            Bitmap bmp = frozen.Crop(frame);
            if (bmp == null) throw new InvalidOperationException("範囲がモニターに収まっていません.");
            return bmp;
        }

        // 撮った範囲を「前回の範囲」として残す(A2). Escで閉じたときは残さない(A8).
        private void SaveLast(Rectangle frame)
        {
            if (!store.CanSave) return; // 壊れたファイルは上書きしない(読み込み時に知らせ済み)
            store.Last = frame;
            try
            {
                store.Save(RegionStore.DefaultPath);
            }
            catch (Exception ex)
            {
                Notice.Show("前回の範囲を保存できませんでした", ex.Message, NoticeKind.Warning);
            }
        }

        private void CloseAll()
        {
            if (closed) return; // Escの連打などで2回呼ばれても安全にする
            closed = true;
            if (foregroundTimer != null) foregroundTimer.Dispose();
            foreach (OverlayForm f in forms)
            {
                f.AllowClose = true;
                f.Close();
                f.Dispose();
            }
            forms.Clear();
            if (frozen != null) frozen.Dispose();
            if (editor != null)
            {
                editor.Close();
                editor = null;
            }
            EventHandler h = Finished;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
