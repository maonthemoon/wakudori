using System;
using Wakudori;

internal static class HotkeyTests
{
    private static int failed = 0;

    private static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) failed++;
    }

    private static bool Parses(string s, HotkeyModifiers m, int vk)
    {
        HotkeySpec h;
        return HotkeyText.TryParse(s, out h) && h.Modifiers == m && h.Vk == vk;
    }

    private static bool Fails(string s)
    {
        HotkeySpec h;
        h = new HotkeySpec(HotkeyModifiers.Alt, 1);
        bool r = HotkeyText.TryParse(s, out h);
        return !r && h.Vk == 0 && h.Modifiers == HotkeyModifiers.None;
    }

    private static HotkeySpec S(HotkeyModifiers m, int vk)
    {
        return new HotkeySpec(m, vk);
    }

    private static int Main()
    {
        HotkeyModifiers C = HotkeyModifiers.Ctrl, A = HotkeyModifiers.Alt, Sh = HotkeyModifiers.Shift, W = HotkeyModifiers.Win;

        // Format
        Check("format: Ctrl+Shift+S", HotkeyText.Format(S(C | Sh, 0x53)) == "Ctrl+Shift+S");
        Check("format: order all", HotkeyText.Format(S(W | Sh | A | C, 0x70)) == "Ctrl+Alt+Shift+Win+F1");
        Check("format: none", HotkeyText.Format(S(HotkeyModifiers.None, 0x2C)) == "PrintScreen");
        Check("format: unknown", HotkeyText.Format(S(C, 0x07)) == "Ctrl+0x07");
        Check("format: digit", HotkeyText.Format(S(A, 0x35)) == "Alt+5");
        Check("format: F24", HotkeyText.Format(S(C, 0x87)) == "Ctrl+F24");

        // KeyName
        Check("keyname: samples", HotkeyText.KeyName(0x41) == "A" && HotkeyText.KeyName(0x69) == "Num9"
            && HotkeyText.KeyName(0xE2) == "Oem102" && HotkeyText.KeyName(0x1B) == "Esc");
        Check("keyname: unsupported", HotkeyText.KeyName(0x10) == null && HotkeyText.KeyName(0x14) == null
            && HotkeyText.KeyName(0x90) == null && HotkeyText.KeyName(0x01) == null && HotkeyText.KeyName(0x6C) == null);
        Check("keyname: modifiers null", HotkeyText.KeyName(0x5B) == null && HotkeyText.KeyName(0xA2) == null);

        // IsModifierKey
        bool modOk = true;
        int[] mods = { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 };
        foreach (int m in mods) modOk = modOk && HotkeyText.IsModifierKey(m);
        Check("ismodifier: all", modOk);
        Check("ismodifier: not", !HotkeyText.IsModifierKey(0x41) && !HotkeyText.IsModifierKey(0xA6) && !HotkeyText.IsModifierKey(0x5D));

        // round trip
        HotkeyModifiers[] combos = { HotkeyModifiers.None, C, C | Sh, A | W, C | A | Sh | W };
        int named = 0;
        bool rt = true;
        for (int vk = 0; vk < 256; vk++)
        {
            if (HotkeyText.KeyName(vk) == null) continue;
            named++;
            foreach (HotkeyModifiers m in combos)
            {
                HotkeySpec h;
                string f = HotkeyText.Format(S(m, vk));
                if (!HotkeyText.TryParse(f, out h) || h.Modifiers != m || h.Vk != vk)
                {
                    rt = false;
                    Console.WriteLine("  roundtrip failed: " + f);
                }
            }
        }
        Check("roundtrip: all named keys (" + named + ")", rt);
        Check("roundtrip: key count", named == 26 + 10 + 24 + 10 + 5 + 18 + 12 + 2);
        Check("roundtrip: unknown 0x07", Parses(HotkeyText.Format(S(C, 0x07)), C, 0x07));

        // aliases
        Check("alias: PrtSc", Parses("PrtSc", HotkeyModifiers.None, 0x2C));
        Check("alias: PrtScn", Parses("prtscn", HotkeyModifiers.None, 0x2C));
        Check("alias: Snapshot", Parses("Snapshot", HotkeyModifiers.None, 0x2C));
        Check("alias: Escape", Parses("Escape", HotkeyModifiers.None, 0x1B));
        Check("alias: Return", Parses("Ctrl+Return", C, 0x0D));
        Check("alias: Del", Parses("Del", HotkeyModifiers.None, 0x2E));
        Check("alias: Ins", Parses("Ins", HotkeyModifiers.None, 0x2D));
        Check("alias: PgUp", Parses("PgUp", HotkeyModifiers.None, 0x21));
        Check("alias: PgDn", Parses("PgDn", HotkeyModifiers.None, 0x22));
        Check("alias: Back", Parses("Back", HotkeyModifiers.None, 0x08));
        Check("alias: D0", Parses("D0", HotkeyModifiers.None, 0x30));
        Check("alias: D9", Parses("Alt+D9", A, 0x39));
        Check("alias: Control", Parses("Control+A", C, 0x41));
        Check("alias: Windows", Parses("Windows+A", W, 0x41));
        Check("alias: hex 0x41", Parses("Ctrl+0x41", C, 0x41));
        Check("alias: hex 0xFE", Parses("0xFE", HotkeyModifiers.None, 0xFE));
        Check("alias: hex 0x1", Parses("0x1", HotkeyModifiers.None, 1));

        // case and spaces
        Check("tolerance: ctrl + shift + s", Parses("ctrl + shift + s", C | Sh, 0x53));
        Check("tolerance: CTRL+ALT+F12", Parses("CTRL+ALT+F12", C | A, 0x7B));
        Check("tolerance: leading/trailing spaces", Parses("  Win+Space  ", W, 0x20));
        Check("tolerance: modifier order", Parses("Shift+Ctrl+A", C | Sh, 0x41));

        // failures
        Check("fail: Ctrl++", Fails("Ctrl++"));
        Check("fail: empty", Fails(""));
        Check("fail: spaces", Fails("  "));
        Check("fail: +A", Fails("+A"));
        Check("fail: A+", Fails("A+"));
        Check("fail: null", Fails(null));
        Check("fail: duplicate Ctrl", Fails("Ctrl+Ctrl+A"));
        Check("fail: duplicate Ctrl/Control", Fails("Ctrl+Control+A"));
        Check("fail: two keys", Fails("Ctrl+A+B"));
        Check("fail: modifier after key", Fails("A+Ctrl"));
        Check("fail: modifier after key 2", Fails("Ctrl+A+Shift"));
        Check("fail: modifier only", Fails("Ctrl"));
        Check("fail: modifiers only", Fails("Ctrl+Shift"));
        Check("fail: unknown name", Fails("Ctrl+Foo"));
        Check("fail: hex 0", Fails("0x0"));
        Check("fail: hex 0xFF", Fails("0xFF"));
        Check("fail: hex 0x", Fails("0x"));
        Check("fail: hex bad", Fails("0xZZ"));
        Check("fail: hex 0x100", Fails("0x100"));
        Check("fail: partial failure resets", Fails("Ctrl+Shift+Foo"));

        // Validate
        Check("validate: S alone", HotkeyText.Validate(S(HotkeyModifiers.None, 0x53)) != null);
        Check("validate: Shift+S", HotkeyText.Validate(S(Sh, 0x53)) != null);
        Check("validate: alone message", HotkeyText.Validate(S(HotkeyModifiers.None, 0x53)).StartsWith("単独のキー"));
        Check("validate: F12", HotkeyText.Validate(S(HotkeyModifiers.None, 0x7B)) == null);
        Check("validate: F1 / F24", HotkeyText.Validate(S(HotkeyModifiers.None, 0x70)) == null
            && HotkeyText.Validate(S(Sh, 0x87)) == null);
        Check("validate: PrintScreen", HotkeyText.Validate(S(HotkeyModifiers.None, 0x2C)) == null);
        Check("validate: Shift+PrintScreen", HotkeyText.Validate(S(Sh, 0x2C)) == null);
        Check("validate: Pause / ScrollLock", HotkeyText.Validate(S(HotkeyModifiers.None, 0x13)) == null
            && HotkeyText.Validate(S(HotkeyModifiers.None, 0x91)) == null);
        Check("validate: Ctrl+S", HotkeyText.Validate(S(C, 0x53)) == null);
        Check("validate: Alt+Space", HotkeyText.Validate(S(A, 0x20)) == null);
        Check("validate: Win+A", HotkeyText.Validate(S(W, 0x41)) == null);
        Check("validate: Win+Esc", HotkeyText.Validate(S(W, 0x1B)) == "Escは選択画面を閉じるのに使うため, 登録できません.");
        Check("validate: Esc alone", HotkeyText.Validate(S(HotkeyModifiers.None, 0x1B)) != null);
        Check("validate: unknown 0x07", HotkeyText.Validate(S(C, 0x07)) == "このキーは使えません.");
        Check("validate: modifier vk Shift", HotkeyText.Validate(S(C, 0x10)) == "このキーは使えません.");
        Check("validate: modifier vk LWin", HotkeyText.Validate(S(C, 0x5B)) == "このキーは使えません.");
        Check("validate: modifier vk RCtrl", HotkeyText.Validate(S(C, 0xA3)) == "このキーは使えません.");
        Check("validate: CapsLock", HotkeyText.Validate(S(C, 0x14)) != null);

        Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
        return failed == 0 ? 0 : 1;
    }
}
