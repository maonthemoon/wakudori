using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wakudori
{
    internal static class SettingsValues
    {
        // 設定画面の日数の選択肢(H18. 細かくは選ばせない. 最大1年)
        private static readonly int[] DefaultDays = { 7, 14, 30, 60, 90, 180, 365 };

        public static void ApplyChanges(IDictionary<string, object> values, IDictionary<string, object> changes)
        {
            foreach (KeyValuePair<string, object> item in changes)
            {
                if (item.Value == null) values.Remove(item.Key);
                else values[item.Key] = item.Value;
            }
        }

        // 選択肢. 設定ファイルに選択肢にない日数(1〜365)が書いてあれば, その日数も順に足す
        public static List<int> CleanupDays(int current)
        {
            List<int> days = new List<int>(DefaultDays);
            int index = days.BinarySearch(current);
            if (index < 0) days.Insert(~index, current);
            return days;
        }

        public static string ShortenPath(string path, int width, Func<string, int> measure)
        {
            if (measure(path) <= width) return path;

            const string dots = "...";
            if (measure(dots) > width) return "";
            int[] starts = StringInfo.ParseCombiningCharacters(path);
            int min = 1;
            int max = starts.Length - 1;
            string result = dots;
            while (min <= max)
            {
                int count = min + (max - min) / 2;
                int head = starts[count / 2];
                int tail = starts[starts.Length - (count + 1) / 2];
                string candidate = path.Substring(0, head) + dots + path.Substring(tail);
                if (measure(candidate) <= width)
                {
                    result = candidate;
                    min = count + 1;
                }
                else max = count - 1;
            }
            return result;
        }
    }
}
