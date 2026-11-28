using System;

namespace Wakudori
{
    internal enum WatchStopReason { Hotkey, MaxDuration, MaxShots, Error, Exit }

    internal sealed class WatchFinishedEventArgs : EventArgs
    {
        public readonly WatchStopReason Reason;
        public readonly int ShotCount;
        public readonly string Folder;
        public readonly bool SkippedBlack;
        public readonly string ErrorMessage;

        public WatchFinishedEventArgs(WatchStopReason reason, int shotCount, string folder, bool skippedBlack, string errorMessage)
        {
            Reason = reason;
            ShotCount = shotCount;
            Folder = folder;
            SkippedBlack = skippedBlack;
            ErrorMessage = errorMessage;
        }
    }
}
