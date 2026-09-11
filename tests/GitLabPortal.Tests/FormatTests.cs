using GitLabPortal.Services;

namespace GitLabPortal.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(null, "-")]
    [InlineData(0d, "-")]
    [InlineData(-5d, "-")]
    [InlineData(45d, "45s")]
    [InlineData(90d, "1m 30s")]
    [InlineData(3600d, "1h 0m 0s")]
    [InlineData(3725d, "1h 2m 5s")]
    public void Duration_formats_by_magnitude(double? seconds, string expected)
        => Assert.Equal(expected, Format.Duration(seconds));

    [Fact]
    public void Relative_returns_dash_for_null()
        => Assert.Equal("-", Format.Relative(null));

    [Theory]
    [InlineData(10, "just now")]
    [InlineData(5 * 60, "5 min ago")]
    [InlineData(3 * 3600, "3 h ago")]
    [InlineData(4 * 86400, "4 d ago")]
    public void Relative_describes_recent_timestamps(int secondsAgo, string expected)
    {
        var value = DateTimeOffset.UtcNow.AddSeconds(-secondsAgo);
        Assert.Equal(expected, Format.Relative(value));
    }

    [Fact]
    public void Relative_falls_back_to_absolute_date_beyond_30_days()
    {
        var value = DateTimeOffset.UtcNow.AddDays(-45);
        Assert.Equal(value.ToLocalTime().ToString("yyyy-MM-dd"), Format.Relative(value));
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    public void Bytes_scales_to_the_largest_fitting_unit(long size, string expected)
        => Assert.Equal(expected, Format.Bytes(size));
}
