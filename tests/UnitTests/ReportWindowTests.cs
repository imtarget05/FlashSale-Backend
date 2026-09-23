using FlashSale.Application.Automation;

namespace FlashSale.UnitTests;

/// <summary>
/// Spec §7 window correctness: a report day must mean exactly one UTC range,
/// regardless of server locale or input DateTime Kind.
/// </summary>
public class ReportWindowTests
{
    [Fact]
    public void ForDay_IsMidnightToMidnightUtc()
    {
        var (from, to) = ReportWindow.ForDay(new DateTime(2026, 9, 23, 15, 30, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(DateTimeKind.Utc, from.Kind);
        Assert.Equal(new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc), to);
    }

    [Fact]
    public void ForDay_TreatsLocalKindInputAsTheSameCalendarDay()
    {
        // An Unspecified/Local 23:59 must not shift the window into another day;
        // the report day is taken as the DATE component, then zeroed to UTC midnight.
        var (from, to) = ReportWindow.ForDay(new DateTime(2026, 9, 23, 23, 59, 59, DateTimeKind.Local));

        Assert.Equal(23, from.Day);
        Assert.Equal(0, from.Hour);                      // day boundary, not the input time
        Assert.Equal(DateTimeKind.Utc, from.Kind);
        Assert.Equal(1, (to - from).TotalDays);
    }
}