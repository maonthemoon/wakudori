using System;
using System.IO;
using System.Text;

namespace Wakudori
{
    internal static class CaptureRecycler
    {
        // 確かに無い場合だけ記録から除けるよう, 状態を読めない場合はnullを返す
        public static CaptureFileState? ReadState(string path)
        {
            return ReadState(path, Directory.Exists);
        }

        internal static CaptureFileState? ReadState(string path, Func<string, bool> rootExists)
        {
            try
            {
                FileInfo file = new FileInfo(path);
                if (file.Exists) return new CaptureFileState(true, file.Length, file.LastWriteTimeUtc.Ticks);

                // FileInfoではドライブの準備ができていないときも見つからない扱いになる
                File.GetAttributes(path);
                return null;
            }
            catch (FileNotFoundException)
            {
                return MissingState(path, rootExists);
            }
            catch (DirectoryNotFoundException)
            {
                return MissingState(path, rootExists);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        private static CaptureFileState? MissingState(string path, Func<string, bool> rootExists)
        {
            // ドライブや共有が使えない場合は記録を残す
            return rootExists(Path.GetPathRoot(path)) ? new CaptureFileState() : (CaptureFileState?)null;
        }

        public static bool IsOnFixedDrive(string path)
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            string root = Path.GetPathRoot(path);
            if (root.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(root).DriveType != DriveType.Fixed) return false;

            // 固定ドライブの下に接続された別のボリュームも確かめる
            StringBuilder volume = new StringBuilder(path.Length + 2);
            if (!NativeMethods.GetVolumePathNameW(path, volume, volume.Capacity)) return false;
            string volumeRoot = volume.ToString();
            return !volumeRoot.StartsWith(@"\\", StringComparison.Ordinal)
                && NativeMethods.GetDriveTypeW(volumeRoot) == (uint)DriveType.Fixed;
        }

        public static bool TryMove(CaptureRecord record)
        {
            string path = record.FullPath;
            // シェルが複数のファイル名やワイルドカードとして解釈する文字を渡さない
            if (path.IndexOf('\0') >= 0 || path.IndexOf('*') >= 0 || path.IndexOf('?') >= 0) return false;

            // 選別後に画像が変わっていることがあるので, 1枚ずつ直前に確かめる
            CaptureFileState? state = ReadState(path);
            return state.HasValue && Cleanup.Matches(record, state.Value) && NativeMethods.TryRecycleFile(path);
        }
    }
}
