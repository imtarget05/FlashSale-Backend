namespace FlashSale.Application.Automation;

/// <summary>
/// Reporting automation configuration (spec §7: daily scheduler; values
/// configurable, never hard-coded). Bound from "Automation:Reporting".
/// </summary>
public sealed class ReportingAutomationOptions
{
    public const string SectionName = "Automation:Reporting";

    /// <summary>UTC hour at which the daily run fires (0 = midnight UTC).</summary>
    public int RunAtHourUtc { get; set; }

    /// <summary>How often the scheduler checks whether today's report is due.</summary>
    public int ScanIntervalSeconds { get; set; } = 300;

    /// <summary>How many top products the report lists.</summary>
    public int TopProductCount { get; set; } = 5;
}

/// <summary>
/// Pure helper for the report window (spec §7). UTC-only on purpose: a report
/// day must mean exactly one thing regardless of server locale.
/// </summary>
public static class ReportWindow
{
    public static DateTime StartOfDayUtc(DateTime dateUtc) =>
        DateTime.SpecifyKind(dateUtc.Date, DateTimeKind.Utc);

    public static (DateTime FromUtc, DateTime ToUtc) ForDay(DateTime dateUtc)
    {
        var start = StartOfDayUtc(dateUtc);
        return (start, start.AddDays(1));
    }
}