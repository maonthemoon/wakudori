using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Wakudori
{
    internal enum CaptureMoveResult
    {
        Keep,
        Forget,
        Moved
    }

    internal static class CaptureRecycler
    {
        // ファイルがない場合と区別できるよう, 状態を読めない場合はnullを返す
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
            // ドライブや共有が使えない場合は, ファイルの有無を確かめられない
            return rootExists(Path.GetPathRoot(path)) ? new CaptureFileState() : (CaptureFileState?)null;
        }

        // フォルダーがない, または中身が違うときは既定値, 読めないときはnullを返す
        public static CaptureFolderState? ReadFolderState(string path, int recordedCount)
        {
            return ReadFolderState(path, recordedCount, Directory.Exists);
        }

        internal static CaptureFolderState? ReadFolderState(string path, int recordedCount, Func<string, bool> rootExists)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                    return new CaptureFolderState();

                // サブフォルダーやリンクをたどらず, 直下にある連番PNGだけを確認する
                int count = 0;
                foreach (FileSystemInfo entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
                {
                    if (!IsNumberedPng(entry.Name, recordedCount)
                        || (entry.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                        return new CaptureFolderState();
                    count++;
                }
                return new CaptureFolderState(count);
            }
            catch (FileNotFoundException)
            {
                return MissingFolderState(path, rootExists);
            }
            catch (DirectoryNotFoundException)
            {
                return MissingFolderState(path, rootExists);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        private static CaptureFolderState? MissingFolderState(string path, Func<string, bool> rootExists)
        {
            return rootExists(Path.GetPathRoot(path)) ? new CaptureFolderState() : (CaptureFolderState?)null;
        }

        private static bool IsNumberedPng(string name, int recordedCount)
        {
            if (name.Length < 7) return false;
            int number;
            return int.TryParse(name.Substring(0, name.Length - 4), NumberStyles.None, CultureInfo.InvariantCulture, out number)
                && number >= 1 && number <= recordedCount
                && string.Equals(name, number.ToString("000", CultureInfo.InvariantCulture) + ".png", StringComparison.Ordinal);
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

        public static CaptureMoveResult TryMove(CaptureRecord record, out int movedImages)
        {
            return TryMove(record, IsOnFixedDrive, NativeMethods.TryRecycleFile, out movedImages);
        }

        internal static CaptureMoveResult TryMove(CaptureRecord record, Func<string, bool> isOnFixedDrive,
            Func<string, bool> recycle, out int movedImages)
        {
            movedImages = 0;
            string path = record.FullPath;
            // シェルが複数のファイル名やワイルドカードとして解釈する文字を渡さない
            if (path.IndexOf('\0') >= 0 || path.IndexOf('*') >= 0 || path.IndexOf('?') >= 0) return CaptureMoveResult.Keep;

            // 選別後にドライブの接続が変わることがあるので, 移す前にもう一度確かめる
            bool fixedDrive = isOnFixedDrive(path);
            int images = 1;
            // 選別後に画像やフォルダーの中身が変わることがあるので, 移す直前に確かめる
            if (record.IsFolder)
            {
                CaptureFolderState? state = ReadFolderState(path, record.ImageCount);
                if (!state.HasValue) return CaptureMoveResult.Keep;
                if (!state.Value.HasOnlyCaptures || !fixedDrive) return CaptureMoveResult.Forget;
                images = state.Value.ImageCount;
            }
            else
            {
                if (!fixedDrive) return CaptureMoveResult.Forget;
                CaptureFileState? state = ReadState(path);
                if (!state.HasValue || !Cleanup.Matches(record, state.Value)) return CaptureMoveResult.Keep;
            }

            if (!recycle(path)) return CaptureMoveResult.Keep;
            movedImages = images;
            return CaptureMoveResult.Moved;
        }
    }
}
