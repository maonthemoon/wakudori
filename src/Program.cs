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

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    Notice.Show("すでに起動しています", "タスクトレイのアイコンか, 登録したホットキーから使ってください.", NoticeKind.Info);
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
                    Notice.Show("エラーで終了します", ex.ToString(), NoticeKind.Error);
                    return 1;
                }
                return 0;
            }
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
        private ContextMenuStrip regionMenu;

        public event EventHandler Finished;

        public CaptureSession(string saveDir, Size defaultFrameSize)
        {
            this.saveDir = saveDir;
            this.defaultFrameSize = defaultFrameSize;

            Screen[] screens = Screen.AllScreens;
            monitors = new Rectangle[screens.Length];
            for (int i = 0; i < screens.Length; i++) monitors[i] = screens[i].Bounds;

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

            for (int i = 0; i < screens.Length; i++)
            {
                // 保存した範囲の一覧は, 開いたときにマウスカーソルがあるモニターにだけ出す(A7).
                bool listHere = monitors[i].Contains(cursor);
                OverlayForm f = new OverlayForm(i, monitors[i], Program.GetMonitorDpi(monitors[i]), selection, store.Regions, listHere);
                f.CaptureRequested += OnCaptureRequested;
                f.CancelRequested += OnCancelRequested;
                f.SaveRequested += OnSaveRequested;
                f.RegionChosen += OnRegionChosen;
                f.RegionHovered += OnRegionHovered;
                f.RegionMenuRequested += OnRegionMenuRequested;
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

        // 一覧の名前を右クリック: 名前を変える, 削除(A5を変更. regions.jsonの手での編集もこれまでどおりできる).
        private void OnRegionMenuRequested(object sender, SavedRegion region)
        {
            if (capturing || closed) return;
            EndPreview();
            if (regionMenu == null)
            {
                regionMenu = new ContextMenuStrip();
                ToolStripProfessionalRenderer renderer = new ToolStripProfessionalRenderer(new Theme.MenuColors());
                renderer.RoundedEdges = false;
                regionMenu.Renderer = renderer;
                regionMenu.BackColor = Theme.Bg;
                regionMenu.ForeColor = Theme.Text;
            }
            regionMenu.Items.Clear();
            Form owner = (Form)sender;
            regionMenu.Items.Add(new ToolStripMenuItem("名前を変える", null, delegate { owner.BeginInvoke((MethodInvoker)delegate { RenameRegion(owner, region); }); }));
            regionMenu.Items.Add(new ToolStripMenuItem("削除", null, delegate { owner.BeginInvoke((MethodInvoker)delegate { DeleteRegion(owner, region); }); }));
            regionMenu.Show(Cursor.Position);
        }

        private void RenameRegion(Form owner, SavedRegion region)
        {
            if (capturing || closed || !store.Regions.Contains(region)) return;
            if (!CheckCanSave()) return;
            List<string> others = new List<string>();
            foreach (SavedRegion r in store.Regions) if (r != region) others.Add(r.Name);
            string newName;
            using (NameForm form = new NameForm(others, region.Name, true))
            {
                if (form.ShowDialog(owner) != DialogResult.OK) { owner.Activate(); return; }
                newName = form.ResultName;
            }
            string oldName = region.Name;
            region.Name = newName;
            if (!TrySaveStore())
            {
                region.Name = oldName;
            }
            else if (selection.RegionName == oldName)
            {
                selection.NameCurrent(newName); // 呼び出し中の範囲なら, 撮ったときのファイル名も新しい名前にする
            }
            foreach (OverlayForm f in forms) f.RefreshRegions();
            owner.Activate();
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

        private void OnCaptureRequested(object sender, EventArgs e)
        {
            if (capturing || closed) return;
            capturing = true;

            Rectangle frame = selection.Frame;
            string name = selection.RegionName; // 名前付きの範囲を動かさずに撮るときだけ名前がある(D5)

            // 選択画面を消してから撮る(B7: Enterを押した時点の画面). 消えた部分が描き直されるのを少し待つ.
            foreach (OverlayForm f in forms) f.Hide();
            Timer timer = new Timer();
            timer.Interval = 150;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                try
                {
                    Capture(frame, name);
                }
                catch (Exception ex)
                {
                    Notice.Show("撮影または保存に失敗しました", ex.Message, NoticeKind.Error);
                }
                CloseAll();
            };
            timer.Start();
        }

        private void Capture(Rectangle frame, string regionName)
        {
            DateTime now = DateTime.Now;
            using (Bitmap bmp = CaptureOutput.CaptureScreen(frame))
            {
                // D1: クリップボードとファイルの両方に出す. D4: 通知は出さない.
                // ファイル名は<範囲名>_..., 名前がなければscreenshot_...(D2, D5).
                CaptureOutput.CopyToClipboard(bmp);
                CaptureOutput.SavePng(bmp, saveDir, regionName, now);
            }
            SaveLast(frame);
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
            foreach (OverlayForm f in forms)
            {
                f.AllowClose = true;
                f.Close();
                f.Dispose();
            }
            forms.Clear();
            if (regionMenu != null)
            {
                regionMenu.Dispose();
                regionMenu = null;
            }
            EventHandler h = Finished;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
