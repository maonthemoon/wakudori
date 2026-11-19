using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Wakudori
{
    // 選択画面の下に出す操作説明のカード(B12). アイボリーの角丸のカードに, キーをキーボードのキーの形で並べる.
    // クレイ色はEnterのキーの1か所だけ. 選択画面(レイヤードウィンドウ)に貼るので, 透明度つきの画像として作る.
    internal static class HelpCard
    {
        private sealed class Item
        {
            public string[] Keys;
            public string Label;
            public bool Accent;

            public Item(string label, bool accent, params string[] keys)
            {
                Label = label;
                Accent = accent;
                Keys = keys;
            }
        }

        private const string Caption = "辺・角のドラッグで大きさ, 内側のドラッグで位置を変える. 辺・角をクリックすると, 矢印キーでその辺を動かせる.";

        public static Bitmap Create(float scale)
        {
            Func<float, int> s = delegate(float v) { return Math.Max(1, (int)Math.Round(v * scale)); };
            Item[] items =
            {
                new Item("撮影", true, "Enter"),
                new Item("閉じる / 選択を外す", false, "Esc"),
                new Item("1px動かす", false, "←", "↑", "↓", "→"),
                new Item("+ 矢印で10px", false, "Ctrl")
            };

            int padX = s(20), padY = s(16);
            int capH = s(26), capMinW = s(26), capPadX = s(8), capGap = s(4), lift = Math.Max(1, s(2));
            int labelGap = s(8), itemGap = s(24), rowGap = s(12);

            using (Font capFont = Theme.BodyStrong(s(13)))
            using (Font labelFont = Theme.Body(s(14)))
            using (Font captionFont = Theme.Body(s(14)))
            using (StringFormat fmt = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

                // 1回目: 大きさを測る
                int[][] capW = new int[items.Length][];
                int[] labelW = new int[items.Length];
                int row1W = 0;
                int captionMax;
                SizeF captionSize;
                using (Bitmap probe = new Bitmap(1, 1))
                using (Graphics m = Graphics.FromImage(probe))
                {
                    m.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    for (int i = 0; i < items.Length; i++)
                    {
                        if (i > 0) row1W += itemGap;
                        capW[i] = new int[items[i].Keys.Length];
                        for (int k = 0; k < items[i].Keys.Length; k++)
                        {
                            int tw = (int)Math.Ceiling(m.MeasureString(items[i].Keys[k], capFont, PointF.Empty, fmt).Width);
                            capW[i][k] = Math.Max(capMinW, tw + capPadX * 2);
                            row1W += capW[i][k] + (k > 0 ? capGap : 0);
                        }
                        labelW[i] = (int)Math.Ceiling(m.MeasureString(items[i].Label, labelFont, PointF.Empty, fmt).Width);
                        row1W += labelGap + labelW[i];
                    }
                    captionMax = Math.Max(row1W, s(560));
                    captionSize = m.MeasureString(Caption, captionFont, captionMax, fmt);
                }

                int contentW = Math.Max(row1W, (int)Math.Ceiling(captionSize.Width));
                int width = contentW + padX * 2;
                int height = padY + capH + lift + rowGap + (int)Math.Ceiling(captionSize.Height) + padY;

                // 2回目: 描く
                Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; // ClearTypeは透明度つきの画像だと汚くなる

                    using (GraphicsPath card = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, width - 1.5f, height - 1.5f), 12 * scale))
                    using (SolidBrush bg = new SolidBrush(Theme.Bg))
                    using (Pen border = new Pen(Theme.Border))
                    {
                        g.FillPath(bg, card);
                        g.DrawPath(border, card);
                    }

                    float x = padX;
                    float y = padY;
                    using (SolidBrush labelBrush = new SolidBrush(Theme.TextSecondary))
                    {
                        for (int i = 0; i < items.Length; i++)
                        {
                            if (i > 0) x += itemGap;
                            for (int k = 0; k < items[i].Keys.Length; k++)
                            {
                                if (k > 0) x += capGap;
                                DrawKeycap(g, new RectangleF(x, y, capW[i][k], capH), lift, items[i].Keys[k], capFont, fmt, items[i].Accent, scale);
                                x += capW[i][k];
                            }
                            x += labelGap;
                            SizeF ls = g.MeasureString(items[i].Label, labelFont, PointF.Empty, fmt);
                            g.DrawString(items[i].Label, labelFont, labelBrush, x, y + (capH - ls.Height) / 2f, fmt);
                            x += labelW[i];
                        }
                    }

                    using (SolidBrush captionBrush = new SolidBrush(Theme.TextTertiary))
                    {
                        RectangleF rc = new RectangleF(padX, padY + capH + lift + rowGap, contentW + 1, captionSize.Height + 1);
                        g.DrawString(Caption, captionFont, captionBrush, rc, fmt);
                    }
                }
                return bmp;
            }
        }

        // 下側を少し厚くして, キーボードのキーに見せる(影は使わない).
        private static void DrawKeycap(Graphics g, RectangleF rc, int lift, string text, Font font, StringFormat fmt, bool accent, float scale)
        {
            Color face = accent ? Theme.AccentInteractive : Theme.Surface;
            Color edge = accent ? Theme.AccentDeep : Theme.Border;
            Color fore = accent ? Color.White : Theme.Text;
            float r = 6 * scale;
            RectangleF cap = new RectangleF(rc.X + 0.5f, rc.Y + 0.5f, rc.Width - 1, rc.Height - 1);
            RectangleF bottom = cap;
            bottom.Offset(0, lift);
            using (GraphicsPath basePath = Theme.RoundedRect(bottom, r))
            using (GraphicsPath capPath = Theme.RoundedRect(cap, r))
            using (SolidBrush baseBrush = new SolidBrush(edge))
            using (SolidBrush faceBrush = new SolidBrush(face))
            using (Pen border = new Pen(edge))
            using (SolidBrush textBrush = new SolidBrush(fore))
            {
                g.FillPath(baseBrush, basePath);
                g.FillPath(faceBrush, capPath);
                g.DrawPath(border, capPath);
                SizeF ts = g.MeasureString(text, font, PointF.Empty, fmt);
                g.DrawString(text, font, textBrush, rc.X + (rc.Width - ts.Width) / 2f, rc.Y + (rc.Height - ts.Height) / 2f, fmt);
            }
        }
    }
}
