using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace Wakudori
{
    // 名前付きの範囲
    internal sealed class SavedRegion
    {
        public string Name;
        public Rectangle Bounds;
    }

    // regions.json の内容(SPEC 7.1). 読み書きは自前の小さなJSON処理で行う
    internal sealed class RegionStore
    {
        public Rectangle? Last;                                    // 前回の範囲. なければnull
        public List<SavedRegion> Regions = new List<SavedRegion>(); // 並び順は保つ
        private bool canSave = true;

        // 読めなかったファイルは上書きしない(SPEC 7.1)ので, その場合false
        public bool CanSave
        {
            get { return canSave; }
        }

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wakudori"),
                    "regions.json");
            }
        }

        // 読み込む. ファイルがなければ空. 壊れていれば空とerror(ファイルは触らない)
        public static RegionStore Load(string path, out string error)
        {
            error = null;
            RegionStore store = new RegionStore();
            if (!File.Exists(path)) return store;
            try
            {
                string text = File.ReadAllText(path, new UTF8Encoding(false, true));
                int pos = 0;
                object root = JsonReader.ParseDocument(text, ref pos);
                Dictionary<string, object> obj = root as Dictionary<string, object>;
                if (obj == null) throw new FormatException("最上位がオブジェクトではありません");

                object last;
                if (obj.TryGetValue("last", out last))
                {
                    Rectangle r;
                    if (TryRect(last as Dictionary<string, object>, out r)) store.Last = r;
                }
                object regions;
                if (obj.TryGetValue("regions", out regions))
                {
                    List<object> list = regions as List<object>;
                    if (list != null)
                    {
                        foreach (object item in list)
                        {
                            Dictionary<string, object> e = item as Dictionary<string, object>;
                            Rectangle r;
                            if (e == null || !TryRect(e, out r)) continue;
                            object n;
                            string name = e.TryGetValue("name", out n) ? n as string : null;
                            if (string.IsNullOrEmpty(name)) continue;
                            SavedRegion sr = new SavedRegion();
                            sr.Name = name;
                            sr.Bounds = r;
                            store.Regions.Add(sr);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException) throw;
                RegionStore broken = new RegionStore();
                broken.canSave = false;
                error = "範囲の保存ファイルを読めませんでした(" + path + "): " + ex.Message
                    + " ファイルは変更しません. 直すか削除してください.";
                return broken;
            }
            return store;
        }

        // x, y, width, height から矩形を作る. 数値が不正, 幅・高さが0以下なら失敗
        private static bool TryRect(Dictionary<string, object> d, out Rectangle r)
        {
            r = Rectangle.Empty;
            if (d == null) return false;
            int x, y, w, h;
            if (!GetInt(d, "x", out x) || !GetInt(d, "y", out y) ||
                !GetInt(d, "width", out w) || !GetInt(d, "height", out h)) return false;
            if (w <= 0 || h <= 0) return false;
            r = new Rectangle(x, y, w, h);
            return true;
        }

        private static bool GetInt(Dictionary<string, object> d, string key, out int v)
        {
            v = 0;
            object o;
            if (!d.TryGetValue(key, out o) || !(o is double)) return false;
            double n = Math.Round((double)o);
            if (double.IsNaN(n) || n < -1000000000.0 || n > 1000000000.0) return false;
            v = (int)n;
            return true;
        }

        // 書き込む. 一時ファイルに書いてから置き換える
        public void Save(string path)
        {
            if (!canSave) throw new InvalidOperationException("読めなかった範囲の保存ファイルは上書きできません.");
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            StringBuilder sb = new StringBuilder();
            sb.Append("{\n");
            List<string> parts = new List<string>();
            if (Last.HasValue) parts.Add("  \"last\": " + RectJson(Last.Value));
            StringBuilder rs = new StringBuilder();
            rs.Append("  \"regions\": [");
            for (int i = 0; i < Regions.Count; i++)
            {
                SavedRegion r = Regions[i];
                rs.Append(i == 0 ? "\n" : ",\n");
                rs.Append("    { \"name\": " + Quote(r.Name) + ", " + RectInner(r.Bounds) + " }");
            }
            rs.Append(Regions.Count == 0 ? "]" : "\n  ]");
            parts.Add(rs.ToString());
            sb.Append(string.Join(",\n", parts.ToArray()));
            sb.Append("\n}\n");

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        private static string RectInner(Rectangle b)
        {
            return "\"x\": " + b.X.ToString(CultureInfo.InvariantCulture)
                + ", \"y\": " + b.Y.ToString(CultureInfo.InvariantCulture)
                + ", \"width\": " + b.Width.ToString(CultureInfo.InvariantCulture)
                + ", \"height\": " + b.Height.ToString(CultureInfo.InvariantCulture);
        }

        private static string RectJson(Rectangle b)
        {
            return "{ " + RectInner(b) + " }";
        }

        // 日本語はそのまま出し, 必要な文字だけエスケープする
        private static string Quote(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u" + ((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        // 最小限のJSON読み取り. object -> Dictionary, array -> List, 数値 -> double
        private static class JsonReader
        {
            private const int MaxDepth = 64;

            public static object ParseDocument(string s, ref int p)
            {
                SkipWs(s, ref p);
                object v = ParseValue(s, ref p, 0);
                SkipWs(s, ref p);
                if (p < s.Length) throw Error(s, p, "余分な文字があります");
                return v;
            }

            private static FormatException Error(string s, int p, string msg)
            {
                int line = 1;
                for (int i = 0; i < p && i < s.Length; i++) if (s[i] == '\n') line++;
                return new FormatException(line + "行目付近: " + msg);
            }

            private static void SkipWs(string s, ref int p)
            {
                while (p < s.Length && (s[p] == ' ' || s[p] == '\t' || s[p] == '\r' || s[p] == '\n' || s[p] == '﻿')) p++;
            }

            private static object ParseValue(string s, ref int p, int depth)
            {
                if (depth > MaxDepth) throw Error(s, p, "入れ子が深すぎます");
                SkipWs(s, ref p);
                if (p >= s.Length) throw Error(s, p, "値がありません");
                char c = s[p];
                if (c == '{') return ParseObject(s, ref p, depth);
                if (c == '[') return ParseArray(s, ref p, depth);
                if (c == '"') return ParseString(s, ref p);
                if (Literal(s, ref p, "true")) return true;
                if (Literal(s, ref p, "false")) return false;
                if (Literal(s, ref p, "null")) return null;
                if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref p);
                throw Error(s, p, "予期しない文字 '" + c + "'");
            }

            private static bool Literal(string s, ref int p, string word)
            {
                if (string.CompareOrdinal(s, p, word, 0, word.Length) != 0) return false;
                p += word.Length;
                return true;
            }

            private static object ParseObject(string s, ref int p, int depth)
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                p++;
                SkipWs(s, ref p);
                if (p < s.Length && s[p] == '}') { p++; return d; }
                while (true)
                {
                    SkipWs(s, ref p);
                    if (p >= s.Length || s[p] != '"') throw Error(s, p, "キー名がありません");
                    string key = ParseString(s, ref p);
                    SkipWs(s, ref p);
                    if (p >= s.Length || s[p] != ':') throw Error(s, p, "':' がありません");
                    p++;
                    d[key] = ParseValue(s, ref p, depth + 1);
                    SkipWs(s, ref p);
                    if (p >= s.Length) throw Error(s, p, "'}' がありません");
                    if (s[p] == ',') { p++; continue; }
                    if (s[p] == '}') { p++; return d; }
                    throw Error(s, p, "',' または '}' が必要です");
                }
            }

            private static object ParseArray(string s, ref int p, int depth)
            {
                List<object> l = new List<object>();
                p++;
                SkipWs(s, ref p);
                if (p < s.Length && s[p] == ']') { p++; return l; }
                while (true)
                {
                    l.Add(ParseValue(s, ref p, depth + 1));
                    SkipWs(s, ref p);
                    if (p >= s.Length) throw Error(s, p, "']' がありません");
                    if (s[p] == ',') { p++; continue; }
                    if (s[p] == ']') { p++; return l; }
                    throw Error(s, p, "',' または ']' が必要です");
                }
            }

            private static string ParseString(string s, ref int p)
            {
                StringBuilder sb = new StringBuilder();
                p++;
                while (true)
                {
                    if (p >= s.Length) throw Error(s, p, "文字列が閉じていません");
                    char c = s[p++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw Error(s, p, "文字列内に改行などの制御文字があります");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (p >= s.Length) throw Error(s, p, "文字列が閉じていません");
                    char e = s[p++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            int code;
                            if (p + 4 > s.Length || !int.TryParse(s.Substring(p, 4), NumberStyles.AllowHexSpecifier,
                                CultureInfo.InvariantCulture, out code))
                                throw Error(s, p, "\\u の後ろが不正です");
                            sb.Append((char)code);
                            p += 4;
                            break;
                        default: throw Error(s, p, "不正なエスケープ '\\" + e + "'");
                    }
                }
            }

            private static object ParseNumber(string s, ref int p)
            {
                int start = p;
                while (p < s.Length && "+-0123456789.eE".IndexOf(s[p]) >= 0) p++;
                double v;
                if (!double.TryParse(s.Substring(start, p - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    throw Error(s, start, "数値が不正です");
                return v;
            }
        }
    }
}
