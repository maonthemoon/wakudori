using System;
using Wakudori;

internal static class Program
{
    static int fail;

    static void Check(bool ok, string name)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) fail++;
    }

    static void FillRect(byte[] bgra, int stride, int left, int top, int width, int height,
        byte red, byte green, byte blue)
    {
        for (int y = top; y < top + height; y++)
        {
            for (int x = left; x < left + width; x++)
            {
                int i = y * stride + x * 4;
                bgra[i] = blue;
                bgra[i + 1] = green;
                bgra[i + 2] = red;
                bgra[i + 3] = 255;
            }
        }
    }

    static byte[] SolidBgra(int width, int height, byte shade)
    {
        byte[] bgra = new byte[width * height * 4];
        FillRect(bgra, width * 4, 0, 0, width, height, shade, shade, shade);
        return bgra;
    }

    static WatchThumb Thumb(byte[] bgra, int width, int height)
    {
        return WatchDetector.Downsample(bgra, width, height, width * 4);
    }

    static WatchThumb SolidThumb(byte shade)
    {
        return Thumb(SolidBgra(40, 40, shade), 40, 40);
    }

    static bool HasRgb(WatchThumb thumb, int x, int y, int red, int green, int blue)
    {
        int offset = (y * thumb.Width + x) * 3;
        return thumb.Rgb[offset] == red && thumb.Rgb[offset + 1] == green && thumb.Rgb[offset + 2] == blue;
    }

    static void TestDownsample()
    {
        WatchThumb narrow = Thumb(SolidBgra(1000, 500, 120), 1000, 500);
        Check(narrow.Width == 500 && narrow.Height == 250, "1000x500 uses an integer scale of 2");

        byte[] small = SolidBgra(3, 2, 0);
        FillRect(small, 12, 2, 1, 1, 1, 11, 22, 33);
        WatchThumb sameSize = Thumb(small, 3, 2);
        Check(sameSize.Width == 3 && sameSize.Height == 2 && sameSize.Rgb.Length == 18
            && HasRgb(sameSize, 2, 1, 11, 22, 33), "small images keep dimensions and RGB channel order");

        const int stride = 28;
        byte[] odd = new byte[stride * 541];
        FillRect(odd, stride, 0, 0, 1, 1, 9, 20, 4);
        FillRect(odd, stride, 1, 0, 1, 1, 0, 12, 8);
        FillRect(odd, stride, 0, 1, 1, 1, 2, 4, 20);
        FillRect(odd, stride, 1, 1, 1, 1, 0, 0, 0);
        odd[20] = 255;
        odd[21] = 255;
        odd[22] = 255;
        FillRect(odd, stride, 4, 0, 1, 1, 80, 60, 40);
        FillRect(odd, stride, 0, 540, 1, 1, 30, 50, 70);
        FillRect(odd, stride, 4, 540, 1, 1, 11, 22, 33);
        WatchThumb reduced = WatchDetector.Downsample(odd, 5, 541, stride);
        Check(reduced.Width == 3 && reduced.Height == 271, "height sets the scale and odd edges are retained");
        Check(HasRgb(reduced, 0, 0, 2, 9, 8), "all four pixels contribute to the integer RGB average");
        Check(HasRgb(reduced, 2, 0, 40, 30, 20), "right edge averages two pixels and skips row padding");
        Check(HasRgb(reduced, 0, 270, 15, 25, 35), "bottom edge averages two pixels");
        Check(HasRgb(reduced, 2, 270, 11, 22, 33), "bottom right edge keeps its single pixel");
    }

    static void TestSmallChangesAndNoise()
    {
        const int width = 1920;
        const int height = 1080;
        byte[] image = SolidBgra(width, height, 255);
        WatchThumb white = Thumb(image, width, height);
        Check(white.Width == 960 && white.Height == 540, "1920x1080 reduces to 960x540");

        // 入力のカーソル(150%の画面で幅3, 高さ30くらい)の点滅は撮らない.
        FillRect(image, width * 4, 501, 301, 3, 30, 0, 0, 0);
        Check(!WatchDetector.Changed(white, Thumb(image, width, height)), "a blinking text caret is ignored");

        // マウスのポインタ(32x32)が別の場所へ動いても撮らない. 前の位置と今の位置の両方が変わる.
        image = SolidBgra(width, height, 255);
        FillRect(image, width * 4, 200, 200, 32, 32, 0, 0, 0);
        WatchThumb before = Thumb(image, width, height);
        FillRect(image, width * 4, 200, 200, 32, 32, 255, 255, 255);
        FillRect(image, width * 4, 1400, 700, 32, 32, 0, 0, 0);
        Check(!WatchDetector.Changed(before, Thumb(image, width, height)), "a moving mouse pointer is ignored");

        // 箇条書きが1行増える程度(600x24)は撮る.
        image = SolidBgra(width, height, 255);
        FillRect(image, width * 4, 200, 500, 600, 24, 40, 40, 40);
        Check(WatchDetector.Changed(white, Thumb(image, width, height)), "a new 600x24 line of text is detected");

        const int noiseWidth = 101;
        byte[] noisy = SolidBgra(noiseWidth, noiseWidth, 120);
        WatchThumb original = Thumb(noisy, noiseWidth, noiseWidth);
        Random noise = new Random(1);
        for (int i = 0; i < noisy.Length; i += 4)
        {
            for (int channel = 0; channel < 3; channel++)
                noisy[i + channel] = (byte)(120 + noise.Next(-10, 11));
        }
        Check(!WatchDetector.Changed(original, Thumb(noisy, noiseWidth, noiseWidth)),
            "RGB noise within 10 is ignored");
    }

    static void TestE2eSizes()
    {
        // e2e(800x600の範囲. 2分の1に縮めて400x300)と同じ大きさで, 撮る・撮らないを確かめる.
        const int width = 800;
        const int height = 600;
        byte[] image = SolidBgra(width, height, 0);
        FillRect(image, width * 4, 0, 0, width, height, 0, 0, 255);
        WatchThumb blue = Thumb(image, width, height);

        FillRect(image, width * 4, 101, 101, 24, 24, 0, 0, 0);
        Check(!WatchDetector.Changed(blue, Thumb(image, width, height)), "the e2e pointer-sized square is ignored");
        FillRect(image, width * 4, 201, 281, 400, 40, 255, 0, 0);
        Check(WatchDetector.Changed(blue, Thumb(image, width, height)), "the e2e red band is detected");
    }

    static void TestThresholds()
    {
        const int width = 101;
        const int stride = width * 4;
        byte[] image = SolidBgra(width, width, 100);
        WatchThumb original = Thumb(image, width, width);
        FillRect(image, stride, 10, 3, 30, 1, 123, 100, 100);
        Check(!WatchDetector.Changed(original, Thumb(image, width, width)), "per-channel difference of 23 is ignored");

        const int side = 100;
        byte[] scattered = SolidBgra(side, side, 100);
        WatchThumb plain = Thumb(scattered, side, side);
        for (int index = 0; index < 19; index++)
            FillRect(scattered, side * 4, (index % 10) * 10, (index / 10) * 10, 1, 1, 100, 100, 124);
        Check(!WatchDetector.Changed(plain, Thumb(scattered, side, side)), "just under 0.2% changes are ignored");
        FillRect(scattered, side * 4, 95, 55, 1, 1, 100, 100, 124);
        Check(WatchDetector.Changed(plain, Thumb(scattered, side, side)), "exactly 0.2% changes are detected");

        Check(WatchDetector.Changed(Thumb(new byte[8], 2, 1), Thumb(new byte[8], 1, 2)),
            "different dimensions count as a change even with identical pixels");
    }

    static void TestState()
    {
        WatchDetector detector = new WatchDetector();
        WatchThumb a = SolidThumb(80);
        WatchThumb b = SolidThumb(140);
        WatchThumb c = SolidThumb(210);

        Check(detector.Feed(a) == WatchResult.None, "first frame starts a candidate");
        Check(detector.Feed(a) == WatchResult.None, "first match does not capture");
        Check(detector.Feed(a) == WatchResult.Capture, "first image is captured on feed 3");
        Check(detector.Feed(a) == WatchResult.None && detector.Feed(a) == WatchResult.None
            && detector.Feed(a) == WatchResult.None, "unchanged frames do not cause another capture");

        Check(detector.Feed(b) == WatchResult.None && detector.Feed(b) == WatchResult.None
            && detector.Feed(b) == WatchResult.Capture, "a changed image is captured on its third feed");

        Check(detector.Feed(c) == WatchResult.None && detector.Feed(c) == WatchResult.None
            && detector.Feed(b) == WatchResult.None, "returning to the reference discards a brief change");
        Check(detector.Feed(c) == WatchResult.None && detector.Feed(c) == WatchResult.None,
            "a discarded candidate starts counting again");
        Check(detector.Feed(a) == WatchResult.None && detector.Feed(a) == WatchResult.None
            && detector.Feed(a) == WatchResult.Capture, "a new candidate waits for two matches and can revisit an old image");
    }

    static void TestInitialSlideBeforePageTurn()
    {
        WatchDetector detector = new WatchDetector();
        WatchThumb firstSlide = SolidThumb(80);
        WatchThumb nextSlide = SolidThumb(140);

        Check(detector.Feed(firstSlide) == WatchResult.None, "the sample at startup starts the first slide candidate");
        Check(detector.Feed(firstSlide) == WatchResult.None, "the one-second sample is the first match");
        Check(detector.Feed(firstSlide) == WatchResult.Capture,
            "the two-second sample captures the first slide before the page turns at 2.5 seconds");
        Check(detector.Feed(nextSlide) == WatchResult.None, "the three-second sample starts the next slide candidate");
    }

    static void TestCaptureFailureResetsCandidate()
    {
        WatchDetector detector = new WatchDetector();
        WatchThumb a = SolidThumb(80);
        WatchThumb b = SolidThumb(140);

        Check(detector.Feed(a) == WatchResult.None && detector.Feed(a) == WatchResult.None
            && detector.Feed(a) == WatchResult.Capture, "capture a reference before a failed screen capture");
        Check(detector.Feed(b) == WatchResult.None && detector.Feed(b) == WatchResult.None,
            "the changed frame has one match before a failed screen capture");
        detector.ResetCandidate();
        Check(detector.Feed(b) == WatchResult.None, "the first frame after a failed screen capture starts a new candidate");
        Check(detector.Feed(b) == WatchResult.None, "the new candidate still needs a second match");
        Check(detector.Feed(b) == WatchResult.Capture, "two matches after a failed screen capture allow saving");

        detector.ResetCandidate();
        Check(detector.Feed(b) == WatchResult.None && detector.Feed(b) == WatchResult.None
            && detector.Feed(b) == WatchResult.None, "a failed screen capture preserves the last captured reference");
    }

    static void TestBlackAndGradualChanges()
    {
        WatchThumb black = SolidThumb(0);
        byte[] darkImage = SolidBgra(40, 40, 0);
        FillRect(darkImage, 160, 39, 39, 1, 1, 0, 0, 1);
        WatchThumb nearBlack = Thumb(darkImage, 40, 40);
        Check(WatchDetector.IsBlack(black) && !WatchDetector.IsBlack(nearBlack),
            "black requires all RGB channels to be zero regardless of alpha");
        WatchDetector detector = new WatchDetector();
        Check(detector.Feed(black) == WatchResult.Black && detector.Feed(black) == WatchResult.Black
            && detector.Feed(black) == WatchResult.Black, "initial black frames never capture");
        Check(detector.Feed(nearBlack) == WatchResult.None && detector.Feed(nearBlack) == WatchResult.None
            && detector.Feed(nearBlack) == WatchResult.Capture, "a nonzero dark image can be captured");
        WatchThumb bright = SolidThumb(100);
        Check(detector.Feed(bright) == WatchResult.None && detector.Feed(bright) == WatchResult.None
            && detector.Feed(black) == WatchResult.Black, "black discards a pending candidate");
        Check(detector.Feed(bright) == WatchResult.None && detector.Feed(bright) == WatchResult.None,
            "a candidate discarded by black needs two new matches");
        Check(detector.Feed(black) == WatchResult.Black, "black can reset a second candidate");
        Check(detector.Feed(nearBlack) == WatchResult.None && detector.Feed(nearBlack) == WatchResult.None
            && detector.Feed(nearBlack) == WatchResult.None, "black preserves the captured reference");
        Check(detector.Feed(bright) == WatchResult.None && detector.Feed(bright) == WatchResult.None
            && detector.Feed(bright) == WatchResult.Capture, "capture resumes after black");

        Check(detector.Feed(SolidThumb(108)) == WatchResult.None && detector.Feed(SolidThumb(116)) == WatchResult.None
            && detector.Feed(SolidThumb(124)) == WatchResult.None && detector.Feed(SolidThumb(130)) == WatchResult.None
            && detector.Feed(SolidThumb(137)) == WatchResult.Capture, "gradual changes accumulate against the captured frame");
        WatchThumb afterCapture = SolidThumb(150);
        Check(detector.Feed(afterCapture) == WatchResult.None && detector.Feed(afterCapture) == WatchResult.None
            && detector.Feed(afterCapture) == WatchResult.None, "the reference is the frame returned as Capture");

        detector = new WatchDetector();
        WatchThumb settled = SolidThumb(64);
        Check(detector.Feed(SolidThumb(40)) == WatchResult.None && detector.Feed(SolidThumb(52)) == WatchResult.None
            && detector.Feed(settled) == WatchResult.None && detector.Feed(settled) == WatchResult.None
            && detector.Feed(settled) == WatchResult.Capture, "stability is measured against the first frame of the candidate");
    }

    static void TestElapsedTime()
    {
        Check(WatchTime.FormatElapsed(TimeSpan.FromSeconds(5)) == "00:05", "elapsed time pads minutes and seconds");
        Check(WatchTime.FormatElapsed(TimeSpan.FromSeconds(3599)) == "59:59", "elapsed time before one hour");
        Check(WatchTime.FormatElapsed(TimeSpan.FromHours(1)) == "1:00:00", "elapsed time displays hours from one hour");
    }

    static void TestFinishedText()
    {
        Check(WatchDoneText.Heading(WatchStopReason.Hotkey) == "見張りを終えました", "manual stop heading");
        Check(WatchDoneText.Heading(WatchStopReason.MaxDuration) == "見張りを終えました(2時間たちました)", "time limit heading");
        Check(WatchDoneText.Heading(WatchStopReason.MaxShots) == "見張りを終えました(500枚になりました)", "shot limit heading");
        Check(WatchDoneText.Heading(WatchStopReason.Error) == "見張りの画像を保存できませんでした", "save error heading");

        const string saveDir = @"C:\画像";
        const string folder = saveDir + @"\見張り_20261128_090000";
        const string empty = "変化がなかったので, 撮った画像はありません.";
        const string protectedScreen = "保護された画面(真っ黒)は撮りませんでした.";
        WatchFinishedEventArgs saved = new WatchFinishedEventArgs(WatchStopReason.Hotkey, 3, folder, false, null);
        Check(WatchDoneText.Body(saved) == "3枚を撮りました.\r\n" + folder, "saved count and full folder path");
        Check(WatchDoneText.Hint(saved.ShotCount) == "クリックで保存先を開く · 数秒で消える", "saved images have an open-folder hint");

        WatchFinishedEventArgs unsaved = new WatchFinishedEventArgs(WatchStopReason.Hotkey, 0, null, false, null);
        Check(WatchDoneText.Body(unsaved) == empty, "zero shots have no folder line");
        Check(WatchDoneText.Hint(unsaved.ShotCount) == "数秒で消える", "zero shots have only the auto-dismiss hint");

        WatchFinishedEventArgs black = new WatchFinishedEventArgs(WatchStopReason.MaxDuration, 0, null, true, null);
        Check(WatchDoneText.Body(black) == empty + "\r\n" + protectedScreen, "skipped black is reported even with zero shots");
        WatchFinishedEventArgs mixed = new WatchFinishedEventArgs(WatchStopReason.MaxShots, 500, folder, true, null);
        Check(WatchDoneText.Body(mixed) == "500枚を撮りました.\r\n" + folder + "\r\n" + protectedScreen,
            "skipped black is reported alongside saved shots");

        const string error = "ディスクに空きがありません.";
        WatchFinishedEventArgs partial = new WatchFinishedEventArgs(WatchStopReason.Error, 3, folder, false, error);
        Check(WatchDoneText.ErrorBody(partial, saveDir) == "3枚を撮りました.\r\n" + folder + "\r\n\r\n" + error,
            "save error includes the saved count, session folder, and reason");
        WatchFinishedEventArgs firstError = new WatchFinishedEventArgs(WatchStopReason.Error, 0, null, false, error);
        Check(WatchDoneText.ErrorBody(firstError, saveDir) == "0枚を撮りました.\r\n保存先(設定): " + saveDir + "\r\n\r\n" + error,
            "first shot error includes the configured folder when the session folder does not exist");
    }

    static int Main()
    {
        TestDownsample();
        TestSmallChangesAndNoise();
        TestE2eSizes();
        TestThresholds();
        TestState();
        TestInitialSlideBeforePageTurn();
        TestCaptureFailureResetsCandidate();
        TestBlackAndGradualChanges();
        TestElapsedTime();
        TestFinishedText();
        Console.WriteLine(fail == 0 ? "ALL PASS" : fail + " FAILED");
        return fail == 0 ? 0 : 1;
    }
}
