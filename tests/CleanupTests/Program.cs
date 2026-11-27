using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wakudori;

internal static class Program
{
    private static int fail;

    private static void Eq<T>(string label, T expected, T actual)
    {
        bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
        Console.WriteLine((ok ? "PASS " : "FAIL ") + label + (ok ? "" : " expected=[" + expected + "] actual=[" + actual + "]"));
        if (!ok) fail++;
    }

    private static void CheckRecords(string label, IList<CaptureRecord> actual, params CaptureRecord[] expected)
    {
        Eq(label, string.Join(", ", expected.Select(record => record.FullPath)),
            string.Join(", ", actual.Select(record => record.FullPath)));
    }

    private static int Main()
    {
        Eq("unreadable settings skip cleanup before touching the capture log", 0,
            Cleanup.RunIfSettingsReadable(false, () => throw new InvalidOperationException("cleanup ran")));
        Eq("readable or missing settings allow cleanup", 2,
            Cleanup.RunIfSettingsReadable(true, () => 2));

        const long size = 1024;
        const int days = 30;
        DateTime now = new DateTime(2026, 11, 20, 9, 5, 7, DateTimeKind.Utc);
        DateTime cutoff = now.AddDays(-days);
        long write = cutoff.Ticks;
        long second = TimeSpan.TicksPerSecond;

        CaptureRecord exact = new CaptureRecord(cutoff.ToLocalTime(), size, write, "exact.png");
        CaptureRecord tooYoung = new CaptureRecord(cutoff.AddSeconds(1), size, write, "too-young.png");
        CaptureRecord missing = new CaptureRecord(cutoff.AddSeconds(1), size, write, "missing.png");
        CaptureRecord differentSize = new CaptureRecord(cutoff, size, write, "different-size.png");
        CaptureRecord youngDifferentSize = new CaptureRecord(cutoff.AddSeconds(1), size, write, "young-different-size.png");
        CaptureRecord laterTwoSeconds = new CaptureRecord(cutoff, size, write, "later-two-seconds.png");
        CaptureRecord earlierTwoSeconds = new CaptureRecord(cutoff, size, write, "earlier-two-seconds.png");
        CaptureRecord laterThreeSeconds = new CaptureRecord(cutoff, size, write, "later-three-seconds.png");
        CaptureRecord earlierThreeSeconds = new CaptureRecord(cutoff, size, write, "earlier-three-seconds.png");
        CaptureRecord unreadable = new CaptureRecord(cutoff, size, write, "unreadable.png");
        CaptureRecord youngUnreadable = new CaptureRecord(cutoff.AddSeconds(1), size, write, "young-unreadable.png");
        CaptureRecord nonFixed = new CaptureRecord(cutoff, size, write, "non-fixed.png");
        CaptureRecord youngNonFixed = new CaptureRecord(cutoff.AddSeconds(1), size, write, "young-non-fixed.png");

        CaptureRecord[] records = { exact, tooYoung, missing, differentSize, youngDifferentSize, laterTwoSeconds,
            earlierTwoSeconds, laterThreeSeconds, earlierThreeSeconds, unreadable, youngUnreadable, nonFixed, youngNonFixed };
        Dictionary<string, CaptureFileState?> states = new Dictionary<string, CaptureFileState?>
        {
            { exact.FullPath, new CaptureFileState(true, size, write) },
            { tooYoung.FullPath, new CaptureFileState(true, size, write) },
            { missing.FullPath, new CaptureFileState(false, size, write) },
            { differentSize.FullPath, new CaptureFileState(true, size + 1, write) },
            { youngDifferentSize.FullPath, new CaptureFileState(true, size + 1, write) },
            { laterTwoSeconds.FullPath, new CaptureFileState(true, size, write + 2 * second) },
            { earlierTwoSeconds.FullPath, new CaptureFileState(true, size, write - 2 * second) },
            { laterThreeSeconds.FullPath, new CaptureFileState(true, size, write + 3 * second) },
            { earlierThreeSeconds.FullPath, new CaptureFileState(true, size, write - 3 * second) },
            { unreadable.FullPath, null },
            { youngUnreadable.FullPath, null },
            { nonFixed.FullPath, new CaptureFileState(true, size, write) },
            { youngNonFixed.FullPath, new CaptureFileState(true, size, write) },
        };

        List<string> checkedPaths = new List<string>();
        CleanupResult result = Cleanup.Select(records, now, days, true, path =>
        {
            checkedPaths.Add(path);
            return states[path];
        }, path => path != nonFixed.FullPath && path != youngNonFixed.FullPath);
        CheckRecords("recycle eligible fixed-drive files at 30 days, allowing up to 2 seconds of timestamp difference", result.ToRecycle,
            exact, laterTwoSeconds, earlierTwoSeconds);
        CheckRecords("keep matching files and unknown states one second before cutoff", result.ToKeep,
            tooYoung, youngUnreadable, youngNonFixed);
        CheckRecords("forget missing and changed files, and expired records with non-fixed drives or unknown states", result.ToForget,
            missing, differentSize, youngDifferentSize, laterThreeSeconds, earlierThreeSeconds, unreadable, nonFixed);
        Eq("check only logged paths", true, checkedPaths.SequenceEqual(records.Select(record => record.FullPath)));

        Func<string, bool> unexpectedDriveCheck = path => throw new InvalidOperationException(path);
        result = Cleanup.Select(records, now, days, false, path => states[path], unexpectedDriveCheck);
        CheckRecords("with autoDelete off, recycle no files", result.ToRecycle);
        CheckRecords("with autoDelete off, keep unexpired matching files and unknown states", result.ToKeep,
            tooYoung, youngUnreadable, youngNonFixed);
        CheckRecords("with autoDelete off, forget all expired records and unexpired missing or changed files", result.ToForget,
            exact, missing, differentSize, youngDifferentSize, laterTwoSeconds, earlierTwoSeconds,
            laterThreeSeconds, earlierThreeSeconds, unreadable, nonFixed);

        result = Cleanup.Select(new[] { tooYoung }, now, days - 1, false, path => states[path], unexpectedDriveCheck);
        CheckRecords("with autoDelete off, use the configured retention days", result.ToForget, tooYoung);

        result = Cleanup.Select(new CaptureRecord[0], now, days, true,
            path => throw new InvalidOperationException(path), unexpectedDriveCheck);
        CheckRecords("empty log has no recycle candidates", result.ToRecycle);
        CheckRecords("empty log has no records to keep", result.ToKeep);
        CheckRecords("empty log has no records to forget", result.ToForget);

        CheckFileStates();

        Console.WriteLine(fail == 0 ? "ALL PASS" : fail + " FAILED");
        return fail == 0 ? 0 : 1;
    }

