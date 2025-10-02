namespace nathanbutlerDEV.mt.net.Utilities;

public static class TimeSpanParser
{
    public static TimeSpan ParseTimeString(string timeString)
    {
        if (string.IsNullOrWhiteSpace(timeString) || timeString == "00:00:00")
            return TimeSpan.Zero;

        // Handle negative time (like -00:02:00)
        bool isNegative = timeString.StartsWith('-');
        if (isNegative)
            timeString = timeString[1..];

        if (TimeSpan.TryParse(timeString, out var result))
        {
            return isNegative ? -result : result;
        }

        // Try parsing HH:MM:SS format manually
        var parts = timeString.Split(':');
        if (parts.Length == 3 &&
            int.TryParse(parts[0], out var hours) &&
            int.TryParse(parts[1], out var minutes) &&
            int.TryParse(parts[2], out var seconds))
        {
            var timeSpan = new TimeSpan(hours, minutes, seconds);
            return isNegative ? -timeSpan : timeSpan;
        }

        throw new ArgumentException($"Invalid time format: {timeString}");
    }

    public static long ToMilliseconds(TimeSpan timeSpan)
    {
        return (long)timeSpan.TotalMilliseconds;
    }

    public static string FormatTimestamp(TimeSpan timeSpan)
    {
        return $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
    }
}