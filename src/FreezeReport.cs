using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Wakudori
{
    // 試作用: 止めた画面の計測を%APPDATA%\Wakudori\freeze-report.txtに足していく. 確認が済んだら消す.
    // 画面の中身, ウィンドウの名前, ファイル名などは書かない.
    internal sealed class FreezeReport
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private readonly StringBuilder marks = new StringBuilder();

        public string Menu = "-";
        public string Foreground = "-";
        public string FirstKey = "-";
        public string Captured = "-";
        public string Error;
        public int Toggles;

        public long ElapsedMs { get { return watch.ElapsedMilliseconds; } }

        public void Mark(string name)
        {
            if (marks.Length > 0) marks.Append(", ");
            marks.Append(name + " " + watch.ElapsedMilliseconds + " ms");
        }

        public void Write(IList<OverlayForm> forms)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("---- " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " ----");
                sb.AppendLine("open: " + marks);
                sb.AppendLine("menu: " + Menu);
                sb.AppendLine("foreground: " + Foreground);
                sb.AppendLine("first key: " + FirstKey);
                sb.AppendLine("captured: " + Captured + ", space: " + Toggles);
                foreach (OverlayForm f in forms)
                {
                    double max = f.PresentMaxMs;
                    double avg = f.PresentCount > 0 ? f.PresentTotalMs / f.PresentCount : 0;
                    sb.AppendLine("monitor " + f.MonitorBounds.Width + "x" + f.MonitorBounds.Height + " scale " + f.DpiScale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                        + ": draws " + f.PresentCount + ", avg " + avg.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                        + " ms, max " + max.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms");
                }
                if (Error != null) sb.AppendLine("error: " + Error);
                Directory.CreateDirectory(AppSettings.ConfigDir);
                File.AppendAllText(Path.Combine(AppSettings.ConfigDir, "freeze-report.txt"), sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // 計測なので, 書けなくても撮影は止めない.
            }
        }
    }
}
