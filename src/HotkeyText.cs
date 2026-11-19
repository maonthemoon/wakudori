using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Wakudori
{
    // RegisterHotKeyのMOD_*と同じ値.
    [Flags]
    internal enum HotkeyModifiers : uint
    {
        None = 0,
        Alt = 1,
        Ctrl = 2,
        Shift = 4,
        Win = 8
    }

    internal struct HotkeySpec
    {
        public HotkeyModifiers Modifiers;
        public int Vk;

        public HotkeySpec(HotkeyModifiers modifiers, int vk)
        {
            Modifiers = modifiers;
            Vk = vk;
        }
    }

    // 文字列とホットキーを相互に変換する.
    internal static class HotkeyText
    {
        private static readonly Dictionary<string, int> NameToVk = BuildNameToVk();
        private static readonly Dictionary<string, int> Aliases = BuildAliases();

        private static Dictionary<string, int> BuildNameToVk()
        {
            Dictionary<string, int> d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int vk = 0; vk < 256; vk++)
            {
                string n = KeyName(vk);
                if (n != null) d[n] = vk;
            }
            return d;
        }

        private static Dictionary<string, int> BuildAliases()
        {
            Dictionary<string, int> d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            d["PrtSc"] = 0x2C;
            d["PrtScn"] = 0x2C;
            d["Snapshot"] = 0x2C;
            d["Escape"] = 0x1B;
            d["Return"] = 0x0D;
            d["Del"] = 0x2E;
            d["Ins"] = 0x2D;
            d["PgUp"] = 0x21;
            d["PgDn"] = 0x22;
            d["Back"] = 0x08;
            for (int i = 0; i <= 9; i++) d["D" + i.ToString(CultureInfo.InvariantCulture)] = 0x30 + i;
            return d;
        }

        // 修飾キー, キーの順に"Ctrl+Shift+S"形式の文字列にする.
        public static string Format(HotkeySpec h)
        {
            StringBuilder sb = new StringBuilder();
            if ((h.Modifiers & HotkeyModifiers.Ctrl) != 0) sb.Append("Ctrl+");
            if ((h.Modifiers & HotkeyModifiers.Alt) != 0) sb.Append("Alt+");
            if ((h.Modifiers & HotkeyModifiers.Shift) != 0) sb.Append("Shift+");
            if ((h.Modifiers & HotkeyModifiers.Win) != 0) sb.Append("Win+");
            string name = KeyName(h.Vk);
            if (name == null) name = "0x" + h.Vk.ToString("X2", CultureInfo.InvariantCulture);
            sb.Append(name);
            return sb.ToString();
        }

        // 文字列を解析する. 失敗時はhがdefaultになる.
        public static bool TryParse(string s, out HotkeySpec h)
        {
            h = default(HotkeySpec);
            if (s == null) return false;
            string[] tokens = s.Split('+');
            HotkeyModifiers mods = HotkeyModifiers.None;
            for (int i = 0; i < tokens.Length; i++)
            {
                tokens[i] = tokens[i].Trim();
                if (tokens[i].Length == 0) return false;
            }
            for (int i = 0; i < tokens.Length - 1; i++)
            {
                HotkeyModifiers m;
                if (!TryModifier(tokens[i], out m)) return false;
                if ((mods & m) != 0) return false;
                mods |= m;
            }
            int vk;
            if (!TryKey(tokens[tokens.Length - 1], out vk)) return false;
            h = new HotkeySpec(mods, vk);
            return true;
        }

        private static bool TryModifier(string t, out HotkeyModifiers m)
        {
            m = HotkeyModifiers.None;
            if (string.Equals(t, "Ctrl", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, "Control", StringComparison.OrdinalIgnoreCase)) m = HotkeyModifiers.Ctrl;
            else if (string.Equals(t, "Alt", StringComparison.OrdinalIgnoreCase)) m = HotkeyModifiers.Alt;
            else if (string.Equals(t, "Shift", StringComparison.OrdinalIgnoreCase)) m = HotkeyModifiers.Shift;
            else if (string.Equals(t, "Win", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, "Windows", StringComparison.OrdinalIgnoreCase)) m = HotkeyModifiers.Win;
            else return false;
            return true;
        }

        private static bool TryKey(string t, out int vk)
        {
            if (NameToVk.TryGetValue(t, out vk)) return true;
            if (Aliases.TryGetValue(t, out vk)) return true;
            if (t.Length > 2 && (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)))
            {
                int v;
                if (int.TryParse(t.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out v)
                    && v >= 1 && v <= 254)
                {
                    vk = v;
                    return true;
                }
            }
            vk = 0;
            return false;
        }

        // 仮想キーコードの正式名. 未対応ならnull.
        public static string KeyName(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1).ToString(CultureInfo.InvariantCulture);
            if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60).ToString(CultureInfo.InvariantCulture);
            switch (vk)
            {
                case 0x6A: return "NumMultiply";
                case 0x6B: return "NumAdd";
                case 0x6D: return "NumSubtract";
                case 0x6E: return "NumDecimal";
                case 0x6F: return "NumDivide";
                case 0x2C: return "PrintScreen";
                case 0x13: return "Pause";
                case 0x91: return "ScrollLock";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x24: return "Home";
                case 0x23: return "End";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x25: return "Left";
                case 0x26: return "Up";
                case 0x27: return "Right";
                case 0x28: return "Down";
                case 0x20: return "Space";
                case 0x0D: return "Enter";
                case 0x09: return "Tab";
                case 0x08: return "Backspace";
                case 0x1B: return "Esc";
                case 0xBA: return "Oem1";
                case 0xBB: return "OemPlus";
                case 0xBC: return "OemComma";
                case 0xBD: return "OemMinus";
                case 0xBE: return "OemPeriod";
                case 0xBF: return "Oem2";
                case 0xC0: return "Oem3";
                case 0xDB: return "Oem4";
                case 0xDC: return "Oem5";
                case 0xDD: return "Oem6";
                case 0xDE: return "Oem7";
                case 0xE2: return "Oem102";
                case 0x1C: return "Convert";
                case 0x1D: return "NonConvert";
            }
            return null;
        }

        // 修飾キー自体の仮想キーコードか.
        public static bool IsModifierKey(int vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x5B || vk == 0x5C ||
                (vk >= 0xA0 && vk <= 0xA5);
        }

        // 登録できるなら null, できないなら理由を返す.
        public static string Validate(HotkeySpec h)
        {
            if (IsModifierKey(h.Vk) || KeyName(h.Vk) == null) return "このキーは使えません.";
            if (h.Vk == 0x1B) return "Escは選択画面を閉じるのに使うため, 登録できません.";
            HotkeyModifiers strong = HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win;
            if ((h.Modifiers & strong) == 0)
            {
                bool ok = (h.Vk >= 0x70 && h.Vk <= 0x87) || h.Vk == 0x2C || h.Vk == 0x13 || h.Vk == 0x91;
                if (!ok)
                    return "単独のキー(またはShift+キー)に使えるのは, F1〜F24, PrintScreen, Pause, ScrollLockだけです. ほかのキーはCtrl, Alt, Winのどれかと組み合わせてください.";
            }
            return null;
        }
    }
}
