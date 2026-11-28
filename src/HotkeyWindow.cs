using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Wakudori
{
    // ホットキーとWindowsのセッション終了を受け取る見えないウィンドウ.
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int HotkeyId = 1;
        private const int WM_ENDSESSION = 0x0016;
        private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
        private bool registered;

        public event EventHandler Pressed;
        public event EventHandler SessionEnded;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams { Caption = "Wakudori HotkeyWindow" });
        }

        public bool IsRegistered { get { return registered; } }

        // 登録する(前のキーは外す). 成功ならnull, 失敗ならその理由.
        public string Register(HotkeySpec h)
        {
            Unregister();
            uint mods = (uint)h.Modifiers | NativeMethods.MOD_NOREPEAT;
            if (!NativeMethods.RegisterHotKey(Handle, HotkeyId, mods, (uint)h.Vk))
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_HOTKEY_ALREADY_REGISTERED)
                {
                    string s = HotkeyText.Format(h) + "は, 他のアプリかWindowsが使っているため登録できません. 別のキーを押してください.";
                    if (h.Vk == 0x2C) s += "\r\n(PrintScreenは, Windowsの設定「PrintScreenキーを使用して画面キャプチャを開く」がオンだと使えません.)";
                    return s;
                }
                return HotkeyText.Format(h) + "を登録できませんでした(エラー" + err + "). 別のキーを押してください.";
            }
            registered = true;
            return null;
        }

        public void Unregister()
        {
            if (!registered) return;
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            registered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt64() == HotkeyId)
            {
                EventHandler p = Pressed;
                if (p != null) p(this, EventArgs.Empty);
                return;
            }
            if (m.Msg == WM_ENDSESSION && m.WParam != IntPtr.Zero)
            {
                EventHandler ended = SessionEnded;
                if (ended != null) ended(this, EventArgs.Empty);
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Unregister();
            DestroyHandle();
        }
    }
}
