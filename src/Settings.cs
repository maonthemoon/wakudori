using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Wakudori
{
    // settings.json の内容
    internal sealed class AppSettings
    {
        public string Hotkey;   // 未設定ならnull
        public string SaveDir;  // 空・未指定なら既定の保存先
        public bool CanSave = true; // 読めなかったファイルは上書きしない
        public int CaptureVk = HotkeyText.DefaultCaptureVk; // 撮影キー(H11). Enterと同じく撮る. 書き込みの画面では完了
        public string CaptureKeyProblem; // captureKeyが使えないキーだったときの知らせ. なければnull
        public const int DefaultAutoDeleteDays = 30;
        public const int MinAutoDeleteDays = 1;
        public const int MaxAutoDeleteDays = 365; // 最大1年(H18)
        public bool AutoDelete = false;
        public int AutoDeleteDays = DefaultAutoDeleteDays;
        public string AutoDeleteProblem;
        public string AutoDeleteDaysProblem;

        public static string ConfigDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wakudori"); }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(ConfigDir, "settings.json"); }
        }

        public static string DefaultSaveDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"); }
        }

        // 読み込む. ファイルがなければ既定値. 壊れていれば既定値とerror(ファイルは触らない)
        public static AppSettings Load(out string error)
        {
            error = null;
            AppSettings s = new AppSettings();
            s.SaveDir = DefaultSaveDir;
            string path = SettingsPath;
            try
            {
                Dictionary<string, object> d = ReadValues(path, new JavaScriptSerializer());
                if (d == null) return s;
                object v;
                if (d.TryGetValue("hotkey", out v) && v is string && ((string)v).Trim().Length > 0)
                    s.Hotkey = ((string)v).Trim();
                if (d.TryGetValue("saveDir", out v) && v is string && ((string)v).Trim().Length > 0)
                    s.SaveDir = Environment.ExpandEnvironmentVariables(((string)v).Trim());
                // 空・null・未指定なら既定(A). 使えないキーなら既定にして知らせる
                if (d.TryGetValue("captureKey", out v) && v != null && !(v is string && ((string)v).Trim().Length == 0))
                {
                    int vk;
                    if (v is string && HotkeyText.TryParseCaptureKey((string)v, out vk)) s.CaptureVk = vk;
                    else s.CaptureKeyProblem = "設定ファイルの撮影キー(captureKey)「" + Convert.ToString(v) + "」は使えません. Aを使います.\n"
                        + "使えるのは英字1文字です(EとFは除く): " + path;
                }
                if (d.TryGetValue("autoDelete", out v))
                {
                    if (v is bool) s.AutoDelete = (bool)v;
                    else s.AutoDeleteProblem = "設定ファイルのautoDeleteにはtrueかfalseを指定してください. 自動整理はオフにします.\n" + path;
                }
                if (d.TryGetValue("autoDeleteDays", out v))
                {
                    if (v is int && (int)v >= MinAutoDeleteDays && (int)v <= MaxAutoDeleteDays) s.AutoDeleteDays = (int)v;
                    else s.AutoDeleteDaysProblem = "設定ファイルのautoDeleteDaysには1〜365の整数を指定してください. "
                        + DefaultAutoDeleteDays + "日を使います.\n" + path;
                }
            }
            catch (Exception ex)
            {
                error = "設定ファイルを読み込めませんでした(既定値を使います): " + path + "\n" + ex.Message;
                AppSettings d2 = new AppSettings();
                d2.SaveDir = DefaultSaveDir;
                d2.CanSave = false;
                return d2;
            }
            return s;
        }

        public void SaveHotkey(string hotkey)
        {
            SaveValues(new Dictionary<string, object> { { "hotkey", hotkey } });
            Hotkey = hotkey;
        }

        // 書く直前に読み直し, 手で足した項目も残す. 一時ファイルを書き終えてから置き換える.
        public void SaveValues(IDictionary<string, object> changes)
        {
            string path = SettingsPath;
            if (!CanSave) throw new InvalidOperationException("設定ファイルを読めなかったので, 保存できません: " + path);
            JavaScriptSerializer ser = new JavaScriptSerializer();
            Dictionary<string, object> items = ReadValues(path, ser);
            bool existed = items != null;
            if (!existed) items = new Dictionary<string, object>();
            SettingsValues.ApplyChanges(items, changes);

            StringBuilder sb = new StringBuilder("{\r\n");
            int remaining = items.Count;
            foreach (KeyValuePair<string, object> item in items)
            {
                sb.Append("  " + ser.Serialize(item.Key) + ": " + ser.Serialize(item.Value));
                sb.Append(--remaining > 0 ? ",\r\n" : "\r\n");
            }
            sb.Append("}\r\n");

            Directory.CreateDirectory(ConfigDir);
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (existed) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch
            {
                try { File.Delete(tmp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw;
            }
        }

        private static Dictionary<string, object> ReadValues(string path, JavaScriptSerializer ser)
        {
            string json;
            try { json = File.ReadAllText(path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            var values = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (values == null) throw new InvalidDataException("JSONのトップがオブジェクトではありません: " + path);
            return values;
        }
    }
}
