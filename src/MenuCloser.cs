using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Wakudori
{
    // ほかのアプリで開いているメニューを閉じる.
    // メニューが開いている間は, 選択画面を前面に出せずキー入力が届かないことがあるため(SetForegroundWindowの条件).
    internal static class MenuCloser
    {
        private const int WaitMs = 200;

        // メニューが開いていれば閉じるよう頼み, 閉じるのを少し待つ
        public static void CloseActiveMenu()
        {
            NativeMethods.GUITHREADINFO info;
            if (!TryGetInfo(out info) || !InMenu(info)) return;
            IntPtr owner = info.hwndMenuOwner != IntPtr.Zero ? info.hwndMenuOwner : info.hwndActive;
            if (owner == IntPtr.Zero) return;
            NativeMethods.PostMessage(owner, NativeMethods.WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero);

            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < WaitMs)
            {
                Thread.Sleep(10);
                if (!TryGetInfo(out info) || !InMenu(info)) return;
            }
        }

        private static bool TryGetInfo(out NativeMethods.GUITHREADINFO info)
        {
            info = new NativeMethods.GUITHREADINFO();
            info.cbSize = Marshal.SizeOf(typeof(NativeMethods.GUITHREADINFO));
            return NativeMethods.GetGUIThreadInfo(0, ref info); // 0: 前面のスレッド
        }

        private static bool InMenu(NativeMethods.GUITHREADINFO info)
        {
            return (info.flags & (NativeMethods.GUI_INMENUMODE | NativeMethods.GUI_POPUPMENUMODE)) != 0;
        }
    }
}
