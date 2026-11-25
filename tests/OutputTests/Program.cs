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

        // 片方が失敗しても, もう片方は試す
        bool copied = false;
        OutputSteps r = OutputSteps.Run(() => "a.png", () => { copied = true; });
        Eq("both ok", "a.png True True -", r.Path + " " + copied + " " + r.AnyDone + " " + (r.Heading ?? "-"));
        r = OutputSteps.Run(() => "a.png", () => { throw new Exception("clip"); });
        Eq("copy fails: path", "a.png True", r.Path + " " + r.AnyDone);
        Eq("copy fails: heading", "クリップボードに入れられませんでした", r.Heading);
        Eq("copy fails: body", "画像はファイルに保存しました.\r\na.png\r\n\r\nclip", r.Body);
        copied = false;
        r = OutputSteps.Run(() => { throw new Exception("disk"); }, () => { copied = true; });
        Eq("save fails", "True True", copied + " " + r.AnyDone);
        Eq("save fails: heading", "画像を保存できませんでした", r.Heading);
        Eq("save fails: body", "クリップボードには入っています.\r\n\r\ndisk", r.Body);
        r = OutputSteps.Run(() => { throw new Exception("disk"); }, () => { throw new Exception("clip"); });
        Eq("both fail", "False 保存もコピーもできませんでした disk\r\nclip", r.AnyDone + " " + r.Heading + " " + r.Body);
        r = OutputSteps.Run(() => { throw new Exception("disk"); }, null);
        Eq("no copy, save fails", "False 画像を保存できませんでした disk", r.AnyDone + " " + r.Heading + " " + r.Body);
        string order = "";
        OutputSteps.Run(() => { order += "save "; return "a"; }, () => { order += "copy"; });
        Eq("file first", "save copy", order);

        // エラーの記録
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wakudori-test-" + Guid.NewGuid().ToString("N"));
        string log = System.IO.Path.Combine(dir, "sub", "error.log");
        string oldLog = System.IO.Path.Combine(dir, "sub", "error.old.log");
        try
        {
            Eq("log write", "True", ErrorLog.Write(log, "0.0.1", "Test", new Exception("boom"), t).ToString());
            string text = System.IO.File.ReadAllText(log);
            Eq("log text", "True", (text.StartsWith("2026-11-20 09:05:07 Wakudori 0.0.1 (") && text.Contains("Test") && text.Contains("boom")).ToString());
            ErrorLog.Write(log, "0.0.1", "Test", null, t);
            Eq("log appends", "True", System.IO.File.ReadAllText(log).StartsWith(text).ToString());
            for (int i = 0; i < 2; i++) // error.old.log がすでにあっても移せる
            {
                System.IO.File.WriteAllText(log, new string('x', 300 * 1024));
                ErrorLog.Write(log, "0.0.1", "Test", null, t);
            }
            Eq("log rotates", "True True", (new System.IO.FileInfo(log).Length < 1024) + " " + (new System.IO.FileInfo(oldLog).Length == 300 * 1024));
            Eq("log never throws", "False", ErrorLog.Write(dir, "0.0.1", "Test", null, t).ToString()); // フォルダーには書けない
        }
        finally
        {
            System.IO.Directory.Delete(dir, true);
        }

        Console.WriteLine(fail == 0 ? "ALL PASS" : fail + " FAILED");
        return fail == 0 ? 0 : 1;
    }
}
