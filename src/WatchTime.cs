using System;
using System.Globalization;

namespace Wakudori
{
    internal static class WatchTime
    {
        public static string FormatElapsed(TimeSpan elapsed)
        {
            string minutesAndSeconds = elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
            int hours = (int)elapsed.TotalHours;
            if (hours == 0) return minutesAndSeconds;
            return hours.ToString(CultureInfo.InvariantCulture) + ":" + minutesAndSeconds;
        }
    }
}
