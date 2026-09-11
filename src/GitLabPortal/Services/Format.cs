namespace GitLabPortal.Services;

public static class Format
{
    public static string Duration(double? seconds)
    {
        if (seconds is null or <= 0) return "-";
        var span = TimeSpan.FromSeconds(seconds.Value);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m {span.Seconds}s"
            : span.TotalMinutes >= 1
                ? $"{span.Minutes}m {span.Seconds}s"
                : $"{span.Seconds}s";
    }

    public static string Relative(DateTimeOffset? value)
    {
        if (value is null) return "-";
        var delta = DateTimeOffset.UtcNow - value.Value.ToUniversalTime();
        if (delta.TotalSeconds < 60) return "just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} min ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} h ago";
        if (delta.TotalDays < 30) return $"{(int)delta.TotalDays} d ago";
        return value.Value.ToLocalTime().ToString("yyyy-MM-dd");
    }

    public static string Bytes(long size)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = size;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }
}
