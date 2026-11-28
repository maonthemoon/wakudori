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

        Func<string, int, CaptureFolderState?> unexpectedFolderRead = (path, _) => throw new InvalidOperationException(path);
        List<string> checkedPaths = new List<string>();
        CleanupResult result = Cleanup.Select(records, now, days, true, path =>
        {
            checkedPaths.Add(path);
            return states[path];
        }, unexpectedFolderRead, path => path != nonFixed.FullPath && path != youngNonFixed.FullPath);
        CheckRecords("recycle eligible fixed-drive files at 30 days, allowing up to 2 seconds of timestamp difference", result.ToRecycle,
            exact, laterTwoSeconds, earlierTwoSeconds);
        Eq("count individually recorded images", 3, result.ImagesToRecycle);
        CheckRecords("keep matching files and unknown states one second before cutoff", result.ToKeep,
            tooYoung, youngUnreadable, youngNonFixed);
        CheckRecords("forget missing and changed files, and expired records with non-fixed drives or unknown states", result.ToForget,
            missing, differentSize, youngDifferentSize, laterThreeSeconds, earlierThreeSeconds, unreadable, nonFixed);
        Eq("check only logged paths", true, checkedPaths.SequenceEqual(records.Select(record => record.FullPath)));

        Func<string, bool> unexpectedDriveCheck = path => throw new InvalidOperationException(path);
        result = Cleanup.Select(records, now, days, false, path => states[path], unexpectedFolderRead, unexpectedDriveCheck);
        CheckRecords("with autoDelete off, recycle no files", result.ToRecycle);
        CheckRecords("with autoDelete off, keep unexpired matching files and unknown states", result.ToKeep,
            tooYoung, youngUnreadable, youngNonFixed);
        CheckRecords("with autoDelete off, forget all expired records and unexpired missing or changed files", result.ToForget,
            exact, missing, differentSize, youngDifferentSize, laterTwoSeconds, earlierTwoSeconds,
            laterThreeSeconds, earlierThreeSeconds, unreadable, nonFixed);

        result = Cleanup.Select(new[] { tooYoung }, now, days - 1, false, path => states[path], unexpectedFolderRead, unexpectedDriveCheck);
        CheckRecords("with autoDelete off, use the configured retention days", result.ToForget, tooYoung);

        result = Cleanup.Select(new CaptureRecord[0], now, days, true,
            path => throw new InvalidOperationException(path), unexpectedFolderRead, unexpectedDriveCheck);
        CheckRecords("empty log has no recycle candidates", result.ToRecycle);
        CheckRecords("empty log has no records to keep", result.ToKeep);
        CheckRecords("empty log has no records to forget", result.ToForget);

        CheckFileStates();
        CheckFolderCleanup(now, days);

        Console.WriteLine(fail == 0 ? "ALL PASS" : fail + " FAILED");
        return fail == 0 ? 0 : 1;
    }

    private static void CheckFolderCleanup(DateTime now, int days)
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory("wakudori-watch-cleanup-");
        try
        {
            DateTime cutoff = now.AddDays(-days);
            CaptureRecord shots = new CaptureRecord(cutoff, 1000, root.CreateSubdirectory("shots").FullName);
            CaptureRecord empty = new CaptureRecord(cutoff, 1, root.CreateSubdirectory("empty").FullName);
            CaptureRecord otherFile = new CaptureRecord(cutoff, 1, root.CreateSubdirectory("other-file").FullName);
            CaptureRecord subfolder = new CaptureRecord(cutoff, 1, root.CreateSubdirectory("subfolder").FullName);
            CaptureRecord shortNumber = new CaptureRecord(cutoff, 1, root.CreateSubdirectory("short-number").FullName);
            CaptureRecord paddedNumber = new CaptureRecord(cutoff, 1, root.CreateSubdirectory("padded-number").FullName);
            CaptureRecord extraNumber = new CaptureRecord(cutoff, 1, root.CreateSubdirectory("extra-number").FullName);
            CaptureRecord zeroNumber = new CaptureRecord(cutoff, 1, root.CreateSubdirectory("zero-number").FullName);
            CaptureRecord overflowNumber = new CaptureRecord(cutoff, int.MaxValue, root.CreateSubdirectory("overflow-number").FullName);
            CaptureRecord young = new CaptureRecord(cutoff.AddSeconds(1), 1, root.CreateSubdirectory("young").FullName);
            CaptureRecord missing = new CaptureRecord(cutoff, 1, Path.Combine(root.FullName, "missing"));
            CaptureRecord unreadable = new CaptureRecord(cutoff, 1, Path.Combine(root.FullName, "unreadable"));
            CaptureRecord nonFixed = new CaptureRecord(cutoff, 1, root.CreateSubdirectory("non-fixed").FullName);
            File.WriteAllText(Path.Combine(shots.FullPath, "001.png"), "");
            File.WriteAllText(Path.Combine(shots.FullPath, "1000.png"), "");
            File.WriteAllText(Path.Combine(otherFile.FullPath, "notes.txt"), "");
            Directory.CreateDirectory(Path.Combine(subfolder.FullPath, "001.png"));
            File.WriteAllText(Path.Combine(shortNumber.FullPath, "01.png"), "");
            File.WriteAllText(Path.Combine(paddedNumber.FullPath, "001.png"), "");
            File.WriteAllText(Path.Combine(paddedNumber.FullPath, "0001.png"), "");
            File.WriteAllText(Path.Combine(extraNumber.FullPath, "001.png"), "");
            File.WriteAllText(Path.Combine(extraNumber.FullPath, "002.png"), "");
            File.WriteAllText(Path.Combine(zeroNumber.FullPath, "000.png"), "");
            File.WriteAllText(Path.Combine(overflowNumber.FullPath, "2147483648.png"), "");

            CaptureRecord[] records = {
                shots, empty, otherFile, subfolder, shortNumber, paddedNumber, extraNumber, zeroNumber, overflowNumber,
                young, missing, unreadable, nonFixed
            };
            Func<string, CaptureFileState?> unexpectedFileRead = path => throw new InvalidOperationException(path);
            Func<string, int, CaptureFolderState?> readFolder = (path, recordedCount) =>
            {
                if (path == young.FullPath) throw new InvalidOperationException(path);
                return CaptureRecycler.ReadFolderState(path, recordedCount, _ => path != unreadable.FullPath);
            };
            CleanupResult result = Cleanup.Select(records, now, days, true, unexpectedFileRead, readFolder,
                path => path != nonFixed.FullPath);
            CheckRecords("recycle expired folders with gaps in numbering and empty folders", result.ToRecycle, shots, empty);
            Eq("count images currently in eligible folders", 2, result.ImagesToRecycle);
            CheckRecords("keep young folders and folders with unavailable roots", result.ToKeep, young, unreadable);
            CheckRecords("forget expired folders with extra entries, missing folders and non-fixed folders", result.ToForget,
                otherFile, subfolder, shortNumber, paddedNumber, extraNumber, zeroNumber, overflowNumber, missing, nonFixed);

            result = Cleanup.Select(records, now, days, false, unexpectedFileRead,
                (path, _) => throw new InvalidOperationException(path), path => throw new InvalidOperationException(path));
            CheckRecords("with autoDelete off, recycle no folders", result.ToRecycle);
            CheckRecords("with autoDelete off, keep young folders", result.ToKeep, young);
            CheckRecords("with autoDelete off, forget expired folders without checking their state", result.ToForget,
                shots, empty, otherFile, subfolder, shortNumber, paddedNumber, extraNumber, zeroNumber, overflowNumber,
                missing, unreadable, nonFixed);

            File.Delete(Path.Combine(shots.FullPath, "1000.png"));
            int movedImages;
            Eq("pass folder path to recycler", CaptureMoveResult.Moved, CaptureRecycler.TryMove(shots, _ => true, path =>
            {
                Eq("recycler receives folder path", shots.FullPath, path);
                return true;
            }, out movedImages));
            Eq("report images found just before recycling", 1, movedImages);
            Eq("keep the folder record if recycling fails", CaptureMoveResult.Keep,
                CaptureRecycler.TryMove(shots, _ => true, _ => false, out movedImages));
            Eq("report no images if recycling fails", 0, movedImages);

            Func<string, bool> unexpectedRecycle = path => throw new InvalidOperationException(path);
            Eq("recheck the drive before moving the folder", CaptureMoveResult.Forget,
                CaptureRecycler.TryMove(shots, _ => false, unexpectedRecycle, out movedImages));
            string latePng = Path.Combine(shots.FullPath, "1001.png");
            File.WriteAllText(latePng, "");
            Eq("recheck the recorded number range before moving the folder", CaptureMoveResult.Forget,
                CaptureRecycler.TryMove(shots, _ => true, unexpectedRecycle, out movedImages));
            File.Delete(latePng);
            File.WriteAllText(Path.Combine(shots.FullPath, "late.txt"), "");
            Eq("recheck folder contents before moving and forget if they changed", CaptureMoveResult.Forget,
                CaptureRecycler.TryMove(shots, _ => true, unexpectedRecycle, out movedImages));
        }
        finally
        {
            root.Delete(true);
        }
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
