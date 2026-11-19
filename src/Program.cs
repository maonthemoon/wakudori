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
                    MessageBox.Show("Wakudoriはすでに起動しています. タスクトレイのアイコンかホットキーから使ってください.",
                        "Wakudori", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                string error;
                AppSettings settings = AppSettings.Load(out error);
                if (error != null)
                {
                    MessageBox.Show(error + "\r\n\r\n既定の設定で続けます. このファイルは上書きしません.", "Wakudori", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                try
                {
                    Application.Run(new TrayApp(settings));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString(), "Wakudori - エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        private bool capturing;
        private bool closed;

        public event EventHandler Finished;

        public CaptureSession(string saveDir, Size defaultFrameSize)
        {
            this.saveDir = saveDir;

            Screen[] screens = Screen.AllScreens;
            Rectangle[] monitors = new Rectangle[screens.Length];
            for (int i = 0; i < screens.Length; i++) monitors[i] = screens[i].Bounds;

            // 前回の範囲(A2)を読む. 開くたびに読み直す(A5). 読めないときは, 前回の範囲なしで開く(7.1).
            string error;
            store = RegionStore.Load(RegionStore.DefaultPath, out error);
            if (error != null)
            {
                MessageBox.Show(error, "Wakudori", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // 前回の範囲がなければ, マウスカーソルがあるモニターの中央に出す(C5).
            // 前回の範囲が画面の外になっていれば, カーソルのモニターの中央に同じ大きさで出し直す(C7).
            Point cursor = Cursor.Position;
            Rectangle initial = Selection.InitialFrame(monitors, store.Last, cursor, defaultFrameSize);
            selection = new Selection(monitors, initial);

            for (int i = 0; i < screens.Length; i++)
            {
                bool cursorHere = monitors[i].Contains(cursor);
                OverlayForm f = new OverlayForm(i, monitors[i], Program.GetMonitorDpi(monitors[i]), selection, cursorHere);
                f.CaptureRequested += OnCaptureRequested;
                f.CancelRequested += OnCancelRequested;
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

        private void OnCaptureRequested(object sender, EventArgs e)
        {
            if (capturing || closed) return;
            capturing = true;

            Rectangle frame = selection.Frame;

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
                    Capture(frame);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("撮影または保存に失敗しました.\r\n\r\n" + ex.Message, "Wakudori", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                CloseAll();
            };
            timer.Start();
        }

        private void Capture(Rectangle frame)
        {
            DateTime now = DateTime.Now;
            using (Bitmap bmp = CaptureOutput.CaptureScreen(frame))
            {
                // D1: クリップボードとファイルの両方に出す. D4: 通知は出さない.
                // 名前付きの範囲(8章の5)はまだないので, ファイル名はscreenshot_...になる(D2).
                CaptureOutput.CopyToClipboard(bmp);
                CaptureOutput.SavePng(bmp, saveDir, null, now);
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
                MessageBox.Show("前回の範囲を保存できませんでした.\r\n\r\n" + ex.Message, "Wakudori", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            EventHandler h = Finished;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
