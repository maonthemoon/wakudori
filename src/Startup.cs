using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Wakudori
{
    // PCの起動時に起動する設定(G3). HKCUのRunキーに, 引用符で囲んだexeのフルパスを書く(管理者権限は不要).
    // 失敗したとき(ポリシーで禁止など)は例外を出さず, 理由の文を返す.
    internal static class Startup
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Wakudori";

        private static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\""; }
        }

        // 登録されているか. 読めないときはfalse
        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key != null && key.GetValue(ValueName) != null;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        // 登録する(enable=true)か外す. 成功ならnull, 失敗なら理由の文
        public static string SetEnabled(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) return "レジストリのキーを開けませんでした.";
                    if (enable) key.SetValue(ValueName, Command, RegistryValueKind.String);
                    else key.DeleteValue(ValueName, false);
                }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // 登録されていて, 別の場所のexeを指していたら(exeを移したとき), 今のexeに直す. 成功か直す必要がなければnull
        public static string FixPathIfMoved()
        {
            string current;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return null;
                    current = key.GetValue(ValueName) as string;
                }
            }
            catch (Exception)
            {
                return null; // 読めないなら, 登録されていないのと同じに扱う
            }
            if (current == null || string.Equals(current.Trim(), Command, StringComparison.OrdinalIgnoreCase)) return null;
            return SetEnabled(true);
        }
    }
}
