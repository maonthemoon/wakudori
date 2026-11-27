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
                if (!File.Exists(path)) return s;
                string json = File.ReadAllText(path);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                Dictionary<string, object> d = ser.DeserializeObject(json) as Dictionary<string, object>;
                if (d == null) throw new InvalidDataException("JSONのトップがオブジェクトではありません.");
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
                    if (v is int && (int)v >= 1 && (int)v <= 3650) s.AutoDeleteDays = (int)v;
                    else s.AutoDeleteDaysProblem = "設定ファイルのautoDeleteDaysには1〜3650の整数を指定してください. "
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

        // ホットキーだけを書き換えて保存する. ほかの項目(手で書いたものを含む)はそのまま残す.
        // 一時ファイルに書いてから置き換える.
        public void SaveHotkey(string hotkey)
        {
            if (!CanSave) throw new InvalidOperationException("設定ファイルが読めなかったので, 上書きしません. 直してから登録し直してください: " + SettingsPath);
            string path = SettingsPath;
            Directory.CreateDirectory(ConfigDir);
            JavaScriptSerializer ser = new JavaScriptSerializer();

            List<KeyValuePair<string, object>> items = new List<KeyValuePair<string, object>>();
            if (File.Exists(path))
            {
                Dictionary<string, object> d = ser.DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
                if (d == null) throw new InvalidDataException("設定ファイルのJSONのトップがオブジェクトではありません: " + path);
                foreach (KeyValuePair<string, object> kv in d) items.Add(kv);
            }
            bool found = false;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Key == "hotkey")
                {
                    items[i] = new KeyValuePair<string, object>("hotkey", hotkey);
                    found = true;
                }
            }
            if (!found) items.Insert(0, new KeyValuePair<string, object>("hotkey", hotkey));

            StringBuilder sb = new StringBuilder("{\r\n");
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append("  " + ser.Serialize(items[i].Key) + ": " + ser.Serialize(items[i].Value));
                sb.Append(i < items.Count - 1 ? ",\r\n" : "\r\n");
            }
            sb.Append("}\r\n");

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
            Hotkey = hotkey;
        }
    }
}
