using System;
using System.Drawing;
using Wakudori;

internal static class Program
{
    static int pass, fail;

    static void Check(bool ok, string name)
    {
        if (ok) { pass++; Console.WriteLine("PASS " + name); }
        else { fail++; Console.WriteLine("FAIL " + name); }
    }

    static void Eq(Rectangle expected, Rectangle actual, string name)
    {
        bool ok = expected == actual;
        Check(ok, ok ? name : name + " expected " + expected + " actual " + actual);
    }

    static int Main()
    {
        Rectangle a = new Rectangle(0, 0, 1920, 1080);
        Rectangle left = new Rectangle(-1280, 0, 1280, 1024);   // 負座標のモニター
        Rectangle small = new Rectangle(1920, 0, 800, 600);
        Rectangle[] one = { a };
        Rectangle[] two = { a, left };
        Rectangle[] three = { a, small };
        Size def = new Size(800, 600);

        // InitialFrame
        Eq(new Rectangle(100, 100, 300, 200), Selection.InitialFrame(one, new Rectangle(100, 100, 300, 200), new Point(5, 5), def), "initial: last valid");
        Eq(new Rectangle(560, 240, 800, 600), Selection.InitialFrame(one, null, new Point(5, 5), def), "initial: no last -> center");
        Eq(new Rectangle(810, 440, 300, 200), Selection.InitialFrame(one, new Rectangle(5000, 5000, 300, 200), new Point(5, 5), def), "initial: last offscreen same size");
        Eq(new Rectangle(-1280 + 240, 212, 800, 600), Selection.InitialFrame(two, null, new Point(-10, 10), def), "initial: negative monitor");
        Eq(new Rectangle(0, 0, 640, 480), Selection.InitialFrame(new Rectangle[] { new Rectangle(0, 0, 640, 480) }, null, new Point(1, 1), def), "initial: default bigger than monitor");
        Eq(new Rectangle(-1280, 0, 1280, 1024), Selection.InitialFrame(two, new Rectangle(0, 0, 3000, 3000), new Point(-1, 1), def), "initial: last larger than monitor shrinks");
        Eq(new Rectangle(560, 240, 800, 600), Selection.InitialFrame(one, null, new Point(99999, 99999), def), "initial: cursor outside -> nearest");
        // last が2モニターにまたがる -> どのモニターにも収まらない
        Eq(new Rectangle(-1280 + 340, 262, 600, 500), Selection.InitialFrame(two, new Rectangle(-300, 0, 600, 500), new Point(-5, 5), def), "initial: last spanning monitors");

        // MoveBy
        Selection s = new Selection(one, new Rectangle(100, 100, 200, 100));
        s.MoveBy(10, -5);
        Eq(new Rectangle(110, 95, 200, 100), s.Frame, "MoveBy basic");
        s.MoveBy(-1000, -1000);
        Eq(new Rectangle(0, 0, 200, 100), s.Frame, "MoveBy clamp top-left");
        s.MoveBy(5000, 5000);
        Eq(new Rectangle(1720, 980, 200, 100), s.Frame, "MoveBy clamp bottom-right");
        Selection sl = new Selection(two, new Rectangle(-300, 100, 200, 100));
        sl.MoveBy(1000, 0);
        Eq(new Rectangle(-200, 100, 200, 100), sl.Frame, "MoveBy cannot cross monitors");

        // MoveTo
        Selection m = new Selection(two, new Rectangle(100, 100, 400, 300));
        m.MoveTo(new Point(-500, 50), new Point(-400, 100));
        Eq(new Rectangle(-500, 50, 400, 300), m.Frame, "MoveTo to left monitor");
        m.MoveTo(new Point(-100, 50), new Point(-50, 100));
        Eq(new Rectangle(-400, 50, 400, 300), m.Frame, "MoveTo clamp inside pointer monitor");
        Selection sh = new Selection(three, new Rectangle(100, 100, 1000, 800));
        sh.MoveTo(new Point(1950, 10), new Point(1960, 20));
        Eq(new Rectangle(1920, 0, 800, 600), sh.Frame, "MoveTo shrink to smaller target");

        // MoveEdges
        Rectangle f0 = new Rectangle(100, 100, 200, 100); // R=300 B=200
        Selection e;
        e = new Selection(one, f0); e.MoveEdges(FrameHandle.Left, 10, 99);
        Eq(new Rectangle(110, 100, 190, 100), e.Frame, "edge Left (ignores dy)");
        e = new Selection(one, f0); e.MoveEdges(FrameHandle.Right, 10, 99);
        Eq(new Rectangle(100, 100, 210, 100), e.Frame, "edge Right");
        e = new Selection(one, f0); e.MoveEdges(FrameHandle.Top, 99, -10);
        Eq(new Rectangle(100, 90, 200, 110), e.Frame, "edge Top (ignores dx)");
        e = new Selection(one, f0); e.MoveEdges(FrameHandle.Bottom, 99, 10);
        Eq(new Rectangle(100, 100, 200, 110), e.Frame, "edge Bottom");
        e = new Selection(one, f0); e.MoveEdges(FrameHandle.TopLeft, -10, -20);
        Eq(new Rectangle(90, 80, 210, 120), e.Frame, "corner TopLeft");
        e = new Selection(one, f0); e.MoveEdges(FrameHandle.TopRight, 10, -20);
        Eq(new Rectangle(100, 80, 210, 120), e.Frame, "corner TopRight");
        e = new Selection(one, f0); e.MoveEdges(FrameHandle.BottomRight, 10, 20);
        Eq(new Rectangle(100, 100, 210, 120), e.Frame, "corner BottomRight");
        e = new Selection(one, f0); e.MoveEdges(FrameHandle.BottomLeft, -10, 20);
        Eq(new Rectangle(90, 100, 210, 120), e.Frame, "corner BottomLeft");
        // MinSize で止まる(反転しない, 反対の辺は動かない)
        e = new Selection(one, f0); e.MoveEdges(FrameHandle.Left, 500, 0);
        Eq(new Rectangle(300 - Selection.MinSize, 100, Selection.MinSize, 100), e.Frame, "MinSize stop Left");
        e = new Selection(one, f0); e.MoveEdges(FrameHandle.Right, -500, 0);
        Eq(new Rectangle(100, 100, Selection.MinSize, 100), e.Frame, "MinSize stop Right");
        e = new Selection(one, f0); e.MoveEdges(FrameHandle.Top, 0, 500);
        Eq(new Rectangle(100, 200 - Selection.MinSize, 200, Selection.MinSize), e.Frame, "MinSize stop Top");
        e = new Selection(one, f0); e.MoveEdges(FrameHandle.Bottom, 0, -500);
        Eq(new Rectangle(100, 100, 200, Selection.MinSize), e.Frame, "MinSize stop Bottom");
        e = new Selection(one, f0); e.MoveEdges(FrameHandle.BottomRight, -500, -500);
        Eq(new Rectangle(100, 100, Selection.MinSize, Selection.MinSize), e.Frame, "MinSize stop corner");
        // モニター端でクランプ
        e = new Selection(one, f0); e.MoveEdges(FrameHandle.TopLeft, -500, -500);
        Eq(new Rectangle(0, 0, 300, 200), e.Frame, "monitor clamp TopLeft");
        e = new Selection(one, f0); e.MoveEdges(FrameHandle.BottomRight, 9999, 9999);
        Eq(new Rectangle(100, 100, 1820, 980), e.Frame, "monitor clamp BottomRight");
        Selection en = new Selection(two, new Rectangle(-300, 100, 200, 100));
        en.MoveEdges(FrameHandle.Right, 9999, 0);
        Eq(new Rectangle(-300, 100, 300, 100), en.Frame, "monitor clamp negative monitor Right");

        // Changed イベント
        int count = 0;
        Selection c = new Selection(one, f0);
        c.Changed += delegate { count++; };
        c.MoveBy(0, 0); Check(count == 0, "Changed: MoveBy no-op");
        c.MoveBy(1, 0); Check(count == 1, "Changed: MoveBy real");
        c.MoveEdges(FrameHandle.Left, 0, 0); Check(count == 1, "Changed: MoveEdges no-op");
        c.MoveEdges(FrameHandle.Left, 1, 0); Check(count == 2, "Changed: MoveEdges real");
        c.SetFrame(c.Frame); Check(count == 2, "Changed: SetFrame same");
        c.SetFrame(new Rectangle(0, 0, 50, 50)); Check(count == 3, "Changed: SetFrame real");
        c.SelectedHandle = FrameHandle.None; Check(count == 3, "Changed: handle same");
        c.SelectedHandle = FrameHandle.Top; Check(count == 4, "Changed: handle real");
        c.MoveTo(c.Frame.Location, new Point(1, 1)); Check(count == 4, "Changed: MoveTo same");
        c.MoveEdges(FrameHandle.Top, 0, -9999); // 上端まで(0,0なので変化なし)
        Check(count == 4, "Changed: MoveEdges clamped no-op");

        // SetFrame / コンストラクタのFit
        Selection sf = new Selection(two, new Rectangle(5000, 5000, 100, 100));
        Eq(new Rectangle(1820, 980, 100, 100), sf.Frame, "ctor fits into nearest monitor");
        sf.SetFrame(new Rectangle(-2000, 10, 100, 100));
        Eq(new Rectangle(-1280, 10, 100, 100), sf.Frame, "SetFrame fits left monitor");

        // HandleRects / HitTest
        {
            Rectangle f = new Rectangle(100, 100, 200, 100); // l=100 r=300 t=100 b=200
            Check(Selection.HitTest(f, new Point(100, 100), 5) == FrameHandle.TopLeft, "HitTest TopLeft on corner");
            Check(Selection.HitTest(f, new Point(108, 95), 5) == FrameHandle.TopLeft, "HitTest corner zone is grip*2");
            Check(Selection.HitTest(f, new Point(200, 102), 5) == FrameHandle.Top, "HitTest anywhere on Top edge");
            Check(Selection.HitTest(f, new Point(150, 198), 5) == FrameHandle.Bottom, "HitTest anywhere on Bottom edge");
            Check(Selection.HitTest(f, new Point(97, 160), 5) == FrameHandle.Left, "HitTest Left edge outside");
            Check(Selection.HitTest(f, new Point(304, 130), 5) == FrameHandle.Right, "HitTest Right edge");
            Check(Selection.HitTest(f, new Point(299, 199), 5) == FrameHandle.BottomRight, "HitTest BottomRight");
            Check(Selection.HitTest(f, new Point(200, 150), 5) == FrameHandle.None, "HitTest inside is None");
            Check(Selection.HitTest(f, new Point(200, 90), 5) == FrameHandle.None, "HitTest far outside is None");
            Rectangle thin = new Rectangle(100, 100, 6, 200); // l=100 r=106
            Check(Selection.HitTest(thin, new Point(105, 200), 5) == FrameHandle.Right, "HitTest thin frame picks nearer side");
        }
        Rectangle fr = new Rectangle(100, 100, 200, 100);
        Rectangle[] hr = Selection.HandleRects(fr, 10);
        Check(hr.Length == 8, "HandleRects count");
        Eq(new Rectangle(95, 95, 10, 10), hr[0], "HandleRects TopLeft");
        Eq(new Rectangle(295, 145, 10, 10), hr[3], "HandleRects Right");
        Rectangle tiny = new Rectangle(100, 100, 16, 16);

        Console.WriteLine(pass + " passed, " + fail + " failed");
        return fail == 0 ? 0 : 1;
    }
}
