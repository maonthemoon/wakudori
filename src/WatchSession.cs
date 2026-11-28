using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Wakudori
{
    // 1回の見張り. StartとStopはUIスレッドで呼ぶ.
    internal sealed class WatchSession
    {
        private const int IntervalMs = 1000;
        private const int MaxDurationHours = 2;
        private const int MaxShots = 500;
        private static readonly TimeSpan MaxDuration = TimeSpan.FromHours(MaxDurationHours);

        private readonly Rectangle region;
        private readonly string saveDir;
        private readonly WatchDetector detector = new WatchDetector();
        private readonly Stopwatch elapsed = new Stopwatch();
        private DateTime startedAt;
        private bool started;
        private Timer timer;
        private WatchFrameForm frame;
        private byte[] bgra;
        private string savedFolder;
        private bool skippedBlack;
        private bool loggedCaptureError;

        public string RegionName { get; private set; }
        public int ShotCount { get; private set; }
        public event EventHandler ShotSaved;
        public event EventHandler<WatchFinishedEventArgs> Finished;

        public WatchSession(Rectangle region, string saveDir, string regionName)
        {
            this.region = region;
            this.saveDir = saveDir;
            RegionName = regionName;
        }

        public void Start()
        {
            if (started) return;
            started = true;
            startedAt = DateTime.Now;
            elapsed.Start();
            try
            {
                frame = new WatchFrameForm(region);
                frame.UpdateStatus(ShotCount, TimeSpan.Zero, false);
                frame.Show();
                timer = new Timer { Interval = IntervalMs };
                timer.Tick += OnTick;
                timer.Start();
            }
            catch (Exception ex)
            {
                Fail("WatchSession.Start", ex);
                return;
            }

            // 開始時点を最初の候補にし, 約2秒後に2回目の一致を確認する.
            OnTick(timer, EventArgs.Empty);
        }

        public void Stop(WatchStopReason reason)
        {
            Finish(reason, null);
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (elapsed.Elapsed >= MaxDuration)
            {
                Stop(WatchStopReason.MaxDuration);
                return;
            }

            bool saved;
            try
            {
                saved = CaptureAndSave();
                frame.UpdateStatus(ShotCount, elapsed.Elapsed, saved);
            }
            catch (Exception ex)
            {
                Fail("WatchSession.Tick", ex);
                return;
            }

            if (saved && ShotSaved != null) ShotSaved(this, EventArgs.Empty);

            if (ShotCount >= MaxShots)
            {
                // 次の撮影を止め, 最後の1枚の合図が終わったら窓を閉じる.
                timer.Stop();
                frame.FlashEnded += OnLastFlashEnded;
            }
            else if (elapsed.Elapsed >= MaxDuration) Stop(WatchStopReason.MaxDuration);
        }

        private void OnLastFlashEnded(object sender, EventArgs e)
        {
            Stop(WatchStopReason.MaxShots);
        }

        private bool CaptureAndSave()
        {
            Bitmap image;
            try
            {
                image = CaptureOutput.CaptureScreen(region);
            }
            catch (Exception ex)
            {
                detector.ResetCandidate();
                if (!loggedCaptureError)
                {
                    loggedCaptureError = true;
                    Program.LogError("WatchSession.CaptureScreen", ex);
                }
                return false;
            }

            using (image)
            {
                WatchResult result = detector.Feed(ReadThumb(image));
                if (result == WatchResult.Black) skippedBlack = true;
                if (result != WatchResult.Capture) return false;

                SaveShot(image);
                ShotCount++;
                return true;
            }
        }

        private WatchThumb ReadThumb(Bitmap image)
        {
            int rowBytes = checked(image.Width * 4);
            if (bgra == null) bgra = new byte[checked(rowBytes * image.Height)];

            BitmapData bits = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                if (bits.Stride == rowBytes)
                {
                    Marshal.Copy(bits.Scan0, bgra, 0, bgra.Length);
                }
                else
                {
                    // 負のstrideでも, 判定には上から順に並べた行を渡す.
                    for (int y = 0; y < image.Height; y++)
                        Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), bgra, y * rowBytes, rowBytes);
                }
            }
            finally
            {
                image.UnlockBits(bits);
            }
            return WatchDetector.Downsample(bgra, image.Width, image.Height, rowBytes);
        }

        private void SaveShot(Bitmap image)
        {
            string fileName = FileNaming.BuildWatchFileName(ShotCount + 1);
            if (savedFolder == null)
            {
                savedFolder = SaveFirstShot(image, fileName);
                return;
            }

            // 書き込みが終わってから連番の名前にする. 既存の画像があれば上書きしない.
            string temporaryFile = Path.Combine(savedFolder, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                image.Save(temporaryFile, ImageFormat.Png);
                File.Move(temporaryFile, Path.Combine(savedFolder, fileName));
            }
            catch
            {
                try { File.Delete(temporaryFile); }
                catch (Exception ex) { Program.LogError("WatchSession.DeleteTemporaryFile", ex); }
                throw;
            }
        }

        private string SaveFirstShot(Bitmap image, string fileName)
        {
            string parent = Path.GetFullPath(saveDir);
            // 1枚目を書き終えてから確定名にする. 空の見張りフォルダーを残さない.
            string temporaryFolder = Path.Combine(parent, Guid.NewGuid().ToString("N") + ".tmp");
            string temporaryFile = Path.Combine(temporaryFolder, fileName);
            Directory.CreateDirectory(temporaryFolder);
            try
            {
                image.Save(temporaryFile, ImageFormat.Png);
                for (;;)
                {
                    string folder = FileNaming.BuildWatchFolderPath(parent, startedAt, PathExists);
                    try
                    {
                        Directory.Move(temporaryFolder, folder);
                        return folder;
                    }
                    catch (IOException)
                    {
                        // 名前を決めた直後に同名のファイルかフォルダーができたときは選び直す.
                        if (!PathExists(folder)) throw;
                    }
                }
            }
            catch
            {
                try
                {
                    File.Delete(temporaryFile);
                    Directory.Delete(temporaryFolder);
                }
                catch (Exception ex) { Program.LogError("WatchSession.DeleteTemporaryFolder", ex); }
                throw;
            }
        }

        private static bool PathExists(string path)
        {
            return Directory.Exists(path) || File.Exists(path);
        }

        private void Fail(string where, Exception ex)
        {
            Program.LogError(where, ex);
            Finish(WatchStopReason.Error, ex.Message);
        }

        private void Finish(WatchStopReason reason, string errorMessage)
        {
            if (!elapsed.IsRunning) return;
            DateTime endedAt = DateTime.Now;
            elapsed.Stop();
            if (timer != null)
            {
                timer.Dispose();
                timer = null;
            }
            if (frame != null)
            {
                frame.Dispose();
                frame = null;
            }
            bgra = null;

            if (ShotCount > 0)
            {
                try { CaptureLog.AppendFolder(CaptureLog.DefaultPath, endedAt, ShotCount, savedFolder); }
                catch (Exception ex) { Program.LogError("CaptureLog.AppendFolder", ex); }
            }

            EventHandler<WatchFinishedEventArgs> handler = Finished;
            if (handler != null)
                handler(this, new WatchFinishedEventArgs(reason, ShotCount, savedFolder, skippedBlack, errorMessage));
        }
    }
}