    private static void CheckFileStates()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("wakudori-cleanup-");
        try
        {
            string image = Path.Combine(directory.FullName, "image.png");
            File.WriteAllBytes(image, new byte[12]);
            CaptureFileState? state = CaptureRecycler.ReadState(image);
            Eq("read existing file size", 12L, state?.SizeBytes);
            Eq("read existing file UTC write time", File.GetLastWriteTimeUtc(image).Ticks, state?.LastWriteUtcTicks);
            string missingFile = Path.Combine(directory.FullName, "missing.png");
            string missingDirectory = Path.Combine(directory.FullName, "old-folder", "image.png");
            Eq("report missing file under an available root", false, CaptureRecycler.ReadState(missingFile)?.Exists);
            Eq("report missing parent directory under an available root", false, CaptureRecycler.ReadState(missingDirectory)?.Exists);

            Func<string, bool> unavailableRoot = root =>
            {
                Eq("probe the path root", Path.GetPathRoot(directory.FullName), root);
                return false;
            };
            Eq("keep state unknown for a missing file under an unavailable root", null,
                CaptureRecycler.ReadState(missingFile, unavailableRoot));
            Eq("keep state unknown for a missing parent directory under an unavailable root", null,
                CaptureRecycler.ReadState(missingDirectory, unavailableRoot));
        }
        finally
        {
            directory.Delete(true);
        }
    }
}

namespace Wakudori
{
    // .NET Framework向けの設定クラスに代わり, CaptureLogの既定パスに使う
    internal static class AppSettings
    {
        public const string ConfigDir = "test-config";
    }
}
