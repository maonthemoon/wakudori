using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Wakudori
{
    // 撮影と出力(クリップボード, PNG)
    internal static class CaptureOutput
    {
        // 物理ピクセルの仮想スクリーン座標rを撮る
        public static Bitmap CaptureScreen(Rectangle r)
        {
            Bitmap bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(r.Location, Point.Empty, r.Size, CopyPixelOperation.SourceCopy);
                }
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
            return bmp;
        }

        // D1: プロセス終了後も残るようにコピー. ロック中は再試行. STAスレッドで呼ぶ
        public static void CopyToClipboard(Bitmap bmp)
        {
            using (MemoryStream png = new MemoryStream())
            {
                bmp.Save(png, ImageFormat.Png);
                DataObject data = new DataObject();
                data.SetData(DataFormats.Bitmap, true, bmp);
                data.SetData("PNG", false, png);
                for (int i = 0; ; i++)
                {
                    try
                    {
                        Clipboard.SetDataObject(data, true);
                        return;
                    }
                    catch (ExternalException)
                    {
                        if (i >= 4) throw;
                        Thread.Sleep(100);
                    }
                }
            }
        }

        // D2/D3: 保存先を作り, 重複しない名前でPNG保存. フルパスを返す
        public static string SavePng(Bitmap bmp, string saveDir, string regionName, DateTime time)
        {
            Directory.CreateDirectory(saveDir);
            string name = FileNaming.BuildFileName(regionName, time);
            string path = FileNaming.UniquePath(saveDir, name, new Func<string, bool>(File.Exists));
            bmp.Save(path, ImageFormat.Png);
            LogSavedImage(path, time);
            return path;
        }

        // 書き込んだ画像を, 元の画像と同じフォルダーに<元の名前>_edit.pngで保存する(H5). 元の画像は変えない
        public static string SaveEditedPng(Bitmap bmp, string originalPath)
        {
            string dir = Path.GetDirectoryName(originalPath);
            Directory.CreateDirectory(dir);
            string path = FileNaming.UniquePath(dir, FileNaming.BuildEditName(originalPath), new Func<string, bool>(File.Exists));
            bmp.Save(path, ImageFormat.Png);
            LogSavedImage(path, DateTime.Now);
            return path;
        }

        private static void LogSavedImage(string path, DateTime time)
        {
            try
            {
                FileInfo file = new FileInfo(path);
                CaptureLog.Append(CaptureLog.DefaultPath, new CaptureRecord(time, file.Length, file.LastWriteTimeUtc.Ticks, file.FullName));
            }
            catch (Exception ex)
            {
                Program.LogError("CaptureLog.Append", ex);
            }
        }
    }
}
