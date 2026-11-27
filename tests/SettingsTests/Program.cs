using System;
using System.Collections.Generic;
using System.Linq;
using Wakudori;

internal static class SettingsTests
{
    private static int failed;

    private static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) failed++;
    }

    private static int Main()
    {
        Check("days: presets contain the selected value once", SettingsValues.CleanupDays(30)
            .SequenceEqual(new[] { 7, 14, 30, 60, 90, 180, 365 }));
        Check("days: custom value inserted in order", SettingsValues.CleanupDays(45)
            .SequenceEqual(new[] { 7, 14, 30, 45, 60, 90, 180, 365 }));
        Check("days: custom minimum", SettingsValues.CleanupDays(1).First() == 1
            && SettingsValues.CleanupDays(1).Last() == 365);

        const string path = @"C:\Users\Wakudori\Pictures\Screenshots";
        Check("path: exact width leaves the path intact", SettingsValues.ShortenPath(path, path.Length, s => s.Length) == path);
        Check("path: keeps both ends", SettingsValues.ShortenPath(path, 15, s => s.Length) == @"C:\Use...nshots");
        Check("path: uses measured width", SettingsValues.ShortenPath("画abcdef面", 9,
            s => s.Sum(c => c > 127 ? 3 : 1)) == "画...面");
        Check("path: ellipsis must fit", SettingsValues.ShortenPath(path, 2, s => s.Length) == "");
        Check("path: keeps a Unicode surrogate pair intact", SettingsValues.ShortenPath("😀abcdef画", 6, s => s.Length) == "😀...画");
        Check("path: keeps a combining character with its base", SettingsValues.ShortenPath("e\u0301abcdef画", 6, s => s.Length) == "e\u0301...画");

        var extra = new Dictionary<string, object> { { "nested", true } };
        var values = new Dictionary<string, object> { { "hotkey", "F12" }, { "saveDir", path }, { "extra", extra }, { "extraNull", null } };
        SettingsValues.ApplyChanges(values, new Dictionary<string, object>
        {
            { "hotkey", "Ctrl+Shift+S" }, { "autoDeleteDays", 45 }, { "autoDelete", false }, { "saveDir", null }, { "missing", null }
        });
        Check("changes: overwrite and insert", Equals(values["hotkey"], "Ctrl+Shift+S")
            && Equals(values["autoDeleteDays"], 45) && Equals(values["autoDelete"], false));
        Check("changes: remove null entries in the request", !values.ContainsKey("saveDir") && !values.ContainsKey("missing"));
        Check("changes: keep unrelated values", ReferenceEquals(values["extra"], extra)
            && values.ContainsKey("extraNull") && values["extraNull"] == null);
        Check("changes: reset can produce an empty object", RemovesLastValue());

        Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
        return failed == 0 ? 0 : 1;
    }

    private static bool RemovesLastValue()
    {
        var values = new Dictionary<string, object> { { "saveDir", "custom" } };
        SettingsValues.ApplyChanges(values, new Dictionary<string, object> { { "saveDir", null } });
        return values.Count == 0;
    }
}
