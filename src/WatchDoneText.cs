namespace Wakudori
{
    internal static class WatchDoneText
    {
        private const string FinishedHeading = "見張りを終えました";

        public static string Heading(WatchStopReason reason)
        {
            switch (reason)
            {
                case WatchStopReason.MaxDuration: return FinishedHeading + "(2時間たちました)";
                case WatchStopReason.MaxShots: return FinishedHeading + "(500枚になりました)";
                case WatchStopReason.Error: return "見張りの画像を保存できませんでした";
                default: return FinishedHeading;
            }
        }

        public static string Body(WatchFinishedEventArgs result)
        {
            string body = result.ShotCount == 0
                ? "変化がなかったので, 撮った画像はありません."
                : ShotSummary(result.ShotCount) + "\r\n" + result.Folder;
            if (result.SkippedBlack) body += "\r\n保護された画面(真っ黒)は撮りませんでした.";
            return body;
        }

        public static string Hint(int shotCount)
        {
            return shotCount > 0 ? "クリックで保存先を開く · 数秒で消える" : "数秒で消える";
        }

        public static string ErrorBody(WatchFinishedEventArgs result, string saveDir)
        {
            string folder = result.Folder ?? ("保存先(設定): " + saveDir);
            return ShotSummary(result.ShotCount) + "\r\n" + folder + "\r\n\r\n" + result.ErrorMessage;
        }

        private static string ShotSummary(int shotCount)
        {
            return shotCount + "枚を撮りました.";
        }
    }
}
