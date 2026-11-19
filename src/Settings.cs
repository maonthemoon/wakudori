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
