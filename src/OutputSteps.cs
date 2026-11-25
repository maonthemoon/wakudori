using System;

namespace Wakudori
{
    // 撮った画像の2つの出し先(ファイル, クリップボード)の結果. UIに依存しない.
    // 片方が失敗しても, もう片方は必ず試す(画像を失わないため). 先にファイルへ出す.
    internal sealed class OutputSteps
    {
        public string Path;          // 保存したファイル. 失敗したらnull
        public Exception SaveError;  // 成功ならnull
        public Exception CopyError;  // 成功か, クリップボードに出さないときはnull
        private bool copied;

        // どちらか片方には出せたか
        public bool AnyDone
        {
            get { return Path != null || copied; }
        }

        // copyがnullなら, クリップボードには出さない
        public static OutputSteps Run(Func<string> save, Action copy)
        {
            OutputSteps r = new OutputSteps();
            try
            {
                r.Path = save();
            }
            catch (Exception ex)
            {
                r.SaveError = ex;
            }
            if (copy == null) return r;
            try
            {
                copy();
                r.copied = true;
            }
            catch (Exception ex)
            {
                r.CopyError = ex;
            }
            return r;
        }

        // 知らせの見出し. すべて成功ならnull
        public string Heading
        {
            get
            {
                if (SaveError != null && CopyError != null) return "保存もコピーもできませんでした";
                if (SaveError != null) return "画像を保存できませんでした";
                if (CopyError != null) return "クリップボードに入れられませんでした";
                return null;
            }
        }

        // 知らせの本文. 出せたほうを先に書く
        public string Body
        {
            get
            {
                string s = "";
                if (SaveError != null && copied) s = "クリップボードには入っています.\r\n\r\n";
                else if (CopyError != null && Path != null) s = "画像はファイルに保存しました.\r\n" + Path + "\r\n\r\n";
                if (SaveError != null) s += SaveError.Message;
                if (CopyError != null) s += (SaveError != null ? "\r\n" : "") + CopyError.Message;
                return s;
            }
        }
    }
}
