using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Wakudori
{
    // DPI確認用のプロトタイプ(SPEC.mdの8章の1).
    // 全モニターを覆い, 各モニターの中央に400x300の点線の枠を出す. Enterで全モニターの枠を撮る.
    internal static class Program
    {
        private static readonly Size FrameSize = new Size(400, 300);

        private static string awarenessNote = "";

        [STAThread]
        private static int Main()
        {
            // マニフェストでPer-Monitor V2にしている. 念のためAPIでも指定する(すでに指定済みなら失敗するのが正常).
            try
            {
                bool ok = NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
                awarenessNote = ok
                    ? "SetProcessDpiAwarenessContext: succeeded (manifest did not set awareness)"
                    : "SetProcessDpiAwarenessContext: failed, error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + " (5 = already set by manifest, expected)";
            }
            catch (EntryPointNotFoundException)
            {
                awarenessNote = "SetProcessDpiAwarenessContext: not available on this OS";
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                Application.Run(new PrototypeContext(FrameSize));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Wakudori - error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            return 0;
        }

        public static string AwarenessNote { get { return awarenessNote; } }

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

        public static string DescribeAwareness()
        {
            try
            {
                IntPtr ctx = NativeMethods.GetThreadDpiAwarenessContext();
                int awareness = NativeMethods.GetAwarenessFromDpiAwarenessContext(ctx);
                bool v2 = NativeMethods.AreDpiAwarenessContextsEqual(ctx, NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
                string name;
                switch (awareness)
                {
                    case 0: name = "Unaware"; break;
                    case 1: name = "SystemAware"; break;
                    case 2: name = "PerMonitorAware"; break;
                    default: name = "Invalid(" + awareness + ")"; break;
                }
                return name + (v2 ? " (PerMonitorV2)" : " (not V2)");
            }
            catch (EntryPointNotFoundException)
            {
                return "unknown (API not available)";
            }
        }
    }

    internal sealed class PrototypeContext : ApplicationContext
    {
        private readonly List<OverlayForm> forms = new List<OverlayForm>();
        private bool capturing;
        private bool closed;

        public PrototypeContext(Size frameSize)
        {
            Screen[] screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                uint dpi = Program.GetMonitorDpi(screens[i].Bounds);
                OverlayForm f = new OverlayForm(i, screens[i], dpi, frameSize);
                f.CaptureRequested += OnCaptureRequested;
                f.CancelRequested += OnCancelRequested;
                forms.Add(f);
            }

            foreach (OverlayForm f in forms) f.Show();

            // キーボードを受けるため, マウスカーソルがあるモニターのフォームを前面にする.
            Point cursor = Cursor.Position;
            foreach (OverlayForm f in forms)
            {
                if (f.MonitorScreen.Bounds.Contains(cursor))
                {
                    f.Activate();
                    break;
                }
            }
        }

        private void OnCancelRequested(object sender, EventArgs e)
        {
            // KeyDownの処理中にフォームを破棄しないよう, 処理が終わってから閉じる.
            ((Control)sender).BeginInvoke((MethodInvoker)CloseAll);
        }

        private void OnCaptureRequested(object sender, EventArgs e)
        {
            if (capturing) return;
            capturing = true;

            // 選択画面を消してから撮る. 消えた画面が描き直されるのを少し待つ.
            foreach (OverlayForm f in forms) f.Hide();
            Timer timer = new Timer();
            timer.Interval = 200;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                try
                {
                    CaptureAll();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString(), "Wakudori - capture error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                CloseAll();
            };
            timer.Start();
        }

        private void CaptureAll()
        {
            DateTime now = DateTime.Now;
            string stamp = now.ToString("yyyyMMdd_HHmmss");
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "captures");
            Directory.CreateDirectory(dir);

            StringBuilder report = new StringBuilder();
            report.AppendLine("Wakudori DPI prototype report");
            report.AppendLine("Time: " + now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("OS: " + Environment.OSVersion.VersionString + (Environment.Is64BitProcess ? " (64-bit process)" : " (32-bit process)"));
            report.AppendLine("CLR: " + Environment.Version);
            report.AppendLine("DPI awareness: " + Program.DescribeAwareness());
            report.AppendLine(Program.AwarenessNote);
            report.AppendLine("Virtual screen: " + FormatRect(SystemInformation.VirtualScreen));
            report.AppendLine();

            foreach (OverlayForm f in forms)
            {
                Screen s = f.MonitorScreen;
                Rectangle r = f.Frame;
                string file = Path.Combine(dir, string.Format("monitor{0}_{1}.png", f.MonitorIndex + 1, stamp));

                int pngW, pngH;
                using (Bitmap bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size, CopyPixelOperation.SourceCopy);
                    }
                    bmp.Save(file, ImageFormat.Png);
                    pngW = bmp.Width;
                    pngH = bmp.Height;
                }

                report.AppendLine(string.Format("[Monitor {0}]{1}", f.MonitorIndex + 1, s.Primary ? " primary" : ""));
                report.AppendLine("  Device:      " + s.DeviceName);
                report.AppendLine("  Bounds:      " + FormatRect(s.Bounds));
                report.AppendLine("  WorkingArea: " + FormatRect(s.WorkingArea));
                report.AppendLine(string.Format("  DPI:         {0} ({1}%)", f.Dpi, (int)Math.Round(f.Dpi * 100.0 / 96.0)));
                report.AppendLine("  Frame:       " + FormatRect(r));
                report.AppendLine(string.Format("  PNG:         {0} ({1}x{2})", Path.GetFileName(file), pngW, pngH));
                report.AppendLine();
            }

            File.WriteAllText(Path.Combine(dir, "dpi-report_" + stamp + ".txt"), report.ToString(), new UTF8Encoding(false));
            Process.Start("explorer.exe", "\"" + dir + "\"");
        }

        private static string FormatRect(Rectangle r)
        {
            return string.Format("x={0} y={1} w={2} h={3}", r.X, r.Y, r.Width, r.Height);
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
