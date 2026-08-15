namespace ExportDocManager.Services.Time;

public interface IBusinessClock
{
    DateTimeOffset UtcNow { get; }

    DateTimeOffset Now { get; }

    DateOnly Today { get; }
}

public sealed class BusinessClock : IBusinessClock
{
    public const string DefaultTimeZoneId = "Asia/Shanghai";

    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone;

    public BusinessClock(TimeProvider timeProvider, string timeZoneId)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _timeZone = ResolveTimeZone(timeZoneId);
    }

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(UtcNow, _timeZone);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

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
