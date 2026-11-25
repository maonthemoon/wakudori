using System;
using System.Collections.Generic;
using Wakudori;

internal static class Program
{
    static int fail;

    static void Eq(string label, string expected, string actual)
    {
        bool ok = expected == actual;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + label + (ok ? "" : " expected=[" + expected + "] actual=[" + actual + "]"));
        if (!ok) fail++;
    }

    static int Main()
    {
        DateTime t = new DateTime(2026, 11, 20, 9, 5, 7);
        Eq("null", "screenshot_20261120_090507.png", FileNaming.BuildFileName(null, t));
        Eq("empty", "screenshot_20261120_090507.png", FileNaming.BuildFileName("", t));
        Eq("whitespace", "screenshot_20261120_090507.png", FileNaming.BuildFileName("  \t ", t));
        Eq("japanese", "設計図_20261120_090507.png", FileNaming.BuildFileName("設計図", t));
        Eq("trim", "a b_20261120_090507.png", FileNaming.BuildFileName("  a b  ", t));
        foreach (char c in "\\/:*?\"<>|")
            Eq("invalid " + c, "a_b_20261120_090507.png", FileNaming.BuildFileName("a" + c + "b", t));
        Eq("control", "a_b_20261120_090507.png", FileNaming.BuildFileName("a\nb", t));
        Eq("only invalid", "__20261120_090507.png", FileNaming.BuildFileName("?", t));

        HashSet<string> set = new HashSet<string>();
        Func<string, bool> ex = p => set.Contains(p);
        string p1 = FileNaming.UniquePath("d", "x.png", ex);
        Eq("unique 1", System.IO.Path.Combine("d", "x.png"), p1);
        set.Add(p1);
        string p2 = FileNaming.UniquePath("d", "x.png", ex);
        Eq("unique 2", System.IO.Path.Combine("d", "x_2.png"), p2);
        set.Add(p2);
        Eq("unique 3", System.IO.Path.Combine("d", "x_3.png"), FileNaming.UniquePath("d", "x.png", ex));

        Eq("edit name", "会議_20261120_090507_edit.png", FileNaming.BuildEditName(System.IO.Path.Combine("d", "会議_20261120_090507.png")));
        Eq("edit name with dot", "v1.2_20261120_090507_2_edit.png", FileNaming.BuildEditName("v1.2_20261120_090507_2.png"));

        Console.WriteLine(fail == 0 ? "ALL PASS" : fail + " FAILED");
        return fail == 0 ? 0 : 1;
    }
}
