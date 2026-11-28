using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Wakudori
{
    internal sealed class CaptureRecord
    {
        public readonly DateTime CapturedAt;
        public readonly long SizeBytes;
        public readonly long LastWriteUtcTicks;
        public readonly string FullPath;
        public readonly bool IsFolder;
        public readonly int ImageCount;

        public CaptureRecord(DateTime capturedAt, long sizeBytes, long lastWriteUtcTicks, string fullPath)
        {
            CapturedAt = capturedAt;
            SizeBytes = sizeBytes;
            LastWriteUtcTicks = lastWriteUtcTicks;
            FullPath = fullPath;
            ImageCount = 1;
        }

        public CaptureRecord(DateTime endedAt, int count, string folder)
        {
            CapturedAt = endedAt;
            FullPath = folder;
            IsFolder = true;
            ImageCount = count;
        }
    }

    // 保存した画像や見張りのフォルダーをタブ区切りで記録する(UIに依存しない)
    internal static class CaptureLog
    {
        private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss";
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();
        private static readonly char[] FieldSeparators = { '\t', '\r', '\n' };
        private static readonly Regex WatchFolderName = new Regex(@"\A見張り_[0-9]{8}_[0-9]{6}(?:_[0-9]+)?\z");
        // 整理の読み直しと書き直しの間に撮影の追記が入ると, その行が失われる
        private static readonly object LogLock = new object();

        public static string DefaultPath
        {
            get { return Path.Combine(AppSettings.ConfigDir, "captures.log"); }
        }

        public static void AppendFolder(string logPath, DateTime endedAt, int count, string folder)
        {
            Append(logPath, new CaptureRecord(endedAt, count, folder));
        }

        public static void Append(string logPath, CaptureRecord record)
        {
            string line = Format(record);
            lock (LogLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath)));
                using (FileStream stream = new FileStream(logPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                {
                    // 前の追記が途中で止まっていても, 今回の記録は別の行にする
                    if (stream.Length > 0)
                    {
                        stream.Seek(-1, SeekOrigin.End);
                        if (stream.ReadByte() != '\n') stream.WriteByte((byte)'\n');
                    }
                    using (StreamWriter writer = new StreamWriter(stream, Utf8))
                        writer.WriteLine(line);
                }
            }
        }

        public static List<CaptureRecord> Read(string logPath)
        {
            List<CaptureRecord> records = new List<CaptureRecord>();
            lock (LogLock)
            {
                try
                {
                    foreach (string line in File.ReadLines(logPath, Utf8))
                    {
                        CaptureRecord record;
                        if (TryParse(line, out record)) records.Add(record);
                    }
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            return records;
        }

        public static void Rewrite(string logPath, IEnumerable<CaptureRecord> records)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath)));
            string tmp = logPath + ".tmp";
            using (StreamWriter writer = new StreamWriter(tmp, false, Utf8))
            {
                foreach (CaptureRecord record in records) writer.WriteLine(Format(record));
            }
            if (File.Exists(logPath)) File.Replace(tmp, logPath, null);
            else File.Move(tmp, logPath);
        }

        public static void Remove(string logPath, IEnumerable<CaptureRecord> removed)
        {
            HashSet<string> lines = new HashSet<string>();
            foreach (CaptureRecord record in removed) lines.Add(Format(record));
            if (lines.Count == 0) return;

            lock (LogLock)
            {
                // ごみ箱へ移している間に追記された行も残す
                List<CaptureRecord> records = Read(logPath);
                if (records.RemoveAll(record => lines.Contains(Format(record))) > 0) Rewrite(logPath, records);
            }
        }

        private static string Format(CaptureRecord record)
        {
            string time = record.CapturedAt.ToString(TimeFormat, Culture);
            if (record.IsFolder)
            {
                if (record.ImageCount < 0 || !IsFullFolderPath(record.FullPath))
                    throw new ArgumentException("見張りのフォルダーのフルパスと枚数を記録できません.", "record");
                return time + "\tfolder\t" + record.ImageCount.ToString(Culture) + "\t" + record.FullPath;
            }

            if (!IsFullPath(record.FullPath) || !HasPngExtension(record.FullPath))
                throw new ArgumentException("PNG画像のフルパスを記録できません.", "record");
            return time + "\t" + record.SizeBytes.ToString(Culture)
                + "\t" + record.LastWriteUtcTicks.ToString(Culture) + "\t" + record.FullPath;
        }

        private static bool TryParse(string line, out CaptureRecord record)
        {
            record = null;
            string[] fields = line.Split('\t');
            DateTime time;
            if (fields.Length != 4
                || !DateTime.TryParseExact(fields[0], TimeFormat, Culture, DateTimeStyles.AssumeLocal, out time)) return false;

            if (fields[1] == "folder")
            {
                int count;
                if (!int.TryParse(fields[2], NumberStyles.None, Culture, out count)
                    || !IsFullFolderPath(fields[3])) return false;
                record = new CaptureRecord(time, count, fields[3]);
                return true;
            }

            long size;
            long ticks;
            if (!long.TryParse(fields[1], NumberStyles.None, Culture, out size)
                || !long.TryParse(fields[2], NumberStyles.None, Culture, out ticks)
                || ticks > DateTime.MaxValue.Ticks
                || !IsFullPath(fields[3]) || !HasPngExtension(fields[3])) return false;

            record = new CaptureRecord(time, size, ticks, fields[3]);
            return true;
        }

        private static bool IsFullFolderPath(string path)
        {
            // 追記が途中で止まったときに, 見張りの名前に合わないパスを読み飛ばす
            return IsFullPath(path) && WatchFolderName.IsMatch(Path.GetFileName(path));
        }

        private static bool HasPngExtension(string path)
        {
            return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFullPath(string path)
        {
            if (path == null || path.IndexOfAny(FieldSeparators) >= 0
                || path.IndexOfAny(InvalidPathChars) >= 0 || !Path.IsPathRooted(path)) return false;
            if (Path.DirectorySeparatorChar != '\\') return true;
            // Windowsではドライブのルート(C:\)かUNCの先頭(\\)が必要
            return path.Length > 2 && (IsDirectorySeparator(path[0])
                ? IsDirectorySeparator(path[1])
                : path[1] == ':' && IsDirectorySeparator(path[2]));
        }

        private static bool IsDirectorySeparator(char c)
        {
            return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
        }
    }
}
