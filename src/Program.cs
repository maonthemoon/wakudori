using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Wakudori
{
    // 今の段階(SPEC.mdの8章の2と3): 起動すると選択画面を開き, 枠を調整してEnterで撮る.
    // 撮った画像はクリップボードとPNGファイルの両方に出し, 終了する. 常駐とホットキーは8章の4で作る.
    internal static class Program
    {
        private static readonly Size DefaultFrameSize = new Size(800, 600);

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

            string error;
            AppSettings settings = AppSettings.Load(out error);
            if (error != null)
            {
                MessageBox.Show(error + "\r\n\r\n既定の設定で続けます.", "Wakudori", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            try
            {
                Application.Run(new CaptureSession(settings, DefaultFrameSize));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Wakudori - エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            return 0;
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

    // 選択画面を1回開いて, 撮るか閉じるまでを受け持つ.
    internal sealed class CaptureSession : ApplicationContext
    {
        private readonly AppSettings settings;
        private readonly List<OverlayForm> forms = new List<OverlayForm>();
        private readonly Selection selection;
        private bool capturing;
        private bool closed;

        public CaptureSession(AppSettings settings, Size defaultFrameSize)
        {
            this.settings = settings;

            Screen[] screens = Screen.AllScreens;
            Rectangle[] monitors = new Rectangle[screens.Length];
            for (int i = 0; i < screens.Length; i++) monitors[i] = screens[i].Bounds;

            // 前回の範囲(A2)は8章の5で読み込む. 今はマウスカーソルがあるモニターの中央に出す(C5).
            Point cursor = Cursor.Position;
            Rectangle initial = Selection.InitialFrame(monitors, null, cursor, defaultFrameSize);
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

            // キーボードを受けるため, マウスカーソルがあるモニターのフォームを前面にする.
            foreach (OverlayForm f in forms)
            {
                if (f.MonitorBounds.Contains(cursor))
                {
                    f.Activate();
                    break;
                }
            }
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
                CaptureOutput.SavePng(bmp, settings.SaveDir, null, now);
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
            ExitThread();
        }
    }
}
