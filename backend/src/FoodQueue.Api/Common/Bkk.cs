namespace FoodQueue.Api.Common;

/// <summary>เวลาของร้านในเขตเวลาไทย — ใช้ตัดวันขายของรายงานและเลขใบเสร็จ</summary>
public static class Bkk
{
    public static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Asia/Bangkok", "SE Asia Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // ลอง id ถัดไป
            }
            catch (InvalidTimeZoneException)
            {
                // ลอง id ถัดไป
            }
        }

        return TimeZoneInfo.Utc;
    }

    public static DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);
    public static DateOnly DateOf(DateTimeOffset utc) => DateOnly.FromDateTime(ToLocal(utc).DateTime);
    public static DateTimeOffset StartOfDay(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), Zone.GetUtcOffset(date.ToDateTime(TimeOnly.MinValue)));
}
