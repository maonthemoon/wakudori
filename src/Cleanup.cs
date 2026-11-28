using System;
using System.Collections.Generic;

namespace Wakudori
{
    internal struct CaptureFileState
    {
        public readonly bool Exists;
        public readonly long SizeBytes;
        public readonly long LastWriteUtcTicks;

        public CaptureFileState(bool exists, long sizeBytes, long lastWriteUtcTicks)
        {
            Exists = exists;
            SizeBytes = sizeBytes;
            LastWriteUtcTicks = lastWriteUtcTicks;
        }
    }

    internal struct CaptureFolderState
    {
        public readonly bool HasOnlyCaptures;
        public readonly int ImageCount;

        public CaptureFolderState(int imageCount)
        {
            HasOnlyCaptures = true;
            ImageCount = imageCount;
        }
    }

    internal sealed class CleanupResult
    {
        public readonly List<CaptureRecord> ToRecycle = new List<CaptureRecord>();
        public readonly List<CaptureRecord> ToKeep = new List<CaptureRecord>();
        public readonly List<CaptureRecord> ToForget = new List<CaptureRecord>();
        public int ImagesToRecycle;
    }

    internal static class Cleanup
    {
        public static int RunIfSettingsReadable(bool canSave, Func<int> run)
        {
            // 読めなかった設定の既定値で撮影記録を外さない.
            return canSave ? run() : 0;
        }

        public static CleanupResult Select(IEnumerable<CaptureRecord> records, DateTime now, int days, bool autoDelete,
            Func<string, CaptureFileState?> readState, Func<string, int, CaptureFolderState?> readFolderState,
            Func<string, bool> isOnFixedDrive)
        {
            CleanupResult result = new CleanupResult();
            DateTime nowUtc = now.ToUniversalTime();
            TimeSpan minAge = TimeSpan.FromDays(days);
            foreach (CaptureRecord record in records)
            {
                // 夏時間で時計が変わっても, 24時間を1日として数える
                bool expired = nowUtc - record.CapturedAt.ToUniversalTime() >= minAge;
                if (expired && !autoDelete)
                {
                    result.ToForget.Add(record);
                    continue;
                }

                if (record.IsFolder)
                {
                    if (!expired)
                    {
                        result.ToKeep.Add(record);
                        continue;
                    }

                    CaptureFolderState? folderState = readFolderState(record.FullPath, record.ImageCount);
                    if (!folderState.HasValue) result.ToKeep.Add(record);
                    else if (folderState.Value.HasOnlyCaptures && isOnFixedDrive(record.FullPath))
                    {
                        result.ToRecycle.Add(record);
                        result.ImagesToRecycle += folderState.Value.ImageCount;
                    }
                    else result.ToForget.Add(record);
                    continue;
                }

                CaptureFileState? state = readState(record.FullPath);
                if (state.HasValue && !Matches(record, state.Value)) result.ToForget.Add(record);
                else if (!expired) result.ToKeep.Add(record);
                else if (state.HasValue && isOnFixedDrive(record.FullPath))
                {
                    result.ToRecycle.Add(record);
                    result.ImagesToRecycle++;
                }
                else result.ToForget.Add(record);
            }
            return result;
        }

        public static bool Matches(CaptureRecord record, CaptureFileState state)
        {
            return state.Exists && state.SizeBytes == record.SizeBytes
                && Math.Abs(state.LastWriteUtcTicks - record.LastWriteUtcTicks) <= 2 * TimeSpan.TicksPerSecond;
        }
    }
}
