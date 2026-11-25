using System;
using System.Drawing;
using Wakudori;

internal static class Program
{
    static int fail;

    static void Check(bool ok, string name)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) fail++;
    }

    static int Main()
    {
        // 元に戻す・やり直す
        MarkList list = new MarkList();
        MarkItem a = MarkItem.Pen(Color.Red, 4, new Point(1, 1)), b = MarkItem.Fill(), c = MarkItem.Fill();
        Check(!list.Undo() && !list.Redo(), "empty: nothing to undo/redo");
        list.Add(a); list.Add(b);
        Check(list.Undo() && list.Count == 1 && list.Items[0] == a, "undo removes the last");
        Check(list.Redo() && list.Count == 2 && list.Items[1] == b, "redo restores it");
        Check(!list.Redo(), "nothing more to redo");
        list.Undo(); list.Undo();
        Check(list.Count == 0, "undo all");
        Check(list.Redo() && list.Items[0] == a && list.Redo() && list.Items[1] == b, "redo keeps the order");
        list.Undo(); list.Add(c);
        Check(!list.Redo() && list.Count == 2 && list.Items[1] == c, "adding discards redo");

        // ペンの点
        Check(!a.AddPoint(new Point(1, 1)) && a.AddPoint(new Point(2, 1)) && a.Points.Count == 2, "same point is not added twice");

        // 塗りつぶしの四角
        Size img = new Size(100, 80);
        Check(MarkItem.RectBetween(new Point(10, 20), new Point(19, 29), img) == new Rectangle(10, 20, 10, 10), "rect includes both ends");
        Check(MarkItem.RectBetween(new Point(19, 29), new Point(10, 20), img) == new Rectangle(10, 20, 10, 10), "rect reversed");
        Check(MarkItem.RectBetween(new Point(50, 50), new Point(500, 500), img) == new Rectangle(50, 50, 50, 30), "rect is clipped to the image");
        Check(MarkItem.RectBetween(new Point(5, 5), new Point(-9, -9), img) == new Rectangle(0, 0, 6, 6), "rect is clipped at the top left");
        Check(b.Color.A == 255, "fill is opaque");

        // パレットの場所
        Size screen = new Size(1920, 1080), bar = new Size(300, 50);
        Rectangle all = new Rectangle(Point.Empty, screen);
        Rectangle image = new Rectangle(400, 200, 800, 600);
        Rectangle p = MarkLayout.PlacePalette(screen, image, bar, 16);
        Check(p == new Rectangle(650, 816, 300, 50), "palette below");
        image = new Rectangle(400, 400, 800, 680);
        p = MarkLayout.PlacePalette(screen, image, bar, 16);
        Check(p == new Rectangle(650, 334, 300, 50), "palette above");
        image = new Rectangle(0, 0, 1000, 1080);
        p = MarkLayout.PlacePalette(screen, image, bar, 16);
        Check(p.X == 1016 && !p.IntersectsWith(image) && all.Contains(p), "palette right");
        image = new Rectangle(920, 0, 1000, 1080);
        p = MarkLayout.PlacePalette(screen, image, bar, 16);
        Check(p.Right == 904 && !p.IntersectsWith(image) && all.Contains(p), "palette left");
        p = MarkLayout.PlacePalette(screen, all, bar, 16);
        Check(p == new Rectangle(810, 1014, 300, 50), "palette over a full-screen image");
        p = MarkLayout.PlacePalette(screen, new Rectangle(0, 0, 40, 40), bar, 16);
        Check(all.Contains(p) && p.X == 16, "palette stays on screen for an image at the corner");

        Console.WriteLine(fail == 0 ? "ALL PASS" : fail + " FAILED");
        return fail == 0 ? 0 : 1;
    }
}
