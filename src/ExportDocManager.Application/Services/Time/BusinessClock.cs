namespace ExportDocManager.Services.Time;

public interface IBusinessClock
{
    string TimeZoneId { get; }

    DateTimeOffset UtcNow { get; }

    DateTimeOffset Now { get; }

    DateOnly Today { get; }

    DateTimeOffset TodayValidUntilUtc { get; }

    DateTimeOffset InterpretLocal(DateTime localDateTime);
}

public sealed class BusinessClock : IBusinessClock
{
    public const string DefaultTimeZoneId = "Asia/Shanghai";

    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone;

    public BusinessClock(TimeProvider timeProvider, string timeZoneId)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId.Trim();
        _timeZone = ResolveTimeZone(TimeZoneId);
    }

    public string TimeZoneId { get; }

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(UtcNow, _timeZone);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public DateTimeOffset TodayValidUntilUtc
    {
        get
        {
            DateTimeOffset now = Now;
            DateTime nextMidnight = DateTime.SpecifyKind(now.Date.AddDays(1), DateTimeKind.Unspecified);
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(nextMidnight, _timeZone), TimeSpan.Zero);
        }
    }

    public DateTimeOffset InterpretLocal(DateTime localDateTime)
    {
        DateTime unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        if (_timeZone.IsInvalidTime(unspecified))
        {
            throw new ArgumentException(
                $"本地业务时间位于时区跳时空档：{unspecified:yyyy-MM-dd HH:mm:ss}",
                nameof(localDateTime));
        }

        TimeSpan offset = _timeZone.IsAmbiguousTime(unspecified)
            ? _timeZone.GetAmbiguousTimeOffsets(unspecified).Max()
            : _timeZone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }

    public static IBusinessClock CreateSystem(string? timeZoneId = null) =>
        new BusinessClock(TimeProvider.System, timeZoneId ?? DefaultTimeZoneId);

    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        string normalized = string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(normalized);
        }
        catch (TimeZoneNotFoundException) when (normalized == DefaultTimeZoneId)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
    }
}
