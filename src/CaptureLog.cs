using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Wakudori
{
    internal sealed class CaptureRecord
    {
        public readonly DateTime CapturedAt;
        public readonly long SizeBytes;
        public readonly long LastWriteUtcTicks;
        public readonly string FullPath;

        public CaptureRecord(DateTime capturedAt, long sizeBytes, long lastWriteUtcTicks, string fullPath)
        {
            CapturedAt = capturedAt;
            SizeBytes = sizeBytes;
            LastWriteUtcTicks = lastWriteUtcTicks;
            FullPath = fullPath;
        }
    }

    // 保存した画像を1枚ずつタブ区切りで記録する(UIに依存しない)
    internal static class CaptureLog
    {
        private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss";
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();
        private static readonly char[] FieldSeparators = { '\t', '\r', '\n' };

        public static string DefaultPath
        {
            get { return Path.Combine(AppSettings.ConfigDir, "captures.log"); }
        }

        public static void Append(string logPath, CaptureRecord record)
        {
            string line = Format(record);
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

        public static List<CaptureRecord> Read(string logPath)
        {
            List<CaptureRecord> records = new List<CaptureRecord>();
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

        private static string Format(CaptureRecord record)
        {
            if (!IsFullPngPath(record.FullPath)) throw new ArgumentException("PNG画像のフルパスを記録できません.", "record");
            return record.CapturedAt.ToString(TimeFormat, Culture) + "\t" + record.SizeBytes.ToString(Culture)
                + "\t" + record.LastWriteUtcTicks.ToString(Culture) + "\t" + record.FullPath;
        }

        private static bool TryParse(string line, out CaptureRecord record)
        {
            record = null;
            string[] fields = line.Split('\t');
            DateTime time;
            long size;
            long ticks;
            if (fields.Length != 4
                || !DateTime.TryParseExact(fields[0], TimeFormat, Culture, DateTimeStyles.AssumeLocal, out time)
                || !long.TryParse(fields[1], NumberStyles.None, Culture, out size)
                || !long.TryParse(fields[2], NumberStyles.None, Culture, out ticks)
                || ticks > DateTime.MaxValue.Ticks
                || !IsFullPngPath(fields[3])) return false;

            record = new CaptureRecord(time, size, ticks, fields[3]);
            return true;
        }

        private static bool IsFullPngPath(string path)
        {
            if (path == null || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || path.IndexOfAny(FieldSeparators) >= 0
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
