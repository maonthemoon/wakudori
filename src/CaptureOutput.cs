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
            return path;
        }
    }
}
