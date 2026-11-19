using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace Wakudori
{
    // settings.json の内容
    internal sealed class AppSettings
    {
        public string Hotkey;   // 未設定ならnull
        public string SaveDir;  // 空・未指定なら既定の保存先

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
                return d2;
            }
            return s;
        }
    }
}
