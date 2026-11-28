using System;

namespace Wakudori
{
    internal sealed class WatchThumb
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Rgb; // 左上から行順に並べたRGB. 判定中は書き換えない.

        public WatchThumb(int width, int height, byte[] rgb)
        {
            Width = width;
            Height = height;
            Rgb = rgb;
        }
    }

    internal enum WatchResult { None, Capture, Black }

    internal sealed class WatchDetector
    {
        private const int MaxThumbWidth = 960; // 縮小後の幅の上限.
        private const int MaxThumbHeight = 540; // 縮小後の高さの上限.
        private const int ChannelThreshold = 24; // 画素の変化とみなすRGBのうち最大の差.
        private const int TileSize = 16; // 局所的な変化を数える正方形の一辺.
        private const int TileStep = 8; // タイルを半分重ねるためにずらす画素数.
        private const int TileChangedPixels = 12; // タイルの変化とみなす画素数.
        private const int PixelsPerGlobalChange = 200; // 全体の1/200(0.5%)以上が変われば変化とみなす.
        private const int StableMatches = 2; // 最初の候補のあと, 同じ状態が続くのを確かめる回数.

        private WatchThumb reference;
        private WatchThumb candidate;
        private int matches;

        // BGRAの行は上から順に並び, strideは行末の余白を含むバイト数.
        public static WatchThumb Downsample(byte[] bgra, int width, int height, int stride)
        {
            if (bgra == null) throw new ArgumentNullException("bgra");
            if (width <= 0) throw new ArgumentOutOfRangeException("width");
            if (height <= 0) throw new ArgumentOutOfRangeException("height");
            long rowBytes = (long)width * 4;
            if (stride < rowBytes) throw new ArgumentOutOfRangeException("stride");
            if ((long)(height - 1) * stride + rowBytes > bgra.Length)
                throw new ArgumentException("BGRAの画素データが足りません.", "bgra");

            int scale = Math.Max(DivideRoundUp(width, MaxThumbWidth), DivideRoundUp(height, MaxThumbHeight));
            int thumbWidth = DivideRoundUp(width, scale);
            int thumbHeight = DivideRoundUp(height, scale);
            byte[] rgb = new byte[thumbWidth * thumbHeight * 3];
            int dest = 0;
            for (int y = 0; y < thumbHeight; y++)
            {
                int top = y * scale;
                int blockHeight = Math.Min(scale, height - top);
                for (int x = 0; x < thumbWidth; x++)
                {
                    int left = x * scale;
                    int blockWidth = Math.Min(scale, width - left);
                    long red = 0, green = 0, blue = 0;
                    for (int dy = 0; dy < blockHeight; dy++)
                    {
                        int source = (top + dy) * stride + left * 4;
                        for (int dx = 0; dx < blockWidth; dx++, source += 4)
                        {
                            red += bgra[source + 2];
                            green += bgra[source + 1];
                            blue += bgra[source];
                        }
                    }
                    int count = blockWidth * blockHeight;
                    rgb[dest++] = (byte)(red / count);
                    rgb[dest++] = (byte)(green / count);
                    rgb[dest++] = (byte)(blue / count);
                }
            }
            return new WatchThumb(thumbWidth, thumbHeight, rgb);
        }

        public static bool Changed(WatchThumb a, WatchThumb b)
        {
            if (a.Width != b.Width || a.Height != b.Height) return true;

            int pixelCount = a.Width * a.Height;
            int globalThreshold = DivideRoundUp(pixelCount, PixelsPerGlobalChange);
            int changedCount = 0;
            bool[] changed = null;
            for (int pixel = 0, offset = 0; pixel < pixelCount; pixel++, offset += 3)
            {
                if (Math.Abs(a.Rgb[offset] - b.Rgb[offset]) < ChannelThreshold
                    && Math.Abs(a.Rgb[offset + 1] - b.Rgb[offset + 1]) < ChannelThreshold
                    && Math.Abs(a.Rgb[offset + 2] - b.Rgb[offset + 2]) < ChannelThreshold)
                    continue;

                changedCount++;
                if (changedCount >= globalThreshold) return true;
                if (changed == null) changed = new bool[pixelCount];
                changed[pixel] = true;
            }
            if (changedCount < TileChangedPixels) return false;

            // 端のタイルは右端・下端にそろえる. 画像が小さい軸では画像の幅・高さを使う.
            int tileWidth = Math.Min(TileSize, a.Width);
            int tileHeight = Math.Min(TileSize, a.Height);
            int lastTileX = a.Width - tileWidth;
            int lastTileY = a.Height - tileHeight;
            for (int top = 0; ; top = Math.Min(top + TileStep, lastTileY))
            {
                for (int left = 0; ; left = Math.Min(left + TileStep, lastTileX))
                {
                    int tileCount = 0;
                    for (int y = top; y < top + tileHeight; y++)
                    {
                        int row = y * a.Width;
                        for (int x = left; x < left + tileWidth; x++)
                        {
                            if (changed[row + x] && ++tileCount >= TileChangedPixels) return true;
                        }
                    }
                    if (left == lastTileX) break;
                }
                if (top == lastTileY) break;
            }
            return false;
        }

        public static bool IsBlack(WatchThumb t)
        {
            foreach (byte channel in t.Rgb)
            {
                if (channel != 0) return false;
            }
            return true;
        }

        public WatchResult Feed(WatchThumb t)
        {
            if (IsBlack(t))
            {
                candidate = null;
                return WatchResult.Black;
            }
            // 基準は直前に撮った画像に固定し, 少しずつ進む変化も拾う.
            if (reference != null && !Changed(reference, t))
            {
                candidate = null;
                return WatchResult.None;
            }
            if (candidate == null || Changed(candidate, t))
            {
                candidate = t;
                matches = 0;
                return WatchResult.None;
            }

            matches++;
            if (matches < StableMatches) return WatchResult.None;

            reference = t;
            candidate = null;
            return WatchResult.Capture;
        }

        private static int DivideRoundUp(int positiveValue, int divisor)
        {
            return 1 + (positiveValue - 1) / divisor;
        }
    }
}
