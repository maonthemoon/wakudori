using System;
using System.IO;
using System.Text;

namespace Wakudori
{
    // 想定外のエラーの記録(%APPDATA%\Wakudori\error.log). このPCに残すだけで, どこにも送らない. UIに依存しない.
    internal static class ErrorLog
    {
        private const long MaxBytes = 256 * 1024; // 超えたら error.old.log に移して, 新しく書き始める

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wakudori"),
                    "error.log");
            }
        }

        // 書けたらtrue. 記録に失敗しても例外は出さない
        public static bool Write(string path, string version, string where, Exception ex, DateTime now)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    string old = Path.Combine(Path.GetDirectoryName(path), "error.old.log");
                    File.Delete(old);
                    File.Move(path, old);
                }
                string text = now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
                    + " Wakudori " + version + " (" + Environment.OSVersion + ") " + where + "\r\n"
                    + (ex != null ? ex.ToString() : "(例外の情報なし)") + "\r\n\r\n";
                File.AppendAllText(path, text, new UTF8Encoding(false));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
