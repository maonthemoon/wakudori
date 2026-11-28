using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Wakudori
{
    // ファイル名の組み立て(UIに依存しない)
    internal static class FileNaming
    {
        private const string TimestampFormat = "yyyyMMdd_HHmmss";

        // 日時の後に範囲名を付ける. 範囲名が空なら日時だけ
        public static string BuildFileName(string regionName, DateTime time)
        {
            string name = Sanitize(regionName);
            string stamp = time.ToString(TimestampFormat, CultureInfo.InvariantCulture);
            return stamp + (name.Length == 0 ? "" : "_" + name) + ".png";
        }

        public static string BuildWatchFolderPath(string dir, DateTime startedAt, Func<string, bool> exists)
        {
            return UniquePath(dir, "見張り_" + startedAt.ToString(TimestampFormat, CultureInfo.InvariantCulture), exists);
        }

        public static string BuildWatchFileName(int number)
        {
            return number.ToString("D3", CultureInfo.InvariantCulture) + ".png";
        }

        private static string Sanitize(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c < ' ' || c == '\u007f' || "\\/:*?\"<>|".IndexOf(c) >= 0) sb.Append('_');
                else sb.Append(c);
            }
            // 置換後の前後の空白・末尾のピリオドはWindowsで問題になるので除く
            return sb.ToString().Trim().TrimEnd('.');
        }

        // 書き込んだ画像の名前: <元の名前>_edit.png
        public static string BuildEditName(string originalPath)
        {
            return Path.GetFileNameWithoutExtension(originalPath) + "_edit.png";
        }

        // 存在しないフルパスを返す: base.png, base_2.png, base_3.png ...
        public static string UniquePath(string dir, string fileName, Func<string, bool> exists)
        {
            string path = Path.Combine(dir, fileName);
            if (!exists(path)) return path;
            string ext = Path.GetExtension(fileName);
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            for (int i = 2; ; i++)
            {
                path = Path.Combine(dir, baseName + "_" + i + ext);
                if (!exists(path)) return path;
            }
        }
    }
}
