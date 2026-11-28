using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wakudori;

internal static class Program
{
    static int fail;

    static void Eq<T>(string label, T expected, T actual)
    {
        bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
        Console.WriteLine((ok ? "PASS " : "FAIL ") + label + (ok ? "" : " expected=[" + expected + "] actual=[" + actual + "]"));
        if (!ok) fail++;
    }

    static void CheckRecord(string label, CaptureRecord expected, CaptureRecord actual)
    {
        Eq(label + " time", expected.CapturedAt, actual.CapturedAt);
        Eq(label + " time kind", DateTimeKind.Local, actual.CapturedAt.Kind);
        Eq(label + " size", expected.SizeBytes, actual.SizeBytes);
        Eq(label + " write ticks", expected.LastWriteUtcTicks, actual.LastWriteUtcTicks);
        Eq(label + " path", expected.FullPath, actual.FullPath);
        Eq(label + " is folder", expected.IsFolder, actual.IsFolder);
        Eq(label + " image count", expected.ImageCount, actual.ImageCount);
    }

    static void CheckFolderRecords(string dir, CaptureRecord image, string imageLine, Encoding utf8)
    {
        string log = Path.Combine(dir, "folder.log");
        string folder = Path.Combine(dir, "見張り_20261120_090507");
        DateTime endedAt = image.CapturedAt.AddMinutes(10);
        const string stamp = "2026-11-20T09:15:07";
        string folderLine = stamp + "\tfolder\t12\t" + folder + Environment.NewLine;
        CaptureLog.Append(log, image);
        CaptureLog.AppendFolder(log, endedAt, 12, folder);
        Eq("write one folder row with end time and count", imageLine + folderLine, File.ReadAllText(log, utf8));

        File.AppendAllLines(log, new[] {
            "timestamp\tfolder\t12\t" + folder,
            stamp + "\tfolder\t-1\t" + folder,
            stamp + "\tfolder\t2147483648\t" + folder,
            stamp + "\tfolder\tmany\t" + folder,
            stamp + "\tfolder\t12",
            stamp + "\tfolder\t12\t" + folder + "\textra",
            stamp + "\tfolder\t12\t",
            stamp + "\tfolder\t12\trelative",
            stamp + "\tfolder\t12\tC:relative",
            stamp + "\tfolder\t12\t\\relative",
            stamp + "\tfolder\t12\t" + image.FullPath,
            stamp + "\tfolder\t12\t" + Path.Combine(dir, "folder.PNG") + Path.DirectorySeparatorChar,
            stamp + "\tFolder\t12\t" + folder,
        }, utf8);

        List<CaptureRecord> records = CaptureLog.Read(log);
        Eq("skip malformed folder rows and read mixed records", 2, records.Count);
        if (records.Count != 2) return;
        CheckRecord("mixed log image", image, records[0]);
        Eq("mixed log folder flag", true, records[1].IsFolder);
        Eq("mixed log folder count", 12, records[1].ImageCount);
        Eq("mixed log folder end time", endedAt, records[1].CapturedAt);
        Eq("mixed log folder time kind", DateTimeKind.Local, records[1].CapturedAt.Kind);
        Eq("mixed log folder path", folder, records[1].FullPath);

        CaptureLog.Rewrite(log, records);
        Eq("rewrite preserves both row formats", imageLine + folderLine, File.ReadAllText(log, utf8));
        CaptureLog.Remove(log, new[] { records[1] });
        Eq("remove folder preserves image row", imageLine, File.ReadAllText(log, utf8));

        bool rejected = false;
        try
        {
            CaptureLog.AppendFolder(log, endedAt, 12, image.FullPath);
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        Eq("reject PNG path on folder append", true, rejected);
        Eq("rejected folder append leaves log intact", imageLine, File.ReadAllText(log, utf8));
    }

    static void CheckInterruptedFolderAppend(string dir, DateTime endedAt, Encoding utf8)
    {
        string log = Path.Combine(dir, "interrupted-folder.log");
        string folder = Path.Combine(dir, "見張り_20261120_090507_2");
        string[] incompletePaths = { Path.Combine(dir, "見張り_2026112"), Path.Combine(dir, "見張"), dir };
        foreach (string path in incompletePaths)
        {
            File.WriteAllText(log, "2026-11-20T09:15:07\tfolder\t1\t" + path, utf8);
            Eq("skip interrupted folder row: " + path, 0, CaptureLog.Read(log).Count);
            CaptureLog.AppendFolder(log, endedAt, 1, folder);
            List<CaptureRecord> records = CaptureLog.Read(log);
            Eq("skip interrupted folder row after the next append: " + path, 1, records.Count);
            if (records.Count == 1) Eq("read folder name with numeric suffix", folder, records[0].FullPath);
        }
    }

    static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wakudori-test-" + Guid.NewGuid().ToString("N"));
        string log = Path.Combine(dir, "config", "captures.log");
        DateTime time = new DateTime(2026, 11, 20, 9, 5, 7, DateTimeKind.Local);
        CaptureRecord first = new CaptureRecord(time, 3456789012L, 639000000000000001L, Path.Combine(dir, "設計 図 #1 (版2)+&'%.png"));
        CaptureRecord second = new CaptureRecord(time.AddSeconds(1), 987, first.LastWriteUtcTicks + 1, Path.Combine(dir, "設計 図_edit.PNG"));
        Encoding utf8 = new UTF8Encoding(false);
        const string stamp = "2026-11-20T09:05:07";
        string firstLine = stamp + "\t3456789012\t639000000000000001\t" + first.FullPath + Environment.NewLine;

        try
        {
            Eq("default path", Path.Combine(AppSettings.ConfigDir, "captures.log"), CaptureLog.DefaultPath);
            Eq("missing log", 0, CaptureLog.Read(log).Count);
            CaptureLog.Append(log, first);
            Eq("UTF-8 without BOM and TSV fields", true, File.ReadAllBytes(log).SequenceEqual(utf8.GetBytes(firstLine)));

            File.AppendAllLines(log, new[] {
                "# 説明行",
                "",
                "timestamp\t123\t456\t" + first.FullPath,
                stamp + "\t-1\t456\t" + first.FullPath,
                stamp + "\t123\t3155378976000000000\t" + first.FullPath,
                stamp + "\t123\t456",
                stamp + "\t123\t456\t" + first.FullPath + "\textra",
                stamp + "\t123\t456\t",
                stamp + "\t123\t456\trelative.png",
                stamp + "\t123\t456\tC:relative.png",
                stamp + "\t123\t456\t\\relative.png",
            }, utf8);
            string partialPath = Path.Combine(dir, "途中で切れた画像.pn");
            File.AppendAllText(log, stamp + "\t123\t456\t" + partialPath, utf8);
            Eq("skip truncated path before append", 1, CaptureLog.Read(log).Count);
            CaptureLog.Append(log, second);

            List<CaptureRecord> records = CaptureLog.Read(log);
            Eq("skip truncated path and read next append", 2, records.Count);
            if (records.Count == 2)
            {
                CheckRecord("first", first, records[0]);
                CheckRecord("second", second, records[1]);
            }

            CaptureLog.Rewrite(log, new[] { second });
            string secondLine = "2026-11-20T09:05:08\t987\t639000000000000002\t" + second.FullPath + Environment.NewLine;
            Eq("replace with just the requested record", secondLine, File.ReadAllText(log, utf8));
            Eq("read after rewrite", 1, CaptureLog.Read(log).Count);
            CaptureLog.Rewrite(log, new CaptureRecord[0]);
            Eq("empty rewrite", 0L, new FileInfo(log).Length);

            string newLog = Path.Combine(dir, "other", "captures.log");
            CaptureLog.Rewrite(newLog, new[] { first });
            Eq("rewrite creates missing log", firstLine, File.ReadAllText(newLog, utf8));

            CaptureRecord newer = new CaptureRecord(time.AddDays(1), 1024, first.LastWriteUtcTicks + 1, first.FullPath);
            CaptureLog.Append(newLog, newer);
            CaptureLog.Remove(newLog, new[] { first });
            List<CaptureRecord> remaining = CaptureLog.Read(newLog);
            Eq("remove preserves a later record for the same path", 1, remaining.Count);
            if (remaining.Count == 1) CheckRecord("later record", newer, remaining[0]);

            bool rejected = false;
            string beforeRejectedAppend = File.ReadAllText(newLog, utf8);
            try
            {
                CaptureLog.Append(newLog, new CaptureRecord(time, 123, 456, partialPath));
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Eq("reject non-PNG path on append", true, rejected);
            Eq("rejected append leaves log intact", beforeRejectedAppend, File.ReadAllText(newLog, utf8));

            string concurrentLog = Path.Combine(dir, "concurrent.log");
            CaptureRecord[] oldRecords = Enumerable.Range(0, 32)
                .Select(i => new CaptureRecord(time, 1024, first.LastWriteUtcTicks, Path.Combine(dir, "old-" + i + ".png"))).ToArray();
            CaptureRecord[] newRecords = Enumerable.Range(0, 32)
                .Select(i => new CaptureRecord(time, 1024, first.LastWriteUtcTicks, Path.Combine(dir, "new-" + i + ".png"))).ToArray();
            CaptureLog.Rewrite(concurrentLog, oldRecords);
            Parallel.Invoke(
                () => { foreach (CaptureRecord record in oldRecords) CaptureLog.Remove(concurrentLog, new[] { record }); },
                () => { foreach (CaptureRecord record in newRecords) CaptureLog.Append(concurrentLog, record); });
            Eq("concurrent removal preserves every appended record", true,
                CaptureLog.Read(concurrentLog).Select(record => record.FullPath).SequenceEqual(newRecords.Select(record => record.FullPath)));

            CheckFolderRecords(dir, first, firstLine, utf8);
            CheckInterruptedFolderAppend(dir, first.CapturedAt, utf8);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        Console.WriteLine(fail == 0 ? "ALL PASS" : fail + " FAILED");
        return fail == 0 ? 0 : 1;
    }
}

namespace Wakudori
{
    // .NET Framework向けの設定クラスに代わり, 既定パスの組み立てに使う設定フォルダーを返す
    internal static class AppSettings
    {
        public const string ConfigDir = "test-config";
    }
}
