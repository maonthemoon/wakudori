using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using Wakudori;

internal static class RegionTests
{
    private static int failed = 0;
    private static string tmpRoot;

    private static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) failed++;
    }

    private static string P(string file)
    {
        return Path.Combine(tmpRoot, file);
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private const string SpecExample = @"{
  ""last"": { ""x"": 100, ""y"": 200, ""width"": 800, ""height"": 600 },
  ""regions"": [
    { ""name"": ""会議資料"", ""x"": 0, ""y"": 0, ""width"": 1280, ""height"": 720 },
    { ""name"": ""右下のグラフ"", ""x"": 2400, ""y"": 900, ""width"": 640, ""height"": 480 }
  ]
}";

    private static bool Same(RegionStore a, RegionStore b)
    {
        if (a.Last.HasValue != b.Last.HasValue) return false;
        if (a.Last.HasValue && a.Last.Value != b.Last.Value) return false;
        if (a.Regions.Count != b.Regions.Count) return false;
        for (int i = 0; i < a.Regions.Count; i++)
            if (a.Regions[i].Name != b.Regions[i].Name || a.Regions[i].Bounds != b.Regions[i].Bounds) return false;
        return true;
    }

    private static int Main()
    {
        tmpRoot = Path.Combine(Path.GetTempPath(), "wakudori-region-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpRoot);
        try
        {
            string err;

            // ファイルなし
            RegionStore s = RegionStore.Load(P("none/regions.json"), out err);
            Check("missing: error null", err == null);
            Check("missing: empty, CanSave", s.CanSave && s.Regions.Count == 0 && !s.Last.HasValue);

            // SPECの例
            Write(P("spec.json"), SpecExample);
            s = RegionStore.Load(P("spec.json"), out err);
            Check("spec: loaded", err == null && s.CanSave && s.Regions.Count == 2);
            Check("spec: last", s.Last.Value == new Rectangle(100, 200, 800, 600));
            Check("spec: names/order", s.Regions[0].Name == "会議資料" && s.Regions[1].Name == "右下のグラフ");
            Check("spec: bounds", s.Regions[1].Bounds == new Rectangle(2400, 900, 640, 480));

            // 保存して読み直し(負の座標含む), 読みやすさ
            s.Regions.Add(new SavedRegion { Name = "左モニター \"A\"\\x", Bounds = new Rectangle(-1920, -50, 1920, 1080) });
            s.Save(P("out/sub/regions.json"));
            byte[] bytes = File.ReadAllBytes(P("out/sub/regions.json"));
            string text = Encoding.UTF8.GetString(bytes);
            Check("save: no BOM", !(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF));
            Check("save: readable Japanese", text.Contains("会議資料") && text.Contains("右下のグラフ") && !text.Contains("\\u"));
            Check("save: multiline", text.Split('\n').Length >= 6);
            Check("save: no tmp left", !File.Exists(P("out/sub/regions.json.tmp")));
            RegionStore s2 = RegionStore.Load(P("out/sub/regions.json"), out err);
            Check("save: reload equal", err == null && Same(s, s2) && s2.Regions[2].Bounds.X == -1920);

            // 壊れたJSON
            string[] broken = { "{ \"last\": ", "", "[1,2]", "{ \"regions\": [ { \"name\": \"a\" } }", "not json", "{} x" };
            bool allBroken = true;
            for (int i = 0; i < broken.Length; i++)
            {
                string bp = P("broken" + i + ".json");
                Write(bp, broken[i]);
                RegionStore b = RegionStore.Load(bp, out err);
                if (b.CanSave || err == null || !err.Contains(bp) || b.Regions.Count != 0 || b.Last.HasValue) allBroken = false;
                if (File.ReadAllText(bp) != broken[i]) allBroken = false;
                bool threw = false;
                try { b.Save(bp); } catch (InvalidOperationException) { threw = true; }
                if (!threw || File.ReadAllText(bp) != broken[i]) allBroken = false;
            }
            Check("broken: CanSave false, error, Save throws, file untouched", allBroken);

            // 余分なキー, 順序, 空白
            Write(P("extra.json"), "{\"zzz\":[1,{\"a\":null}],\"regions\":[{\"height\":10,\"extra\":true,\"name\":\"n\",\"width\":20,\"y\":3,\"x\":-4}],\n\t\"last\":{\"height\":5,\"width\":6,\"y\":7,\"x\":8,\"foo\":\"bar\"},\"version\":1.5e0}");
            s = RegionStore.Load(P("extra.json"), out err);
            Check("extra: ignored", err == null && s.Regions.Count == 1 && s.Regions[0].Bounds == new Rectangle(-4, 3, 20, 10)
                && s.Last.Value == new Rectangle(8, 7, 6, 5));

            // 不正な項目はスキップ
            Write(P("invalid.json"), @"{ ""last"": { ""x"": ""a"", ""y"": 0, ""width"": 1, ""height"": 1 },
 ""regions"": [
  { ""name"": ""ok"", ""x"": 1, ""y"": 2, ""width"": 3, ""height"": 4 },
  { ""name"": ""zero"", ""x"": 1, ""y"": 2, ""width"": 0, ""height"": 4 },
  { ""name"": ""neg"", ""x"": 1, ""y"": 2, ""width"": 3, ""height"": -4 },
  { ""name"": ""str"", ""x"": ""1"", ""y"": 2, ""width"": 3, ""height"": 4 },
  { ""name"": ""missing"", ""x"": 1, ""y"": 2, ""width"": 3 },
  { ""x"": 1, ""y"": 2, ""width"": 3, ""height"": 4 },
  { ""name"": """", ""x"": 1, ""y"": 2, ""width"": 3, ""height"": 4 },
  { ""name"": 5, ""x"": 1, ""y"": 2, ""width"": 3, ""height"": 4 },
  7, null,
  { ""name"": ""ok2"", ""x"": 0, ""y"": 0, ""width"": 9, ""height"": 9 }
 ] }");
            s = RegionStore.Load(P("invalid.json"), out err);
            Check("invalid: skipped, not fatal", err == null && s.CanSave && !s.Last.HasValue
                && s.Regions.Count == 2 && s.Regions[0].Name == "ok" && s.Regions[1].Name == "ok2");

            // lastのみ
            RegionStore l = new RegionStore();
            l.Last = new Rectangle(1, 2, 3, 4);
            l.Save(P("last.json"));
            RegionStore l2 = RegionStore.Load(P("last.json"), out err);
            Check("last only: roundtrip", err == null && Same(l, l2) && l2.Regions.Count == 0);

            // 空
            new RegionStore().Save(P("empty.json"));
            RegionStore e2 = RegionStore.Load(P("empty.json"), out err);
            Check("empty: roundtrip", err == null && e2.CanSave && !e2.Last.HasValue && e2.Regions.Count == 0);

            // 上書き
            l.Regions.Add(new SavedRegion { Name = "x", Bounds = new Rectangle(0, 0, 10, 10) });
            l.Last = null;
            l.Save(P("last.json"));
            l2 = RegionStore.Load(P("last.json"), out err);
            Check("overwrite: replaced", err == null && Same(l, l2) && !l2.Last.HasValue && l2.Regions.Count == 1);
            Check("overwrite: no tmp left", !File.Exists(P("last.json.tmp")));

            Check("DefaultPath", RegionStore.DefaultPath.EndsWith("regions.json") && RegionStore.DefaultPath.Contains("Wakudori"));
        }
        finally
        {
            try { Directory.Delete(tmpRoot, true); } catch (Exception) { }
        }
        Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
        return failed == 0 ? 0 : 1;
    }
}
